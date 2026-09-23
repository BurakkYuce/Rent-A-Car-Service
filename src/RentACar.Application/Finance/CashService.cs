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
    ICustomerRepository customers, RentACar.Application.Kur.KurCozucu kurCozucu,
    RentACar.Application.FinancialAccounts.HesapCozucu hesapCozucu)
{
    private readonly ICashRepository _repository = repository;
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly ICustomerRepository _customers = customers;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;
    private readonly RentACar.Application.FinancialAccounts.HesapCozucu _hesapCozucu = hesapCozucu;

    public Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-67 — süzgeçli nakit işlem listesi (cari adı/kodu çözümlenmiş).</summary>
    public Task<IReadOnlyList<NakitIslemSatirDto>> SearchIslemlerAsync(
        CashFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.SearchIslemlerAsync(filter, ct);
    }

    public Task<CashTransaction?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetCariBalanceAsync(cariId, ct);

    /// <summary>
    /// Cari ekstresi (satırlar + devir). <paramref name="filter"/> null → carinin TÜM hareketleri.
    /// Devir yalnız tarih alt sınırı verildiğinde dolar; bkz. <see cref="CariEkstreSonuc"/>.
    /// </summary>
    /// <summary>
    /// FAZ-59 — cari↔cari virman geçmişi (tüm cariler). Salt okuma; tutar defterden gelir.
    /// </summary>
    /// <summary>FAZ-50 — kasa/banka virman geçmişi (künye + defterden tutar). Salt okuma.</summary>
    public Task<IReadOnlyList<KasaVirmanSatirDto>> ListKasaVirmanlarAsync(
        KasaVirmanFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.ListKasaVirmanlarAsync(filter, ct);
    }

    public Task<IReadOnlyList<CariVirmanSatirDto>> ListCariVirmanlarAsync(
        CariVirmanFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.ListCariVirmanlarAsync(filter, ct);
    }

    public Task<CariEkstreSonuc> GetStatementAsync(
        Guid cariId, CariEkstreFilter? filter = null, CancellationToken ct = default)
        => _repository.GetCariStatementAsync(cariId, filter, ct);

    /// <summary>Kira başına işlem SAYISI (ters kayıt dahil, monoton) — deterministik tahsilat
    /// anahtarının zamansal bileşeni. Kayıtsız kira sözlükte yok → TryGetValue→0.</summary>
    public Task<Dictionary<Guid, int>> GetRentalIslemSayilariAsync(
        IReadOnlyCollection<Guid> rentalIds, CancellationToken ct = default)
        => _repository.GetRentalIslemSayilariAsync(rentalIds, ct);

    /// <summary>F4.4 adversarial HIGH-1: verilen <c>IslemAnahtari</c> ile yazılmış kasa/banka işlemi (yoksa null;
    /// kiracı RLS'i + tenant filtresi). Deterministik tahsilat anahtarının YENİDEN HESAPLANMASINDAN ÖNCE bakılır:
    /// kaybolan yanıttan sonraki birebir tekrar "kayıt değişti" değil "zaten kaydedildi" almalı.</summary>
    public Task<CashTransaction?> IslemAnahtariylaBulAsync(Guid islemAnahtari, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return _repository.FindByIslemAnahtariAsync(islemAnahtari, ct);
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
        TarihPolitikasi.ParaTarihi(input.Tarih, "İşlem"); // savunma: gelecek tarih reddi (geçmiş dönem-kilidinde)
        EnsureKasaBanka(input.Hesap);
        // F4.4a adversarial MEDIUM-2: cari kiracı içinde GERÇEKTEN var olmalı (FindAsync RLS + sorgu filtresi →
        // yoksa/başka kiracınınsa null). Önce rastgele ya da başka kiracının cari kimliğiyle tahsilat/ödeme
        // yazılabiliyor, defterde hiçbir ekstrede görünmeyen yetim AccountRef'li küme kalıyordu. Virmandaki L2 deseni.
        if (await _customers.FindAsync(input.CariId, ct) is null)
            throw new ValidationException("Cari bulunamadı.", "cariId");

        // Kur çözümü (1.1b): açık kur aynen; boş → TRY=1 / döviz KurService (yoksa net red — sessiz 1 YOK).
        var cozulenKur = await _kurCozucu.CozAsync(input.Doviz, input.Kur, input.Tarih, ct);
        var cozulenHesap = await _hesapCozucu.CozAsync(input.HesapId, input.Hesap, ct, input.Doviz); // FAZ-50
        var money = new Money(input.Tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(input.Doviz), cozulenKur);
        // FAZ-84: kanal SAF BİLGİ — CashTransaction belgesine yazılır, defter/bakiye hiç görmez.
        var kanal = NormalizeKanalOrThrow(input.Kanal);
        var tx = new CashTransaction
        {
            Tip = tip,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            Amount = money,
            KarsiHesap = input.Hesap,
            HesapId = cozulenHesap,
            Aciklama = input.Aciklama,
            IslemAnahtari = input.IslemAnahtari is { } k && k != Guid.Empty ? k : null, // adversarial M5: çift-submit dedup
            Kanal = kanal
        };
        await _lock.EnsureOpenAsync(tx.Tarih, ct); // dönem kilidi: kapalı tarihe tahsilat/ödeme YOK

        var entries = Natural(tx);
        // Kira bağlıysa Tahsilat/Bakiye repo'da tx'ten türetilir (yön + kira dövizi, atomik — K2/O1).
        await _repository.PostAsync(tx, entries, ct);
        if (tip == CashTransactionType.Tahsilat) RentACar.Application.Observability.RacarMetrics.TahsilatOk(); // metrik
        return tx.Id;
    }

    /// <summary>Toplu tahsilat (parite #10): çok cariye tek seferde tahsilat. ATOMİK (hep-ya-hiç) +
    /// satır-bazlı dengeli. <paramref name="batchAnahtari"/> verilirse her satır deterministik idempotency
    /// anahtarı alır → çift-submit tüm batch'i geri alır. Bir satır geçersizse HİÇBİRİ yazılmaz.</summary>
    public Task BatchCollectAsync(
        IReadOnlyList<CashInput> satirlar, Guid? batchAnahtari = null, CancellationToken ct = default)
        => BatchCashAsync(satirlar, CashTransactionType.Tahsilat, batchAnahtari, ct);

    /// <summary>Toplu ödeme/tediye: çok cariye tek seferde ödeme. ATOMİK + idempotent (bkz. BatchCollectAsync).</summary>
    public Task BatchPayAsync(
        IReadOnlyList<CashInput> satirlar, Guid? batchAnahtari = null, CancellationToken ct = default)
        => BatchCashAsync(satirlar, CashTransactionType.Odeme, batchAnahtari, ct);

    private async Task BatchCashAsync(
        IReadOnlyList<CashInput> satirlar, CashTransactionType tip, Guid? batchAnahtari, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (satirlar.Count == 0) throw new ValidationException("Toplu işlem en az bir satır içermelidir.");
        if (satirlar.Count > 500) throw new ValidationException("Toplu işlem en çok 500 satır olabilir.");
        foreach (var s in satirlar) TarihPolitikasi.ParaTarihi(s.Tarih, "İşlem"); // savunma (adversarial BULGU 3: tek yolla simetri)

        // Dönem kilidi: kapanış tarihini bir kez oku, her satırın tarihini yerelde karşılaştır.
        var closing = await _lock.GetClosingDateAsync(ct);

        // TÜM satırlar önce doğrulanır (fail-fast) → repo'ya yalnız geçerli set gider; atomiklik repo'da.
        var kurCache = new Dictionary<(string, DateTime?), decimal>();
        var hesapCache = new Dictionary<(Guid, LedgerAccountType), Guid?>(); // FAZ-50: satır-bazlı hesap doğrulaması
        var postings = new List<CashPosting>(satirlar.Count);
        for (var i = 0; i < satirlar.Count; i++)
        {
            var input = satirlar[i];
            if (input.CariId == Guid.Empty) throw new ValidationException($"Satır {i + 1}: cari seçilmelidir.");
            if (input.Tutar <= 0) throw new ValidationException($"Satır {i + 1}: tutar pozitif olmalıdır.");
            EnsureKasaBanka(input.Hesap);

            // Kur çözümü (1.1b) — açık kur satır-bazlı ÖNCE (satır-önekli guard); otomatik çözüm
            // (kod, gün) önbelleğiyle (500 satırda tek lookup).
            decimal cozulenKur;
            if (input.Kur is { } acik)
            {
                // FAZ-82: açık kur da KurCozucu'dan GEÇER. Önce burada yerel bir pozitiflik kontrolü
                // yapılıp değer aynen alınıyordu; bu, "kur elle girilemez" tenant kilidinin TOPLU
                // tahsilat/ödeme ekranından dolanılmasına açık kapı bırakıyordu (tekil yol kilitli,
                // toplu yol serbest). Satır öneki korunuyor (500 satırda hangi satır olduğu şart).
                cozulenKur = await SatirKuruAsync(input.Doviz, acik, input.Tarih, i, ct);
            }
            else
            {
                var kurKey = (RentACar.Application.Kur.KurService.NormalizeKod(input.Doviz), input.Tarih?.UtcDateTime.Date);
                if (!kurCache.TryGetValue(kurKey, out cozulenKur))
                    kurCache[kurKey] = cozulenKur = await _kurCozucu.CozAsync(input.Doviz, null, input.Tarih, ct);
            }

            // FAZ-50: satır-bazlı hesap seçimi (aynı hesap tekrar ediyorsa tek doğrulama).
            Guid? cozulenHesap = null;
            if (input.HesapId is { } hid && hid != Guid.Empty)
            {
                if (!hesapCache.TryGetValue((hid, input.Hesap), out cozulenHesap))
                    hesapCache[(hid, input.Hesap)] = cozulenHesap =
                        await _hesapCozucu.CozAsync(hid, input.Hesap, ct, input.Doviz);
            }

            var money = new Money(input.Tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(input.Doviz), cozulenKur);
            // FAZ-84: satır-bazlı kanal (bozuk değer önekli hata — 500 satırda hangisi olduğu şart).
            string? kanal;
            try { kanal = NormalizeKanalOrThrow(input.Kanal); }
            catch (ValidationException ex) { throw new ValidationException($"Satır {i + 1}: {ex.Message}"); }
            var tx = new CashTransaction
            {
                Tip = tip,
                CariId = input.CariId,
                RentalId = input.RentalId,
                Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
                Amount = money,
                KarsiHesap = input.Hesap,
                HesapId = cozulenHesap,
                Aciklama = input.Aciklama,
                IslemAnahtari = batchAnahtari is { } b ? RowKey(b, i) : null,
                Kanal = kanal
            };
            PeriodLock.ThrowIfClosed(tx.Tarih, closing, $"Satır {i + 1}"); // dönem kilidi (satır-bazlı)
            postings.Add(new CashPosting(tx, Natural(tx)));
        }

        await _repository.PostBatchAsync(postings, ct);
    }

    /// <summary>FAZ-82: toplu satırda AÇIK kur çözümü — kural tek kaynaktan (KurCozucu: pozitiflik +
    /// tenant elle-giriş kilidi), hata mesajı satır önekiyle zenginleştirilerek yeniden fırlatılır.</summary>
    private async Task<decimal> SatirKuruAsync(
        string? doviz, decimal acik, DateTimeOffset? tarih, int index, CancellationToken ct)
    {
        try { return await _kurCozucu.CozAsync(doviz, acik, tarih, ct); }
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
    /// <param name="secim">satırId → kapatılacak baz tutar. Tutar null/0 ise kalemin KALANI kapatılır.</param>
    /// <returns>Postlanan tahsilatın tutarı (baz para).</returns>
    public async Task<decimal> TekCariTopluKapatAsync(
        Guid cariId, IReadOnlyDictionary<Guid, decimal?> secim, LedgerAccountType hesap,
        DateTimeOffset? tarih = null, string? aciklama = null, Guid? islemAnahtari = null,
        string? kanal = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (cariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (secim.Count == 0) throw new ValidationException("En az bir kalem seçilmelidir.");
        EnsureKasaBanka(hesap);

        // F1.4 — ANAHTAR ÖNCE: aynı anahtarla ikinci gönderim, tahsis/bakiye ön-kontrollerinden ÖNCE
        // mükerrer sayılır (repo aynı kontrolü kilidin arkasında tekrarlar). Yoksa sonuç ilk gönderimin
        // kalemi tam mı kısmi mi kapattığına göre 400 ↔ 409 değişiyordu.
        if (islemAnahtari is { } oncekiAnahtar && oncekiAnahtar != Guid.Empty
            && await _repository.IslemAnahtariVarMiAsync(oncekiAnahtar, ct))
            throw new MukerrerIslemException("Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).");

        // Ekstre O CARİ için okunur → başka carinin satırı burada zaten bulunamaz; tenant sınırı
        // ayrıca RLS + query filter ile korunur.
        var ekstre = await _repository.GetCariStatementAsync(cariId, null, ct);
        var index = ekstre.Satirlar.ToDictionary(x => x.Id);
        var kapatilan = await _repository.GetTahsisToplamlariAsync([.. secim.Keys], ct);

        var tahsisler = new List<KapatmaTahsis>(secim.Count);
        var toplamHam = 0m;
        foreach (var (id, istenenTutar) in secim)
        {
            if (!index.TryGetValue(id, out var satir))
                throw new ValidationException("Seçilen kalemlerden biri bu carinin ekstresinde bulunamadı.");
            if (satir.Direction != LedgerDirection.Debit)
                throw new ValidationException("Yalnız BORÇ kalemleri kapatılabilir; seçimde alacak kalemi var.");

            var satirBaz = satir.Amount.AmountInBase;
            var onceki = kapatilan.TryGetValue(id, out var t0) ? t0 : 0m;
            var kalan = satirBaz - onceki;
            if (kalan <= 0.005m)
                throw new ValidationException("Seçilen kalemlerden biri zaten tamamen kapatılmış.");

            // Tutar verilmediyse KALANI kapat. Kuruşa AŞAĞI yuvarlanır (adversarial M3): yukarı
            // yuvarlamak kalemin/bakiyenin üstüne çıkıp bakiyeyi eksiye düşürüyordu.
            var tutar = istenenTutar is { } v && v > 0m
                ? decimal.Round(v, 2, MidpointRounding.ToZero)
                : decimal.Round(kalan, 2, MidpointRounding.ToZero);
            if (tutar <= 0m) throw new ValidationException("Kapatma tutarı pozitif olmalıdır.");
            if (tutar > kalan + 0.005m)
                throw new ValidationException(
                    $"Kapatma tutarı kalemin kalanını aşıyor (kalan {kalan:N2}, istenen {tutar:N2}).");

            toplamHam += tutar;
            tahsisler.Add(new KapatmaTahsis
            { LedgerEntryId = id, CariId = cariId, KapatilanBaz = tutar });
        }

        if (toplamHam <= 0m) throw new ValidationException("Seçilen kalemlerin toplamı pozitif olmalıdır.");

        // Bakiye ön-kontrolü YALNIZ iyi hata mesajı içindir; ASIL çit repo'da, kilidin arkasında.
        var bakiye = await _repository.GetCariBalanceAsync(cariId, ct);
        if (bakiye <= 0m)
            throw new ValidationException("Carinin kapatılacak borcu yok (bakiye borçlu değil).");
        if (toplamHam > bakiye)
            throw new ValidationException(
                $"Seçilen tutar ({toplamHam:N2}) carinin güncel borcunu ({bakiye:N2}) aşıyor.");

        var rentalId = await KiraBagiCozAsync(secim.Keys, index, ct);

        // Tahsilat BAZ parada postlanır: seçim karışık dövizli satırlardan gelebilir, tek bir
        // döviz seçmek toplamı bozardı (AmountInBase toplandı).
        var tx = new CashTransaction
        {
            Tip = CashTransactionType.Tahsilat,
            CariId = cariId,
            RentalId = rentalId,
            Tarih = tarih ?? DateTimeOffset.UtcNow,
            Amount = new Money(toplamHam, "TRY", 1m),
            KarsiHesap = hesap,
            Aciklama = aciklama ?? $"Toplu kapatma ({tahsisler.Count} kalem)",
            IslemAnahtari = islemAnahtari is { } k && k != Guid.Empty ? k : null,
            Kanal = NormalizeKanalOrThrow(kanal) // FAZ-84
        };
        TarihPolitikasi.ParaTarihi(tx.Tarih, "İşlem");
        await _lock.EnsureOpenAsync(tx.Tarih, ct);      // dönem kilidi

        foreach (var t in tahsisler) t.CashTransactionId = tx.Id;
        await _repository.PostCariKapatmaAsync(cariId, tx, Natural(tx), tahsisler, ct);
        RentACar.Application.Observability.RacarMetrics.TahsilatOk();
        return toplamHam;
    }

    /// <summary>Verilen borç satırları için şu ana kadar KAPATILMIŞ baz tutarlar (ekran "kapalı /
    /// kısmi" göstergesi). Servisin çit kurarken kullandığı kaynağın AYNISI — ekran ayrı bir hesap
    /// yapsaydı gösterge ile çit ayrışabilirdi.</summary>
    public Task<Dictionary<Guid, decimal>> KapatilanTutarlarAsync(
        IReadOnlyCollection<Guid> ledgerEntryIds, CancellationToken ct = default)
        => _repository.GetTahsisToplamlariAsync(ledgerEntryIds, ct);

    /// <summary>Kalemleri TAMAMEN kapatan kısayol (kısmi tutar verilmez) — sözleşme aynıdır.</summary>
    public Task<decimal> TekCariTopluKapatAsync(
        Guid cariId, IReadOnlyCollection<Guid> secilenSatirIds, LedgerAccountType hesap,
        DateTimeOffset? tarih = null, string? aciklama = null, Guid? islemAnahtari = null,
        string? kanal = null, CancellationToken ct = default)
        => TekCariTopluKapatAsync(
            cariId, secilenSatirIds.Distinct().ToDictionary(x => x, _ => (decimal?)null),
            hesap, tarih, aciklama, islemAnahtari, kanal, ct);

    /// <summary>Kapatılan kalemlerin HEPSİ aynı kiranın faturasından geliyorsa o kira; aksi halde
    /// null (tek tahsilatı iki kiraya atfetmek yanlış olurdu).</summary>
    private async Task<Guid?> KiraBagiCozAsync(
        IEnumerable<Guid> ids, IReadOnlyDictionary<Guid, AccountLedgerEntry> index, CancellationToken ct)
    {
        var faturaIds = ids.Select(i => index[i])
            .Where(e => string.Equals(e.SourceType, "Fatura", StringComparison.Ordinal) && e.SourceId != Guid.Empty)
            .Select(e => e.SourceId).Distinct().ToList();
        if (faturaIds.Count == 0) return null;

        var kiralar = await _repository.FaturaKiralariAsync(faturaIds, ct);
        // Faturasız/kirasız bir kalem varsa da bağ kurma: seçim homojen değil.
        if (kiralar.Count != faturaIds.Count) return null;
        var tekil = kiralar.Values.Distinct().ToList();
        return tekil.Count == 1 ? tekil[0] : null;
    }

    /// <summary>
    /// Kasa↔Banka virman (transfer): Borç Hedef / Alacak Kaynak. Belgesiz (dengeli defter).
    ///
    /// <para><b>FAZ-50 — aynı türde iki hesap arası virman ARTIK MÜMKÜN.</b> Önceden kontrol enum
    /// düzeyindeydi: iki farklı banka hesabı da sistemin gözünde "Banka" olduğundan Ziraat→İş Bankası
    /// aktarımı reddediliyordu. Artık kaynak/hedef SPESİFİK hesap (<paramref name="kaynakHesapId"/>/
    /// <paramref name="hedefHesapId"/>) ile ayrışır.</para>
    ///
    /// <para><b>Aynı türde virmanda iki hesap da ZORUNLU</b> (bilinçli sıkılaştırma): yalnız biri
    /// verilseydi para, "hesap belirtilmemiş" legacy kovası ile gerçek bir hesap arasında akar ve
    /// geçmiş kayıtların toplandığı o kovanın bakiyesi sebepsiz oynardı.</para>
    ///
    /// <para><b>Künye</b> (makbuz no / işlem şubesi) defter DIŞINDA
    /// <see cref="KasaVirmanBilgi"/>'ye aynı transaction'da yazılır — <c>CariVirmanBilgi</c> deseni.</para>
    /// </summary>
    public async Task TransferAsync(
        LedgerAccountType kaynak, LedgerAccountType hedef, decimal tutar,
        string? doviz = "TRY", decimal? kur = null, string? aciklama = null,
        Guid? islemAnahtari = null,
        // FAZ-50 — spesifik hesap + künye. Hepsi opsiyonel: verilmezse eski davranış birebir korunur.
        Guid? kaynakHesapId = null, Guid? hedefHesapId = null,
        string? makbuzNo = null, string? sube = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        EnsureKasaBanka(kaynak);
        EnsureKasaBanka(hedef);
        if (tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");

        // Hesap doğrulaması guard'lardan ÖNCE: uydurma/başka tenant'ın hesabı buradan geri döner.
        var kaynakRef = await _hesapCozucu.CozAsync(kaynakHesapId, kaynak, ct, doviz);
        var hedefRef = await _hesapCozucu.CozAsync(hedefHesapId, hedef, ct, doviz);

        // ADVERSARIAL M1 — kural TÜRDEN BAĞIMSIZ: bir taraf hesap seçilmişse diğeri de seçilmeli.
        // Önce yalnız aynı-tür dalında kontrol ediliyordu; Kasa→Banka virmanında tek taraf seçmek
        // serbestti ve paranın diğer ucu "hesap belirtilmemiş" kovasına düşüp o kovanın bakiyesini
        // tam da yasaklanan şekilde oynatıyordu.
        if ((kaynakRef is null) != (hedefRef is null))
            throw new ValidationException(
                "Virmanda bir taraf için hesap seçtiyseniz diğer taraf için de seçmelisiniz " +
                "(aksi hâlde paranın bir ucu 'hesap belirtilmemiş' kovasına düşer).");
        if (kaynak == hedef)
        {
            if (kaynakRef is null || hedefRef is null)
                throw new ValidationException(
                    "Aynı türdeki iki hesap arasında virman için kaynak ve hedef hesabı ayrı ayrı seçin.");
            if (kaynakRef == hedefRef)
                throw new ValidationException("Kaynak ve hedef hesap farklı olmalıdır.");
        }

        await _lock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // dönem kilidi (virman bugün tarihli)
        var cozulenKur = await _kurCozucu.CozAsync(doviz, kur, null, ct); // 1.1b: bugünkü kur
        var money = new Money(tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(doviz), cozulenKur);
        // İdempotency (pre-launch takip): token verilirse SourceId o olur → kısmi unique index çift-submit'i yutar.
        var sourceId = islemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = aciklama ?? $"Virman {kaynak}→{hedef}";
        var simdi = DateTimeOffset.UtcNow;
        var kunye = new KasaVirmanBilgi
        {
            Id = sourceId, KaynakTur = kaynak, HedefTur = hedef,
            KaynakHesapId = kaynakRef, HedefHesapId = hedefRef,
            Tarih = simdi, MakbuzNo = Kirp(makbuzNo), Sube = Kirp(sube),
            IslemYapan = _currentUser.UserName, Aciklama = Kirp(aciklama)
        };
        await _ledger.PostWithAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = simdi, AccountType = hedef, AccountRef = hedefRef,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = simdi, AccountType = kaynak, AccountRef = kaynakRef,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc }
        ], kunye, ct);

        static string? Kirp(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    }

    /// <summary>
    /// Cari↔cari virman (parite #9): iki cari arası bakiye aktarımı. DENGELİ çift kayıt —
    /// hedef cari Borç (Debit, bakiye +), kaynak cari Alacak (Credit, bakiye −); ikisi de AccountType=Cari,
    /// iki farklı AccountRef, aynı Money → Σ borç(base) = Σ alacak(base). LedgerPoster dengeyi zorlar.
    /// Belge/No yazmaz (TransferAsync deseni). Her iki carinin ekstresinde görünür.
    ///
    /// İDEMPOTENCY: <paramref name="islemAnahtari"/> verilirse SourceId o olur ve kısmi unique index
    /// (SourceType='CariVirman') çift-submit'i yutar (web formu her açılışta sabit token gönderir →
    /// çift tıklama tek virman). Verilmezse her çağrı AYRI virmandır (Guid.NewGuid).
    /// DÜZELTME: ledger-only (CashTransaction/storno yok) → düzeltme, AYNI KUR ile ters yön virmandır
    /// (kaynak↔hedef değiş); FARKLI kurda baz para kalıntısı kalır (bakiye baz-para'da tutulur).
    /// DİKKAT (1.1b): kur BOŞ bırakılırsa GÜNÜN kuru çözülür — ertesi gün ters kayıtta kalıntı
    /// oluşmaması için düzeltmede ORİJİNAL kuru AÇIKÇA girin.
    /// </summary>
    public async Task TransferBetweenCariAsync(
        Guid kaynakCariId, Guid hedefCariId, decimal tutar,
        string? doviz = "TRY", decimal? kur = null, string? aciklama = null,
        Guid? islemAnahtari = null, CancellationToken ct = default,
        // FAZ-59 künye alanları — PARAYA DOKUNMAZ, ayrı tabloya yazılır.
        DateTimeOffset? tarih = null, DateTimeOffset? vade = null,
        string? makbuzNo = null, string? sube = null)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (kaynakCariId == Guid.Empty || hedefCariId == Guid.Empty)
            throw new ValidationException("Kaynak ve hedef cari seçilmelidir.");
        if (kaynakCariId == hedefCariId)
            throw new ValidationException("Kaynak ve hedef cari farklı olmalıdır.");
        if (tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");

        // FAZ-59: tarih artık ELLE girilebiliyor (önce her zaman "şimdi"ydi). Dolayısıyla dönem
        // kilidi de VERİLEN tarihe göre kontrol edilmeli — yoksa kapalı bir döneme geriye dönük
        // virman atılabilirdi. Gelecek tarih para kaydında yasak (TarihPolitikasi).
        var islemTarihi = tarih ?? DateTimeOffset.UtcNow;
        TarihPolitikasi.ParaTarihi(islemTarihi, "Virman");
        if (vade is { } v && v < islemTarihi.Date)
            throw new ValidationException("Vade tarihi virman tarihinden önce olamaz.");
        await _lock.EnsureOpenAsync(islemTarihi, ct);
        // L2: her iki cari tenant içinde GERÇEKTEN var olmalı (FindAsync RLS+query-filter → yoksa null).
        // Aksi halde bakiye var-olmayan bir "hayalet" cari ekstresine taşınırdı (tenant-içi bütünlük).
        if (await _customers.FindAsync(kaynakCariId, ct) is null)
            throw new ValidationException("Kaynak cari bulunamadı.");
        if (await _customers.FindAsync(hedefCariId, ct) is null)
            throw new ValidationException("Hedef cari bulunamadı.");
        var cozulenKur = await _kurCozucu.CozAsync(doviz, kur, null, ct); // 1.1b
        var money = new Money(tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(doviz), cozulenKur);
        var sourceId = islemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = aciklama ?? "Cari virman";
        // Künye defterle AYNI transaction'da yazılır: "defter var künye yok" durumu oluşamaz.
        // Künye PARA TAŞIMAZ — tutar/döviz/kur yalnız defterde (tek kaynak).
        var kunye = new CariVirmanBilgi
        {
            Id = sourceId, KaynakCariId = kaynakCariId, HedefCariId = hedefCariId,
            Tarih = islemTarihi, Vade = vade,
            MakbuzNo = Kirp(makbuzNo), Sube = Kirp(sube),
            IslemYapan = _currentUser.UserName, Aciklama = Kirp(aciklama)
        };
        await _ledger.PostWithAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = islemTarihi, AccountType = LedgerAccountType.Cari,
                AccountRef = hedefCariId, Direction = LedgerDirection.Debit, Amount = money,
                SourceType = "CariVirman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = islemTarihi, AccountType = LedgerAccountType.Cari,
                AccountRef = kaynakCariId, Direction = LedgerDirection.Credit, Amount = money,
                SourceType = "CariVirman", SourceId = sourceId, Description = desc }
        ], kunye, ct);

        static string? Kirp(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
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
            throw new MukerrerIslemException("Bu işlem zaten ters kaydedilmiş.");

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
        var hesapDir = tx.Tip == CashTransactionType.Tahsilat ? LedgerDirection.Debit : LedgerDirection.Credit;
        var cariDir = tx.Tip == CashTransactionType.Tahsilat ? LedgerDirection.Credit : LedgerDirection.Debit;
        if (flip) { hesapDir = Flip(hesapDir); cariDir = Flip(cariDir); }

        var src = flip ? "TersKayit" : (tx.Tip == CashTransactionType.Tahsilat ? "Tahsilat" : "Odeme");
        var sourceId = sourceIdOverride ?? tx.Id;

        return
        [
            // FAZ-50: para bacağı artık HANGİ kasa/banka hesabından geçtiğini taşır (null → legacy kova).
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = tx.KarsiHesap, AccountRef = tx.HesapId,
                Direction = hesapDir, Amount = tx.Amount, SourceType = src, SourceId = sourceId, Description = tx.Aciklama },
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = LedgerAccountType.Cari, AccountRef = tx.CariId,
                Direction = cariDir, Amount = tx.Amount, SourceType = src, SourceId = sourceId, Description = tx.Aciklama }
        ];
    }

    private static LedgerDirection Flip(LedgerDirection d)
        => d == LedgerDirection.Debit ? LedgerDirection.Credit : LedgerDirection.Debit;

    private static void EnsureKasaBanka(LedgerAccountType hesap)
    {
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Hesap yalnız Kasa veya Banka olabilir.");
    }

    /// <summary>FAZ-84 — boş → "Masaüstü"; bilinmeyen serbest metin GÜRÜLTÜLÜ reddedilir (sessiz
    /// normalize, formdaki yazım hatasını "Masaüstü"ye düşürüp raporu yanlış gösterirdi).</summary>
    private static string NormalizeKanalOrThrow(string? raw)
        => CashKanal.TryNormalize(raw)
            ?? throw new ValidationException(
                $"Geçersiz kanal: '{raw}'. İzin verilenler: {string.Join(", ", CashKanal.Hepsi)}.");
}
