using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Kasa/Banka işlemleri: tahsilat (cash in), ödeme/tediye (cash out), kasa↔banka virman,
/// ters kayıt. Her işlem DENGELİ çift-taraflı defter kümesi yazar. Düzeltme = ters kayıt.
///
///   Tahsilat: Borç Hesap(Kasa/Banka) / Alacak Cari   → cari bakiye ↓
///   Ödeme:    Borç Cari / Alacak Hesap(Kasa/Banka)    → cari bakiye ↑ (mahsup)
///   Virman:   Borç Hedef / Alacak Kaynak (cari yok)
///   Ters:     orijinalin yönleri çevrilir.
/// </summary>
public sealed class CashService(
    ICashRepository repository, ILedgerPoster ledger, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    ICustomerRepository customers, RentACar.Application.Kur.ExchangeRateResolver exchangeRateResolver,
    RentACar.Application.FinancialAccounts.AccountResolver accountResolver)
{
    private readonly ICashRepository _repository = repository;
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly ICustomerRepository _customers = customers;
    private readonly RentACar.Application.Kur.ExchangeRateResolver _exchangeRateResolver = exchangeRateResolver;
    private readonly RentACar.Application.FinancialAccounts.AccountResolver _accountResolver = accountResolver;

    public Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-67 — süzgeçli nakit işlem listesi (cari adı/kodu çözümlenmiş).</summary>
    public Task<IReadOnlyList<NakitIslemSatirDto>> SearchTransactionsAsync(
        CashFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.SearchTransactionsAsync(filter, ct);
    }

    public Task<CashTransaction?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public Task<decimal> GetAccountBalanceAsync(Guid customerId, CancellationToken ct = default)
        => _repository.GetAccountBalanceAsync(customerId, ct);

    /// <summary>
    /// Cari ekstresi (satırlar + devir). <paramref name="filter"/> null → carinin TÜM hareketleri.
    /// Devir yalnız tarih alt sınırı verildiğinde dolar; bkz. <see cref="CariEkstreSonuc"/>.
    /// </summary>
    /// <summary>
    /// FAZ-59 — cari↔cari virman geçmişi (tüm cariler). Salt okuma; tutar defterden gelir.
    /// </summary>
    /// <summary>FAZ-50 — kasa/banka virman geçmişi (künye + defterden tutar). Salt okuma.</summary>
    public Task<IReadOnlyList<KasaVirmanSatirDto>> ListCashTransfersAsync(
        KasaVirmanFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.ListCashTransfersAsync(filter, ct);
    }

    public Task<IReadOnlyList<CariVirmanSatirDto>> ListAccountTransfersAsync(
        CariVirmanFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.ListAccountTransfersAsync(filter, ct);
    }

    public Task<CariEkstreSonuc> GetStatementAsync(
        Guid customerId, CariEkstreFilter? filter = null, CancellationToken ct = default)
        => _repository.GetAccountStatementAsync(customerId, filter, ct);

    /// <summary>Kira başına işlem SAYISI (ters kayıt dahil, monoton) — deterministik tahsilat
    /// anahtarının zamansal bileşeni. Kayıtsız kira sözlükte yok → TryGetValue→0.</summary>
    public Task<Dictionary<Guid, int>> GetRentalTransactionCountsAsync(
        IReadOnlyCollection<Guid> rentalIds, CancellationToken ct = default)
        => _repository.GetRentalTransactionCountsAsync(rentalIds, ct);

    /// <summary>F4.4 adversarial HIGH-1: verilen <c>IslemAnahtari</c> ile yazılmış kasa/banka işlemi (yoksa null;
    /// kiracı RLS'i + tenant filtresi). Deterministik tahsilat anahtarının YENİDEN HESAPLANMASINDAN ÖNCE bakılır:
    /// kaybolan yanıttan sonraki birebir tekrar "kayıt değişti" değil "zaten kaydedildi" almalı.</summary>
    public Task<CashTransaction?> FindByOperationKeyAsync(Guid operationKey, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return _repository.FindByOperationKeyAsync(operationKey, ct);
    }

    /// <summary>Tahsilat (cash in): Borç Hesap / Alacak Cari.</summary>
    public Task<Guid> CollectAsync(CashInput input, CancellationToken ct = default)
        => PostCashAsync(input, CashTransactionType.Tahsilat, ct);

    /// <summary>Ödeme/tediye (cash out): Borç Cari / Alacak Hesap.</summary>
    public Task<Guid> PayAsync(CashInput input, CancellationToken ct = default)
        => PostCashAsync(input, CashTransactionType.Odeme, ct);

    private async Task<Guid> PostCashAsync(CashInput input, CashTransactionType tip, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (input.Tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");
        DatePolicy.MoneyDate(input.Tarih, "İşlem"); // savunma: gelecek tarih reddi (geçmiş dönem-kilidinde)
        EnsureCashBank(input.Hesap);
        // F4.4a adversarial MEDIUM-2: cari kiracı içinde GERÇEKTEN var olmalı. Toplu yolla ORTAK kural.
        await EnsureCustomersExistAsync([input.CariId], _ => "cariId", ct);

        // Kur çözümü (1.1b): açık kur aynen; boş → TRY=1 / döviz KurService (yoksa net red — sessiz 1 YOK).
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(input.Doviz, input.Kur, input.Tarih, ct);
        var resolvedAccount = await _accountResolver.ResolveAsync(input.HesapId, input.Hesap, ct, input.Doviz); // FAZ-50
        var money = new Money(input.Tutar, RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(input.Doviz), resolvedRate);
        // FAZ-84: kanal SAF BİLGİ — CashTransaction belgesine yazılır, defter/bakiye hiç görmez.
        var channel = NormalizeChannelOrThrow(input.Kanal);
        var tx = new CashTransaction
        {
            Tip = tip,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            Amount = money,
            KarsiHesap = input.Hesap,
            HesapId = resolvedAccount,
            Aciklama = input.Aciklama,
            IslemAnahtari = input.IslemAnahtari is { } k && k != Guid.Empty ? k : null, // adversarial M5: çift-submit dedup
            Kanal = channel
        };
        await _lock.EnsureOpenAsync(tx.Tarih, ct); // dönem kilidi: kapalı tarihe tahsilat/ödeme YOK

        var entries = Natural(tx);
        // Kira bağlıysa Tahsilat/Bakiye repo'da tx'ten türetilir (yön + kira dövizi, atomik — K2/O1).
        await _repository.PostAsync(tx, entries, ct);
        if (tip == CashTransactionType.Tahsilat) RentACar.Application.Observability.RacarMetrics.CollectionOk(); // metrik
        return tx.Id;
    }

    /// <summary>Toplu tahsilat (parite #10): çok cariye tek seferde tahsilat. ATOMİK (hep-ya-hiç) +
    /// satır-bazlı dengeli. <paramref name="batchKey"/> verilirse her satır deterministik idempotency
    /// anahtarı alır → çift-submit tüm batch'i geri alır. Bir satır geçersizse HİÇBİRİ yazılmaz.</summary>
    public Task BatchCollectAsync(
        IReadOnlyList<CashInput> rows, Guid? batchKey = null, CancellationToken ct = default)
        => BatchCashAsync(rows, CashTransactionType.Tahsilat, batchKey, ct);

    /// <summary>Toplu ödeme/tediye: çok cariye tek seferde ödeme. ATOMİK + idempotent (bkz. BatchCollectAsync).</summary>
    public Task BatchPayAsync(
        IReadOnlyList<CashInput> rows, Guid? batchKey = null, CancellationToken ct = default)
        => BatchCashAsync(rows, CashTransactionType.Odeme, batchKey, ct);

    private async Task BatchCashAsync(
        IReadOnlyList<CashInput> rows, CashTransactionType tip, Guid? batchKey, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (rows.Count == 0) throw new ValidationException("Toplu işlem en az bir satır içermelidir.");
        if (rows.Count > 500) throw new ValidationException("Toplu işlem en çok 500 satır olabilir.");
        foreach (var s in rows) DatePolicy.MoneyDate(s.Tarih, "İşlem"); // savunma (adversarial BULGU 3: tek yolla simetri)

        // Dönem kilidi: kapanış tarihini bir kez oku, her satırın tarihini yerelde karşılaştır.
        var closing = await _lock.GetClosingDateAsync(ct);

        // TÜM satırlar önce doğrulanır (fail-fast) → repo'ya yalnız geçerli set gider; atomiklik repo'da.
        var exchangeRateCache = new Dictionary<(string, DateTime?), decimal>();
        var accountCache = new Dictionary<(Guid, LedgerAccountType), Guid?>(); // FAZ-50: satır-bazlı hesap doğrulaması
        var postings = new List<CashPosting>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var input = rows[i];
            if (input.CariId == Guid.Empty) throw new ValidationException($"Satır {i + 1}: cari seçilmelidir.");
            if (input.Tutar <= 0) throw new ValidationException($"Satır {i + 1}: tutar pozitif olmalıdır.");
            EnsureCashBank(input.Hesap);

            // Kur çözümü (1.1b) — açık kur satır-bazlı ÖNCE (satır-önekli guard); otomatik çözüm
            // (kod, gün) önbelleğiyle (500 satırda tek lookup).
            decimal resolvedRate;
            if (input.Kur is { } open)
            {
                // FAZ-82: açık kur da KurCozucu'dan GEÇER. Önce burada yerel bir pozitiflik kontrolü
                // yapılıp değer aynen alınıyordu; bu, "kur elle girilemez" tenant kilidinin TOPLU
                // tahsilat/ödeme ekranından dolanılmasına açık kapı bırakıyordu (tekil yol kilitli,
                // toplu yol serbest). Satır öneki korunuyor (500 satırda hangi satır olduğu şart).
                resolvedRate = await LineExchangeRateAsync(input.Doviz, open, input.Tarih, i, ct);
            }
            else
            {
                var exchangeRateKey = (RentACar.Application.Kur.ExchangeRateService.NormalizeCode(input.Doviz), input.Tarih?.UtcDateTime.Date);
                if (!exchangeRateCache.TryGetValue(exchangeRateKey, out resolvedRate))
                    exchangeRateCache[exchangeRateKey] = resolvedRate = await _exchangeRateResolver.ResolveAsync(input.Doviz, null, input.Tarih, ct);
            }

            // FAZ-50: satır-bazlı hesap seçimi (aynı hesap tekrar ediyorsa tek doğrulama).
            Guid? resolvedAccount = null;
            if (input.HesapId is { } hid && hid != Guid.Empty)
            {
                if (!accountCache.TryGetValue((hid, input.Hesap), out resolvedAccount))
                    accountCache[(hid, input.Hesap)] = resolvedAccount =
                        await _accountResolver.ResolveAsync(hid, input.Hesap, ct, input.Doviz);
            }

            var money = new Money(input.Tutar, RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(input.Doviz), resolvedRate);
            // FAZ-84: satır-bazlı kanal (bozuk değer önekli hata — 500 satırda hangisi olduğu şart).
            string? channel;
            try { channel = NormalizeChannelOrThrow(input.Kanal); }
            catch (ValidationException ex) { throw new ValidationException($"Satır {i + 1}: {ex.Message}"); }
            var tx = new CashTransaction
            {
                Tip = tip,
                CariId = input.CariId,
                RentalId = input.RentalId,
                Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
                Amount = money,
                KarsiHesap = input.Hesap,
                HesapId = resolvedAccount,
                Aciklama = input.Aciklama,
                IslemAnahtari = batchKey is { } b ? RowKey(b, i) : null,
                Kanal = channel
            };
            PeriodLock.ThrowIfClosed(tx.Tarih, closing, $"Satır {i + 1}"); // dönem kilidi (satır-bazlı)
            postings.Add(new CashPosting(tx, Natural(tx)));
        }

        // Cari varlığı (DEVIR §6 Low): tekil yoldaki kuralın AYNISI, TÜM satırlar için defter yazımından ÖNCE.
        // Olmayan ya da başka kiracının carisi → tüm parti reddedilir (hep-ya-hiç; yetim AccountRef'li küme yok).
        // Diğer satır doğrulamalarından SONRA: onların mesajları/önceliği değişmez.
        await EnsureCustomersExistAsync(rows.Select(s => s.CariId).ToList(), i => $"satirlar[{i}].cariId", ct,
            i => $"Satır {i + 1}: ");

        await _repository.PostBatchAsync(postings, ct);
    }

    /// <summary>F4.4a adversarial MEDIUM-2 (tekil) + DEVIR §6 Low (toplu): cari kiracı içinde GERÇEKTEN var olmalı
    /// (tenant sorgu filtresi + RLS → başka kiracının ya da hiç olmayan kimlik "yok" sayılır). Önce rastgele ya da
    /// başka kiracının cari kimliğiyle tahsilat/ödeme yazılabiliyor, defterde hiçbir ekstrede görünmeyen yetim
    /// AccountRef'li küme kalıyordu. İlk eksik satır <paramref name="alan"/>(index) ile reddedilir.</summary>
    private async Task EnsureCustomersExistAsync(
        IReadOnlyList<Guid> customerIds, Func<int, string> alan, CancellationToken ct, Func<int, string>? prefix = null)
    {
        var known = await _customers.ExistingIdsAsync(customerIds, ct);
        for (var i = 0; i < customerIds.Count; i++)
            if (!known.Contains(customerIds[i]))
                throw new ValidationException($"{prefix?.Invoke(i)}Cari bulunamadı.", alan(i));
    }

    /// <summary>FAZ-82: toplu satırda AÇIK kur çözümü — kural tek kaynaktan (KurCozucu: pozitiflik +
    /// tenant elle-giriş kilidi), hata mesajı satır önekiyle zenginleştirilerek yeniden fırlatılır.</summary>
    private async Task<decimal> LineExchangeRateAsync(
        string? currency, decimal open, DateTimeOffset? date, int index, CancellationToken ct)
    {
        try { return await _exchangeRateResolver.ResolveAsync(currency, open, date, ct); }
        catch (ValidationException ex) { throw new ValidationException($"Satır {index + 1}: {ex.Message}"); }
    }

    /// <summary>Toplu işlem anahtarından satır-bazlı deterministik idempotency anahtarı (batch ⊕ index).
    /// FAZ 4.2-B3: dönem tahsilatı da (rentalId ⊕ donemSira) deterministik anahtarını buradan üretir —
    /// çift-submit/job-tekrarı ikinci tahsilat yazamaz (kısmi unique index + yutma).</summary>
    public static Guid RowKey(Guid batch, int index)
    {
        var b = batch.ToByteArray();
        b[0] ^= (byte)(index & 0xFF);
        b[1] ^= (byte)((index >> 8) & 0xFF);
        b[2] ^= (byte)((index >> 16) & 0xFF);
        b[3] ^= (byte)((index >> 24) & 0xFF);
        return new Guid(b);
    }

    /// <summary>
    /// FAZ-29 — <b>tek cari, ekstresinden seç, toplu kapat.</b> Seçilen BORÇ satırlarının baz
    /// toplamı kadar TEK tahsilat postlar.
    ///
    /// <para><b>KALEM-BAZLI TAHSİS:</b> hangi tahsilatın hangi borç kalemini ne kadar kapattığı
    /// <see cref="KapatmaTahsis"/> tablosuna yazılır. Bu kayıt olmadan aynı kalem defalarca
    /// kapatılabiliyordu — adversarial inceleme ampirik gösterdi (100+900 borçta 100'lük kalem
    /// kapatılıp bakiye 900'e indikten sonra AYNI kalem yeniden seçilince "bakiyeyi aşmıyor" çiti
    /// geçiyor, ALINMAMIŞ tahsilat yazılıyordu). <b>Bakiye çiti tek başına YETERSİZDİR.</b></para>
    ///
    /// <para><b>Kısmi kapatma:</b> her kalem için ayrı tutar verilebilir; bir kaleme tahsis edilen
    /// TOPLAM, o satırın baz tutarını aşamaz. Kalan açık kalır ve ekranda "400/1000" görünür.</para>
    ///
    /// <para><b>Seçim doğrulaması:</b> her id, O CARİNİN ekstresinde bulunan bir BORÇ satırı
    /// olmalıdır. Başka cariye/tenant'a ait ya da alacak satırı id'si gürültülü reddedilir —
    /// sessizce atlamak, kullanıcının seçtiğini sandığından farklı bir tutar tahsil ederdi.</para>
    ///
    /// <para><b>Kira bağı:</b> seçilen kalemlerin hepsi AYNI kiranın faturasından geliyorsa
    /// tahsilat o kiraya bağlanır (kira bakiyesi + tahsilat-mutabakat raporu cari ekstresiyle
    /// tutarlı kalır). Karışık seçimde bağ kurulmaz — tek tahsilatı iki kiraya atfetmek yanlış
    /// olurdu.</para>
    ///
    /// <para><b>Yarış güvenliği:</b> bakiye ve tahsis kontrolleri repo'da, <c>(tenant, cari)</c>
    /// danışma kilidinin arkasında ve kayıtla AYNI transaction'da tekrarlanır (adversarial H2).</para>
    ///
    /// <para><b>İdempotency:</b> anahtar çağırandan gelir (form render'ı başına tek token) →
    /// çift-submit kısmi unique index'te çakışır. Değerden türetilen bir anahtar burada YANLIŞ
    /// olurdu: aynı cari, aynı gün, aynı tutar meşru biçimde iki kez tahsil edilebilir.</para>
    /// </summary>
    /// <param name="selection">satırId → kapatılacak baz tutar. Tutar null/0 ise kalemin KALANI kapatılır.</param>
    /// <returns>Postlanan tahsilatın tutarı (baz para).</returns>
    public async Task<decimal> CloseSingleAccountBulkAsync(
        Guid customerId, IReadOnlyDictionary<Guid, decimal?> selection, LedgerAccountType account,
        DateTimeOffset? date = null, string? description = null, Guid? operationKey = null,
        string? channel = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (customerId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (selection.Count == 0) throw new ValidationException("En az bir kalem seçilmelidir.");
        EnsureCashBank(account);

        // F1.4 — ANAHTAR ÖNCE: aynı anahtarla ikinci gönderim, tahsis/bakiye ön-kontrollerinden ÖNCE
        // mükerrer sayılır (repo aynı kontrolü kilidin arkasında tekrarlar). Yoksa sonuç ilk gönderimin
        // kalemi tam mı kısmi mi kapattığına göre 400 ↔ 409 değişiyordu.
        if (operationKey is { } previousKey && previousKey != Guid.Empty
            && await _repository.OperationKeyExistsAsync(previousKey, ct))
            throw new DuplicateOperationException("Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).");

        // Ekstre O CARİ için okunur → başka carinin satırı burada zaten bulunamaz; tenant sınırı
        // ayrıca RLS + query filter ile korunur.
        var statement = await _repository.GetAccountStatementAsync(customerId, null, ct);
        var index = statement.Satirlar.ToDictionary(x => x.Id);
        var settled = await _repository.GetAllocationTotalsAsync([.. selection.Keys], ct);

        var allocations = new List<KapatmaTahsis>(selection.Count);
        var totalRaw = 0m;
        foreach (var (id, requestedAmount) in selection)
        {
            if (!index.TryGetValue(id, out var row))
                throw new ValidationException("Seçilen kalemlerden biri bu carinin ekstresinde bulunamadı.");
            if (row.Direction != LedgerDirection.Debit)
                throw new ValidationException("Yalnız BORÇ kalemleri kapatılabilir; seçimde alacak kalemi var.");

            var rowBase = row.Amount.AmountInBase;
            var previous = settled.TryGetValue(id, out var t0) ? t0 : 0m;
            var remaining = rowBase - previous;
            if (remaining <= 0.005m)
                throw new ValidationException("Seçilen kalemlerden biri zaten tamamen kapatılmış.");

            // Tutar verilmediyse KALANI kapat. Kuruşa AŞAĞI yuvarlanır (adversarial M3): yukarı
            // yuvarlamak kalemin/bakiyenin üstüne çıkıp bakiyeyi eksiye düşürüyordu.
            var amount = requestedAmount is { } v && v > 0m
                ? decimal.Round(v, 2, MidpointRounding.ToZero)
                : decimal.Round(remaining, 2, MidpointRounding.ToZero);
            if (amount <= 0m) throw new ValidationException("Kapatma tutarı pozitif olmalıdır.");
            if (amount > remaining + 0.005m)
                throw new ValidationException(
                    $"Kapatma tutarı kalemin kalanını aşıyor (kalan {remaining:N2}, istenen {amount:N2}).");

            totalRaw += amount;
            allocations.Add(new KapatmaTahsis
            { LedgerEntryId = id, CariId = customerId, KapatilanBaz = amount });
        }

        if (totalRaw <= 0m) throw new ValidationException("Seçilen kalemlerin toplamı pozitif olmalıdır.");

        // Bakiye ön-kontrolü YALNIZ iyi hata mesajı içindir; ASIL çit repo'da, kilidin arkasında.
        var balance = await _repository.GetAccountBalanceAsync(customerId, ct);
        if (balance <= 0m)
            throw new ValidationException("Carinin kapatılacak borcu yok (bakiye borçlu değil).");
        if (totalRaw > balance)
            throw new ValidationException(
                $"Seçilen tutar ({totalRaw:N2}) carinin güncel borcunu ({balance:N2}) aşıyor.");

        var rentalId = await ResolveRentalLinkAsync(selection.Keys, index, ct);

        // Tahsilat BAZ parada postlanır: seçim karışık dövizli satırlardan gelebilir, tek bir
        // döviz seçmek toplamı bozardı (AmountInBase toplandı).
        var tx = new CashTransaction
        {
            Tip = CashTransactionType.Tahsilat,
            CariId = customerId,
            RentalId = rentalId,
            Tarih = date ?? DateTimeOffset.UtcNow,
            Amount = new Money(totalRaw, "TRY", 1m),
            KarsiHesap = account,
            Aciklama = description ?? $"Toplu kapatma ({allocations.Count} kalem)",
            IslemAnahtari = operationKey is { } k && k != Guid.Empty ? k : null,
            Kanal = NormalizeChannelOrThrow(channel) // FAZ-84
        };
        DatePolicy.MoneyDate(tx.Tarih, "İşlem");
        await _lock.EnsureOpenAsync(tx.Tarih, ct);      // dönem kilidi

        foreach (var t in allocations) t.CashTransactionId = tx.Id;
        await _repository.PostAccountClosingAsync(customerId, tx, Natural(tx), allocations, ct);
        RentACar.Application.Observability.RacarMetrics.CollectionOk();
        return totalRaw;
    }

    /// <summary>Verilen borç satırları için şu ana kadar KAPATILMIŞ baz tutarlar (ekran "kapalı /
    /// kısmi" göstergesi). Servisin çit kurarken kullandığı kaynağın AYNISI — ekran ayrı bir hesap
    /// yapsaydı gösterge ile çit ayrışabilirdi.</summary>
    public Task<Dictionary<Guid, decimal>> SettledAmountsAsync(
        IReadOnlyCollection<Guid> ledgerEntryIds, CancellationToken ct = default)
        => _repository.GetAllocationTotalsAsync(ledgerEntryIds, ct);

    /// <summary>Kalemleri TAMAMEN kapatan kısayol (kısmi tutar verilmez) — sözleşme aynıdır.</summary>
    public Task<decimal> CloseSingleAccountBulkAsync(
        Guid customerId, IReadOnlyCollection<Guid> selectedLineIds, LedgerAccountType account,
        DateTimeOffset? date = null, string? description = null, Guid? operationKey = null,
        string? channel = null, CancellationToken ct = default)
        => CloseSingleAccountBulkAsync(
            customerId, selectedLineIds.Distinct().ToDictionary(x => x, _ => (decimal?)null),
            account, date, description, operationKey, channel, ct);

    /// <summary>Kapatılan kalemlerin HEPSİ aynı kiranın faturasından geliyorsa o kira; aksi halde
    /// null (tek tahsilatı iki kiraya atfetmek yanlış olurdu).</summary>
    private async Task<Guid?> ResolveRentalLinkAsync(
        IEnumerable<Guid> ids, IReadOnlyDictionary<Guid, AccountLedgerEntry> index, CancellationToken ct)
    {
        var invoiceIds = ids.Select(i => index[i])
            .Where(e => string.Equals(e.SourceType, "Fatura", StringComparison.Ordinal) && e.SourceId != Guid.Empty)
            .Select(e => e.SourceId).Distinct().ToList();
        if (invoiceIds.Count == 0) return null;

        var rentals = await _repository.InvoiceRentalsAsync(invoiceIds, ct);
        // Faturasız/kirasız bir kalem varsa da bağ kurma: seçim homojen değil.
        if (rentals.Count != invoiceIds.Count) return null;
        var unique = rentals.Values.Distinct().ToList();
        return unique.Count == 1 ? unique[0] : null;
    }

    /// <summary>
    /// Kasa↔Banka virman (transfer): Borç Hedef / Alacak Kaynak. Belgesiz (dengeli defter).
    ///
    /// <para><b>FAZ-50 — aynı türde iki hesap arası virman ARTIK MÜMKÜN.</b> Önceden kontrol enum
    /// düzeyindeydi: iki farklı banka hesabı da sistemin gözünde "Banka" olduğundan Ziraat→İş Bankası
    /// aktarımı reddediliyordu. Artık kaynak/hedef SPESİFİK hesap (<paramref name="sourceAccountId"/>/
    /// <paramref name="targetAccountId"/>) ile ayrışır.</para>
    ///
    /// <para><b>Aynı türde virmanda iki hesap da ZORUNLU</b> (bilinçli sıkılaştırma): yalnız biri
    /// verilseydi para, "hesap belirtilmemiş" legacy kovası ile gerçek bir hesap arasında akar ve
    /// geçmiş kayıtların toplandığı o kovanın bakiyesi sebepsiz oynardı.</para>
    ///
    /// <para><b>Künye</b> (makbuz no / işlem şubesi) defter DIŞINDA
    /// <see cref="KasaVirmanBilgi"/>'ye aynı transaction'da yazılır — <c>CariVirmanBilgi</c> deseni.</para>
    /// </summary>
    public async Task TransferAsync(
        LedgerAccountType source, LedgerAccountType target, decimal amount,
        string? currency = "TRY", decimal? exchangeRate = null, string? description = null,
        Guid? operationKey = null,
        // FAZ-50 — spesifik hesap + künye. Hepsi opsiyonel: verilmezse eski davranış birebir korunur.
        Guid? sourceAccountId = null, Guid? targetAccountId = null,
        string? receiptNo = null, string? branch = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        EnsureCashBank(source);
        EnsureCashBank(target);
        if (amount <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");

        // Hesap doğrulaması guard'lardan ÖNCE: uydurma/başka tenant'ın hesabı buradan geri döner.
        var sourceRef = await _accountResolver.ResolveAsync(sourceAccountId, source, ct, currency);
        var targetRef = await _accountResolver.ResolveAsync(targetAccountId, target, ct, currency);

        // ADVERSARIAL M1 — kural TÜRDEN BAĞIMSIZ: bir taraf hesap seçilmişse diğeri de seçilmeli.
        // Önce yalnız aynı-tür dalında kontrol ediliyordu; Kasa→Banka virmanında tek taraf seçmek
        // serbestti ve paranın diğer ucu "hesap belirtilmemiş" kovasına düşüp o kovanın bakiyesini
        // tam da yasaklanan şekilde oynatıyordu.
        if ((sourceRef is null) != (targetRef is null))
            throw new ValidationException(
                "Virmanda bir taraf için hesap seçtiyseniz diğer taraf için de seçmelisiniz " +
                "(aksi hâlde paranın bir ucu 'hesap belirtilmemiş' kovasına düşer).");
        if (source == target)
        {
            if (sourceRef is null || targetRef is null)
                throw new ValidationException(
                    "Aynı türdeki iki hesap arasında virman için kaynak ve hedef hesabı ayrı ayrı seçin.");
            if (sourceRef == targetRef)
                throw new ValidationException("Kaynak ve hedef hesap farklı olmalıdır.");
        }

        await _lock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // dönem kilidi (virman bugün tarihli)
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(currency, exchangeRate, null, ct); // 1.1b: bugünkü kur
        var money = new Money(amount, RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(currency), resolvedRate);
        // İdempotency (pre-launch takip): token verilirse SourceId o olur → kısmi unique index çift-submit'i yutar.
        var sourceId = operationKey is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = description ?? $"Virman {source}→{target}";
        var now = DateTimeOffset.UtcNow;
        var profile = new KasaVirmanBilgi
        {
            Id = sourceId, KaynakTur = source, HedefTur = target,
            KaynakHesapId = sourceRef, HedefHesapId = targetRef,
            Tarih = now, MakbuzNo = Clamp(receiptNo), Sube = Clamp(branch),
            IslemYapan = _currentUser.UserName, Aciklama = Clamp(description)
        };
        await _ledger.PostWithAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = now, AccountType = target, AccountRef = targetRef,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = now, AccountType = source, AccountRef = sourceRef,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc }
        ], profile, ct);

        static string? Clamp(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    }

    /// <summary>
    /// Cari↔cari virman (parite #9): iki cari arası bakiye aktarımı. DENGELİ çift kayıt —
    /// hedef cari Borç (Debit, bakiye +), kaynak cari Alacak (Credit, bakiye −); ikisi de AccountType=Cari,
    /// iki farklı AccountRef, aynı Money → Σ borç(base) = Σ alacak(base). LedgerPoster dengeyi zorlar.
    /// Belge/No yazmaz (TransferAsync deseni). Her iki carinin ekstresinde görünür.
    ///
    /// İDEMPOTENCY: <paramref name="operationKey"/> verilirse SourceId o olur ve kısmi unique index
    /// (SourceType='CariVirman') çift-submit'i yutar (web formu her açılışta sabit token gönderir →
    /// çift tıklama tek virman). Verilmezse her çağrı AYRI virmandır (Guid.NewGuid).
    /// DÜZELTME: ledger-only (CashTransaction/storno yok) → düzeltme, AYNI KUR ile ters yön virmandır
    /// (kaynak↔hedef değiş); FARKLI kurda baz para kalıntısı kalır (bakiye baz-para'da tutulur).
    /// DİKKAT (1.1b): kur BOŞ bırakılırsa GÜNÜN kuru çözülür — ertesi gün ters kayıtta kalıntı
    /// oluşmaması için düzeltmede ORİJİNAL kuru AÇIKÇA girin.
    /// </summary>
    public async Task TransferBetweenAccountsAsync(
        Guid sourceCustomerId, Guid targetAccountId, decimal amount,
        string? currency = "TRY", decimal? exchangeRate = null, string? description = null,
        Guid? operationKey = null, CancellationToken ct = default,
        // FAZ-59 künye alanları — PARAYA DOKUNMAZ, ayrı tabloya yazılır.
        DateTimeOffset? date = null, DateTimeOffset? due = null,
        string? receiptNo = null, string? branch = null)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (sourceCustomerId == Guid.Empty || targetAccountId == Guid.Empty)
            throw new ValidationException("Kaynak ve hedef cari seçilmelidir.");
        if (sourceCustomerId == targetAccountId)
            throw new ValidationException("Kaynak ve hedef cari farklı olmalıdır.");
        if (amount <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");

        // FAZ-59: tarih artık ELLE girilebiliyor (önce her zaman "şimdi"ydi). Dolayısıyla dönem
        // kilidi de VERİLEN tarihe göre kontrol edilmeli — yoksa kapalı bir döneme geriye dönük
        // virman atılabilirdi. Gelecek tarih para kaydında yasak (TarihPolitikasi).
        var transactionDate = date ?? DateTimeOffset.UtcNow;
        DatePolicy.MoneyDate(transactionDate, "Virman");
        if (due is { } v && v < transactionDate.Date)
            throw new ValidationException("Vade tarihi virman tarihinden önce olamaz.");
        await _lock.EnsureOpenAsync(transactionDate, ct);
        // L2: her iki cari tenant içinde GERÇEKTEN var olmalı (FindAsync RLS+query-filter → yoksa null).
        // Aksi halde bakiye var-olmayan bir "hayalet" cari ekstresine taşınırdı (tenant-içi bütünlük).
        if (await _customers.FindAsync(sourceCustomerId, ct) is null)
            throw new ValidationException("Kaynak cari bulunamadı.");
        if (await _customers.FindAsync(targetAccountId, ct) is null)
            throw new ValidationException("Hedef cari bulunamadı.");
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(currency, exchangeRate, null, ct); // 1.1b
        var money = new Money(amount, RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(currency), resolvedRate);
        var sourceId = operationKey is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = description ?? "Cari virman";
        // Künye defterle AYNI transaction'da yazılır: "defter var künye yok" durumu oluşamaz.
        // Künye PARA TAŞIMAZ — tutar/döviz/kur yalnız defterde (tek kaynak).
        var profile = new CariVirmanBilgi
        {
            Id = sourceId, KaynakCariId = sourceCustomerId, HedefCariId = targetAccountId,
            Tarih = transactionDate, Vade = due,
            MakbuzNo = Clamp(receiptNo), Sube = Clamp(branch),
            IslemYapan = _currentUser.UserName, Aciklama = Clamp(description)
        };
        await _ledger.PostWithAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = transactionDate, AccountType = LedgerAccountType.Cari,
                AccountRef = targetAccountId, Direction = LedgerDirection.Debit, Amount = money,
                SourceType = "CariVirman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = transactionDate, AccountType = LedgerAccountType.Cari,
                AccountRef = sourceCustomerId, Direction = LedgerDirection.Credit, Amount = money,
                SourceType = "CariVirman", SourceId = sourceId, Description = desc }
        ], profile, ct);

        static string? Clamp(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    }

    public async Task<Guid> ReverseAsync(Guid cashTransactionId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceReverse); // inceltme: defteri geri sarma ayrı izin
        var original = await _repository.FindAsync(cashTransactionId, ct)
            ?? throw new ValidationException("İşlem bulunamadı.");
        if (original.TersKayitMi)
            throw new ValidationException("Ters kayıt tekrar ters alınamaz.");
        // F1.4: ikinci ters kayıt MÜKERRER gönderimdir — yarışta DB kısıtı (TersAlinanId kısmi unique)
        // zaten MukerrerIslemException (409) veriyor; sıralı ikinci istek de AYNI tipi almalı, yoksa
        // sonuç zamanlamaya bağlı olurdu (400 ↔ 409).
        if (await _repository.HasReversalAsync(cashTransactionId, ct))
            throw new DuplicateOperationException("Bu işlem zaten ters kaydedilmiş.");

        var reversal = new CashTransaction
        {
            Tip = original.Tip,
            CariId = original.CariId,
            RentalId = original.RentalId,
            Tarih = DateTimeOffset.UtcNow,
            Amount = original.Amount,
            KarsiHesap = original.KarsiHesap,
            // FAZ-50: ters kayıt ORİJİNAL hesaba yazılır — para hangi kasadan girdiyse oradan çıkar.
            HesapId = original.HesapId,
            Aciklama = $"Ters kayıt: {original.No}",
            TersKayitMi = true,
            TersAlinanId = original.Id,
            Kanal = original.Kanal // FAZ-84 adversarial: ters kayıt orijinalin kanalını KAYBETMEMELİ
        };
        await _lock.EnsureOpenAsync(reversal.Tarih, ct); // dönem kilidi: ters kayıt bugün tarihli postlanır

        // Orijinalle aynı hesap/cari/tutar; doğal yönler çevrilir, ters kayıt tarihiyle.
        // Kira deltası repo'da türetilir: TersKayitMi=true yönü çevirir (tahsilat tersi → Tahsilat azalır).
        var entries = Natural(reversal, flip: true);
        await _repository.PostAsync(reversal, entries, ct);
        return reversal.Id;
    }

    /// <summary>İşlemin doğal (veya çevrilmiş) dengeli defter kümesi. FAZ 4.2-B4: DonemFaturaUretici
    /// (job) oto-tahsilat kayıtlarını da BU kümeden üretir — manuel/job defter şekli özdeş (tek kopya).</summary>
    public static List<AccountLedgerEntry> Natural(CashTransaction tx, Guid? sourceIdOverride = null, bool flip = false)
    {
        // Tahsilat: Hesap Borç, Cari Alacak. Ödeme: Hesap Alacak, Cari Borç.
        var accountDirection = tx.Tip == CashTransactionType.Tahsilat ? LedgerDirection.Debit : LedgerDirection.Credit;
        var customerDirection = tx.Tip == CashTransactionType.Tahsilat ? LedgerDirection.Credit : LedgerDirection.Debit;
        if (flip) { accountDirection = Flip(accountDirection); customerDirection = Flip(customerDirection); }

        var src = flip ? "TersKayit" : (tx.Tip == CashTransactionType.Tahsilat ? "Tahsilat" : "Odeme");
        var sourceId = sourceIdOverride ?? tx.Id;

        return
        [
            // FAZ-50: para bacağı artık HANGİ kasa/banka hesabından geçtiğini taşır (null → legacy kova).
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = tx.KarsiHesap, AccountRef = tx.HesapId,
                Direction = accountDirection, Amount = tx.Amount, SourceType = src, SourceId = sourceId, Description = tx.Aciklama },
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = LedgerAccountType.Cari, AccountRef = tx.CariId,
                Direction = customerDirection, Amount = tx.Amount, SourceType = src, SourceId = sourceId, Description = tx.Aciklama }
        ];
    }

    private static LedgerDirection Flip(LedgerDirection d)
        => d == LedgerDirection.Debit ? LedgerDirection.Credit : LedgerDirection.Debit;

    private static void EnsureCashBank(LedgerAccountType account)
    {
        if (account is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Hesap yalnız Kasa veya Banka olabilir.");
    }

    /// <summary>FAZ-84 — boş → "Masaüstü"; bilinmeyen serbest metin GÜRÜLTÜLÜ reddedilir (sessiz
    /// normalize, formdaki yazım hatasını "Masaüstü"ye düşürüp raporu yanlış gösterirdi).</summary>
    private static string NormalizeChannelOrThrow(string? raw)
        => CashKanal.TryNormalize(raw)
            ?? throw new ValidationException(
                $"Geçersiz kanal: '{raw}'. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.");
}
