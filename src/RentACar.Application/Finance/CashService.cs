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
    ICustomerRepository customers, RentACar.Application.Kur.KurCozucu kurCozucu)
{
    private readonly ICashRepository _repository = repository;
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly ICustomerRepository _customers = customers;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;

    public Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<CashTransaction?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetCariBalanceAsync(cariId, ct);

    /// <summary>
    /// Cari ekstresi (satırlar + devir). <paramref name="filter"/> null → carinin TÜM hareketleri.
    /// Devir yalnız tarih alt sınırı verildiğinde dolar; bkz. <see cref="CariEkstreSonuc"/>.
    /// </summary>
    public Task<CariEkstreSonuc> GetStatementAsync(
        Guid cariId, CariEkstreFilter? filter = null, CancellationToken ct = default)
        => _repository.GetCariStatementAsync(cariId, filter, ct);

    /// <summary>Kira başına işlem SAYISI (ters kayıt dahil, monoton) — deterministik tahsilat
    /// anahtarının zamansal bileşeni. Kayıtsız kira sözlükte yok → TryGetValue→0.</summary>
    public Task<Dictionary<Guid, int>> GetRentalIslemSayilariAsync(
        IReadOnlyCollection<Guid> rentalIds, CancellationToken ct = default)
        => _repository.GetRentalIslemSayilariAsync(rentalIds, ct);

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

        // Kur çözümü (1.1b): açık kur aynen; boş → TRY=1 / döviz KurService (yoksa net red — sessiz 1 YOK).
        var cozulenKur = await _kurCozucu.CozAsync(input.Doviz, input.Kur, input.Tarih, ct);
        var money = new Money(input.Tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(input.Doviz), cozulenKur);
        var tx = new CashTransaction
        {
            Tip = tip,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            Amount = money,
            KarsiHesap = input.Hesap,
            Aciklama = input.Aciklama,
            IslemAnahtari = input.IslemAnahtari is { } k && k != Guid.Empty ? k : null // adversarial M5: çift-submit dedup
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
                if (acik <= 0m) throw new ValidationException($"Satır {i + 1}: kur pozitif olmalıdır.");
                cozulenKur = acik;
            }
            else
            {
                var kurKey = (RentACar.Application.Kur.KurService.NormalizeKod(input.Doviz), input.Tarih?.UtcDateTime.Date);
                if (!kurCache.TryGetValue(kurKey, out cozulenKur))
                    kurCache[kurKey] = cozulenKur = await _kurCozucu.CozAsync(input.Doviz, null, input.Tarih, ct);
            }

            var money = new Money(input.Tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(input.Doviz), cozulenKur);
            var tx = new CashTransaction
            {
                Tip = tip,
                CariId = input.CariId,
                RentalId = input.RentalId,
                Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
                Amount = money,
                KarsiHesap = input.Hesap,
                Aciklama = input.Aciklama,
                IslemAnahtari = batchAnahtari is { } b ? RowKey(b, i) : null
            };
            PeriodLock.ThrowIfClosed(tx.Tarih, closing, $"Satır {i + 1}"); // dönem kilidi (satır-bazlı)
            postings.Add(new CashPosting(tx, Natural(tx)));
        }

        await _repository.PostBatchAsync(postings, ct);
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

    /// <summary>Kasa↔Banka virman (transfer): Borç Hedef / Alacak Kaynak. Belgesiz (dengeli defter).</summary>
    public async Task TransferAsync(
        LedgerAccountType kaynak, LedgerAccountType hedef, decimal tutar,
        string? doviz = "TRY", decimal? kur = null, string? aciklama = null,
        Guid? islemAnahtari = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        EnsureKasaBanka(kaynak);
        EnsureKasaBanka(hedef);
        if (kaynak == hedef) throw new ValidationException("Kaynak ve hedef hesap farklı olmalıdır.");
        if (tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");

        await _lock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // dönem kilidi (virman bugün tarihli)
        var cozulenKur = await _kurCozucu.CozAsync(doviz, kur, null, ct); // 1.1b: bugünkü kur
        var money = new Money(tutar, RentACar.Application.Kur.KurService.NormalizeKodStrict(doviz), cozulenKur);
        // İdempotency (pre-launch takip): token verilirse SourceId o olur → kısmi unique index çift-submit'i yutar.
        var sourceId = islemAnahtari is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = aciklama ?? $"Virman {kaynak}→{hedef}";
        await _ledger.PostAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = DateTimeOffset.UtcNow, AccountType = hedef, AccountRef = null,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = DateTimeOffset.UtcNow, AccountType = kaynak, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc }
        ], ct);
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
        Guid? islemAnahtari = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (kaynakCariId == Guid.Empty || hedefCariId == Guid.Empty)
            throw new ValidationException("Kaynak ve hedef cari seçilmelidir.");
        if (kaynakCariId == hedefCariId)
            throw new ValidationException("Kaynak ve hedef cari farklı olmalıdır.");
        if (tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");

        await _lock.EnsureOpenAsync(DateTimeOffset.UtcNow, ct); // dönem kilidi (cari virman bugün tarihli)
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
        await _ledger.PostAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Cari,
                AccountRef = hedefCariId, Direction = LedgerDirection.Debit, Amount = money,
                SourceType = "CariVirman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = DateTimeOffset.UtcNow, AccountType = LedgerAccountType.Cari,
                AccountRef = kaynakCariId, Direction = LedgerDirection.Credit, Amount = money,
                SourceType = "CariVirman", SourceId = sourceId, Description = desc }
        ], ct);
    }

    public async Task<Guid> ReverseAsync(Guid cashTransactionId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var original = await _repository.FindAsync(cashTransactionId, ct)
            ?? throw new ValidationException("İşlem bulunamadı.");
        if (original.TersKayitMi)
            throw new ValidationException("Ters kayıt tekrar ters alınamaz.");
        if (await _repository.HasReversalAsync(cashTransactionId, ct))
            throw new ValidationException("Bu işlem zaten ters kaydedilmiş.");

        var reversal = new CashTransaction
        {
            Tip = original.Tip,
            CariId = original.CariId,
            RentalId = original.RentalId,
            Tarih = DateTimeOffset.UtcNow,
            Amount = original.Amount,
            KarsiHesap = original.KarsiHesap,
            Aciklama = $"Ters kayıt: {original.No}",
            TersKayitMi = true,
            TersAlinanId = original.Id
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
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = tx.KarsiHesap, AccountRef = null,
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
}
