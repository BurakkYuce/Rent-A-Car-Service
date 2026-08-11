using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleSales;

public interface IVehicleSaleRepository
{
    Task<IReadOnlyList<VehicleSale>> ListAsync(CancellationToken ct = default);

    /// <summary>FAZ-18 — filtreli liste (tarih aralığı / durum / devir bayrağı / plaka / alıcı / ofis). Salt-okur.</summary>
    Task<IReadOnlyList<VehicleSale>> SearchAsync(VehicleSaleFilter filtre, CancellationToken ct = default);

    Task<VehicleSale?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// No boşluksuz tahsis edip satışı + DENGELİ defter kümesini yazar ve aracı Satildi'ye
    /// çevirir — TEK transaction. Araç zaten satılmışsa ValidationException (DB-garantili:
    /// tamamlanmış satış için araç başına kısmi unique index).
    /// </summary>
    Task PostAsync(VehicleSale sale, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);
}
