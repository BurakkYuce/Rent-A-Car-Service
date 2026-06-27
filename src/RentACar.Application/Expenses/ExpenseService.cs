using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Expenses;

/// <summary>
/// Gider iş mantığı: doğrulama + KDV + DENGELİ çift-taraflı defter.
///   Borç Gider(net) + Borç KDV(indirilecek, kdv) / Alacak Kasa·Banka·Cari(gross).
/// Nakit/Banka → Kasa/Banka azalır; AçıkHesap → tedarikçi cari'ye borçlanılır (Alacak).
/// </summary>
public sealed class ExpenseService(IExpenseRepository repository, ICurrentUser currentUser)
{
    private readonly IExpenseRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Expense>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<Expense?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ExpenseInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.NetTutar <= 0) throw new ValidationException("Gider tutarı pozitif olmalıdır.");
        if (input.Kur <= 0) throw new ValidationException("Kur pozitif olmalıdır.");
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
            Kur = input.Kur,
            OdemeYontemi = input.OdemeYontemi,
            KasaBankaHesap = karsiHesap == LedgerAccountType.Cari ? LedgerAccountType.Kasa : karsiHesap,
            Aciklama = input.Aciklama
        };

        var entries = BuildEntries(expense, karsiHesap, net, kdv, gross);
        await _repository.PostAsync(expense, entries, ct);
        return expense.Id;
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
