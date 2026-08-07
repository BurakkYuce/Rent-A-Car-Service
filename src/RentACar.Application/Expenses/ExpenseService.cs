using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Expenses;

/// <summary>
/// Gider iş mantığı: doğrulama + KDV + DENGELİ çift-taraflı defter.
///   Borç Gider(net) + Borç KDV(indirilecek, kdv) / Alacak Kasa·Banka·Cari(gross).
/// Nakit/Banka → Kasa/Banka azalır; AçıkHesap → tedarikçi cari'ye borçlanılır (Alacak).
/// </summary>
public sealed class ExpenseService(IExpenseRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.KurCozucu kurCozucu)
{
    private readonly IExpenseRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.KurCozucu _kurCozucu = kurCozucu;

    /// <summary>
    /// Giderler. Şube kapsamı (C3: FK-farkındalı) HER ZAMAN uygulanır; <paramref name="filter"/>
    /// yalnız kapsam içinde daraltır (FAZ-63 arama paneli). null → eski davranış.
    /// </summary>
    public Task<IReadOnlyList<Expense>> ListAsync(ExpenseFilter? filter = null, CancellationToken ct = default)
        => _repository.ListAsync(BranchScope.EffectiveFilter(_currentUser), filter, ct);

    public async Task<Expense?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var e = await _repository.FindAsync(id, ct);
        // C3 adversarial F1: liste kapsamlıyken tekil-ID probe'u açıktı (M3 sınıfı; bugün çağıran yüzey
        // yok — gelecekteki gider-detay ucu için parite guard'ı).
        if (e is not null) BranchScope.RequireInScope(_currentUser, e.SubeId, e.Sube);
        return e;
    }

    public async Task<Guid> CreateAsync(ExpenseInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        TarihPolitikasi.ParaTarihi(input.Tarih, "Gider"); // savunma: gelecek tarih reddi (geçmiş dönem-kilidinde)
        var cozulenKur = await _kurCozucu.CozAsync(input.Doviz, input.Kur, input.Tarih, ct); // 1.1b
        var posting = BuildPosting(input, islemAnahtari: null, cozulenKur);
        await _lock.EnsureOpenAsync(posting.Expense.Tarih, ct); // dönem kilidi: kapalı tarihe gider YOK
        await _repository.PostAsync(posting.Expense, posting.Entries, ct);
        return posting.Expense.Id;
    }

    /// <summary>Toplu gider (parite #10): çok kalem gideri TEK transaction'da işler. ATOMİK (hep-ya-hiç) +
    /// kalem-bazlı dengeli. <paramref name="batchAnahtari"/> verilirse her kalem deterministik idempotency
    /// anahtarı alır → çift-submit tüm batch'i geri alır. Bir kalem geçersizse HİÇBİRİ yazılmaz.</summary>
    public async Task BatchCreateAsync(
        IReadOnlyList<ExpenseInput> kalemler, Guid? batchAnahtari = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (kalemler.Count == 0) throw new ValidationException("Toplu gider en az bir kalem içermelidir.");
        if (kalemler.Count > 500) throw new ValidationException("Toplu gider en çok 500 kalem olabilir.");
        foreach (var k in kalemler) TarihPolitikasi.ParaTarihi(k.Tarih, "Gider"); // savunma: gelecek tarih reddi

        var closing = await _lock.GetClosingDateAsync(ct); // dönem kilidi: bir kez oku, kalem-bazlı karşılaştır
        var kurCache = new Dictionary<(string, DateTime?), decimal>(); // 1.1b: aynı (kod,gün) tek lookup
        var postings = new List<ExpensePosting>(kalemler.Count);
        for (var i = 0; i < kalemler.Count; i++)
        {
            var input = kalemler[i];
            decimal cozulenKur;
            if (input.Kur is { } acikKur)
            {
                if (acikKur <= 0m) throw new ValidationException($"Kalem {i + 1}: kur pozitif olmalıdır.");
                cozulenKur = acikKur;
            }
            else
            {
                var kurKey = (RentACar.Application.Kur.KurService.NormalizeKod(input.Doviz), input.Tarih?.UtcDateTime.Date);
                if (!kurCache.TryGetValue(kurKey, out cozulenKur))
                    kurCache[kurKey] = cozulenKur = await _kurCozucu.CozAsync(input.Doviz, null, input.Tarih, ct);
            }
            var p = BuildPosting(input, batchAnahtari is { } b ? CashService.RowKey(b, i) : null, cozulenKur);
            PeriodLock.ThrowIfClosed(p.Expense.Tarih, closing, $"Kalem {i + 1}");
            postings.Add(p);
        }

        await _repository.PostBatchAsync(postings, ct);
    }

    /// <summary>Bir gider girişini doğrular + Expense belgesi + dengeli defter kümesi kurar (tek + toplu ortak).</summary>
    private static ExpensePosting BuildPosting(ExpenseInput input, Guid? islemAnahtari, decimal cozulenKur)
    {
        if (input.NetTutar <= 0) throw new ValidationException("Gider tutarı pozitif olmalıdır.");
        if (input.KdvOrani < 0) throw new ValidationException("KDV oranı negatif olamaz.");
        if (input.OdemeYontemi == OdemeYontemi.AcikHesap && (input.CariId is null || input.CariId == Guid.Empty))
            throw new ValidationException("Açık hesap (tedarikçi) gideri için cari seçilmelidir.");
        if (input.Tip == ExpenseType.Arac && (input.VehicleId is null || input.VehicleId == Guid.Empty))
            throw new ValidationException("Araç gideri için araç seçilmelidir.");

        var (kdv, gross) = KdvMath.FromNet(input.NetTutar, input.KdvOrani);
        var net = KdvMath.RoundGross(input.NetTutar); // kuruşa sabit
        var currency = (input.Doviz ?? "TRY").Trim().ToUpperInvariant();

        var karsiHesap = input.OdemeYontemi == OdemeYontemi.AcikHesap
            ? LedgerAccountType.Cari
            : (input.OdemeYontemi == OdemeYontemi.Banka ? LedgerAccountType.Banka : LedgerAccountType.Kasa);

        var expense = new Expense
        {
            Tip = input.Tip,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            VehicleId = input.VehicleId,
            CariId = input.CariId,
            Sube = input.Sube,
            EvrakNo = input.EvrakNo,
            NetTutar = net,
            KdvOrani = input.KdvOrani,
            KdvTutar = kdv,
            GenelToplam = gross,
            Currency = currency,
            Kur = cozulenKur,
            OdemeYontemi = input.OdemeYontemi,
            KasaBankaHesap = karsiHesap == LedgerAccountType.Cari ? LedgerAccountType.Kasa : karsiHesap,
            Aciklama = input.Aciklama,
            IslemAnahtari = islemAnahtari
        };

        var entries = BuildEntries(expense, karsiHesap, net, kdv, gross);
        return new ExpensePosting(expense, entries);
    }

    /// <summary>Borç Gider(net) + Borç KDV(kdv) / Alacak karşıHesap(gross). DENGELİ.</summary>
    private static List<AccountLedgerEntry> BuildEntries(
        Expense e, LedgerAccountType karsiHesap, decimal net, decimal kdv, decimal gross)
    {
        AccountLedgerEntry Entry(LedgerAccountType type, Guid? reff, LedgerDirection dir, decimal amount) => new()
        {
            EntryDateUtc = e.Tarih, AccountType = type, AccountRef = reff, Direction = dir,
            Amount = new Money(amount, e.Currency, e.Kur),
            SourceType = "Gider", SourceId = e.Id, Description = e.Aciklama
        };

        var list = new List<AccountLedgerEntry>
        {
            Entry(LedgerAccountType.Gider, e.VehicleId, LedgerDirection.Debit, net)
        };
        if (kdv > 0)
            list.Add(Entry(LedgerAccountType.Kdv, null, LedgerDirection.Debit, kdv)); // indirilecek KDV

        // Karşı hesap (Alacak): Kasa/Banka veya tedarikçi Cari.
        var karsiRef = karsiHesap == LedgerAccountType.Cari ? e.CariId : null;
        list.Add(Entry(karsiHesap, karsiRef, LedgerDirection.Credit, gross));
        return list;
    }
}
