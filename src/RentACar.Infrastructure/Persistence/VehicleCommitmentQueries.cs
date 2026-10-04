using Microsoft.EntityFrameworkCore;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Kabul bulgusu — "araç şu an neye bağlı" sorgularının TEK yeri. Araç kartı durum kuralı, "Servise Başla" ve BAF
/// tahsisi aynı tanımı kullanır: açık kira = <see cref="RentalStatus.Kirada"/> sözleşme (teslim edilmemiş olsa da araç
/// sözleşmeye bağlıdır), açık tahsis = <see cref="BafStatus.Acik"/>, serviste = <see cref="ServiceStatus.Serviste"/>.
/// Çağıranın kendi transaction'ında (ve gerekiyorsa araç satır kilidinin arkasında) çalışır.
/// </summary>
internal static class VehicleCommitmentQueries
{
    public static Task<string?> OpenRentalNoAsync(AppDbContext db, Guid vehicleId, CancellationToken ct)
        => db.Rentals.AsNoTracking().Where(r => r.VehicleId == vehicleId && r.Durum == RentalStatus.Kirada)
            .OrderBy(r => r.CreatedAtUtc).Select(r => r.SozlesmeNo).FirstOrDefaultAsync(ct);

    public static Task<string?> ActiveServiceNoAsync(AppDbContext db, Guid vehicleId, CancellationToken ct)
        => db.ServiceRecords.AsNoTracking().Where(s => s.VehicleId == vehicleId && s.Durum == ServiceStatus.Serviste)
            .OrderBy(s => s.CreatedAtUtc).Select(s => s.No).FirstOrDefaultAsync(ct);

    public static Task<string?> OpenBafNoAsync(AppDbContext db, Guid vehicleId, CancellationToken ct)
        => db.Baflar.AsNoTracking().Where(b => b.VehicleId == vehicleId && b.Durum == BafStatus.Acik)
            .OrderBy(b => b.CreatedAtUtc).Select(b => b.No).FirstOrDefaultAsync(ct);

    public static Task<string?> CompletedSaleNoAsync(AppDbContext db, Guid vehicleId, CancellationToken ct)
        => db.VehicleSales.AsNoTracking().Where(s => s.VehicleId == vehicleId && s.Durum == SaleStatus.Tamamlandi)
            .OrderBy(s => s.CreatedAtUtc).Select(s => s.No).FirstOrDefaultAsync(ct);

    public static async Task<VehicleCommitments> AllAsync(AppDbContext db, Guid vehicleId, CancellationToken ct)
        => new(await OpenRentalNoAsync(db, vehicleId, ct), await ActiveServiceNoAsync(db, vehicleId, ct),
            await OpenBafNoAsync(db, vehicleId, ct), await CompletedSaleNoAsync(db, vehicleId, ct));

    /// <summary>Araç satırını transaction sonuna kadar kilitler (araç yoksa no-op). Aynı araca eşzamanlı tahsis /
    /// servise başlama / kira teslimi (araç satırını günceller) bu kilitte serileşir.</summary>
    public static Task LockVehicleAsync(AppDbContext db, Guid vehicleId, CancellationToken ct)
        => db.Vehicles.FromSqlRaw("SELECT * FROM \"Vehicles\" WHERE \"Id\" = {0} FOR UPDATE", vehicleId)
            .AsNoTracking().Select(v => v.Id).FirstOrDefaultAsync(ct);
}
