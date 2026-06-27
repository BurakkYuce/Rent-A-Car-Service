using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.VehicleSales;

/// <summary>
/// Araç satışı: net + KDV → brüt; alıcı cari BORÇLANIR ve araç filodan çıkar (Satildi).
///   Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv) — DENGELİ.
/// Maliyet/sabit-kıymet defteri bu sürümde tutulmaz (gelir tam net olarak yazılır), tıpkı
/// kira gelirinde olduğu gibi; gelecekte amortisman/defter-değeri eklenebilir.
/// </summary>
public sealed class VehicleSaleService(IVehicleSaleRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleSaleRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<VehicleSale>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);
    public Task<VehicleSale?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleSaleInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.AliciCariId == Guid.Empty) throw new ValidationException("Alıcı (cari) seçilmelidir.");
        if (input.SatisNet <= 0) throw new ValidationException("Satış tutarı pozitif olmalıdır.");
        if (input.KdvOrani < 0) throw new ValidationException("KDV oranı negatif olamaz.");
        if (input.Kur <= 0) throw new ValidationException("Kur pozitif olmalıdır.");

        var (kdv, gross) = KdvMath.FromNet(input.SatisNet, input.KdvOrani);
        var sale = new VehicleSale
        {
            VehicleId = input.VehicleId,
            AliciCariId = input.AliciCariId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            NoterNo = input.NoterNo,
            SatisNet = input.SatisNet,
            KdvOrani = input.KdvOrani,
            KdvTutar = kdv,
            GenelToplam = gross,
            Currency = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
            Kur = input.Kur,
            Aciklama = input.Aciklama,
            Durum = SatisDurum.Tamamlandi
        };

        await _repository.PostAsync(sale, BuildEntries(sale), ct);
        return sale.Id;
    }

    /// <summary>Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv). DENGELİ.</summary>
    private static List<AccountLedgerEntry> BuildEntries(VehicleSale s)
    {
        AccountLedgerEntry Entry(LedgerAccountType type, Guid? reff, LedgerDirection dir, decimal amount) => new()
        {
            EntryDateUtc = s.Tarih, AccountType = type, AccountRef = reff, Direction = dir,
            Amount = new Money(amount, s.Currency, s.Kur),
            SourceType = "AracSatis", SourceId = s.Id, Description = $"Araç satış {s.No}"
        };

        return
        [
            Entry(LedgerAccountType.Cari, s.AliciCariId, LedgerDirection.Debit, s.GenelToplam),
            Entry(LedgerAccountType.Gelir, null, LedgerDirection.Credit, s.SatisNet),
            Entry(LedgerAccountType.Kdv, null, LedgerDirection.Credit, s.KdvTutar)
        ];
    }
}
