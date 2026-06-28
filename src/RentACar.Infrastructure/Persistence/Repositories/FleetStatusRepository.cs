using Microsoft.EntityFrameworkCore;
using RentACar.Application.Fleet;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Araç Güncel Durum birleşik sorgusu. Araç filtreleri SQL'de uygulanır; aktif kira (Durum=Kirada)
/// + müşteri adı bellek içinde birleştirilir (tenant başına filo boyutu ölçülü). Tenant izolasyonu
/// RLS + query filter ile otomatik.
/// </summary>
public sealed class FleetStatusRepository(IDbContextFactory<AppDbContext> factory) : IFleetStatusRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<FleetStatusRow>> QueryAsync(FleetStatusFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Vehicles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Sube)) q = q.Where(v => v.Sube == filter.Sube);
        if (filter.Durum is { } d) q = q.Where(v => v.Durum == d);
        if (filter.FiloDurum is { } f) q = q.Where(v => v.FiloDurum == f);
        if (filter.Vites is { } vt) q = q.Where(v => v.Vites == vt);
        if (filter.Yakit is { } y) q = q.Where(v => v.Yakit == y);
        if (!string.IsNullOrWhiteSpace(filter.Grup)) q = q.Where(v => v.Grup == filter.Grup);
        if (!string.IsNullOrWhiteSpace(filter.Marka)) q = q.Where(v => v.Marka == filter.Marka);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            q = q.Where(v => EF.Functions.ILike(v.Plaka, term)
                || (v.Marka != null && EF.Functions.ILike(v.Marka, term)));
        }

        var vehicles = await q.OrderBy(v => v.Plaka).ToListAsync(ct);

        // Aktif kiralar (yalnız bu araçlar için) + müşteri adı.
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var activeRentals = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum == RentalStatus.Kirada && vehicleIds.Contains(r.VehicleId))
            .ToListAsync(ct);

        // Araç başına tek aktif kira (en geç başlayan — normalde tek olur).
        var rentalByVehicle = activeRentals
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.BasTar).First());

        var custIds = activeRentals.Select(r => r.MusteriId).Distinct().ToList();
        var custNames = await db.Customers.AsNoTracking()
            .Where(c => custIds.Contains(c.Id))
            .ToListAsync(ct);
        var custNameById = custNames.ToDictionary(c => c.Id, c => c.DisplayName);

        var rows = vehicles.Select(v =>
        {
            rentalByVehicle.TryGetValue(v.Id, out var rental);
            return new FleetStatusRow
            {
                VehicleId = v.Id,
                Plaka = v.Plaka,
                Marka = v.Marka,
                Tip = v.Tip,
                Grup = v.Grup,
                Segment = v.Segment,
                Sipp = v.Sipp,
                Vites = v.Vites,
                Yakit = v.Yakit,
                Km = v.Km,
                Sube = v.Sube,
                Durum = v.Durum,
                FiloDurum = v.FiloDurum,
                AktifKiraId = rental?.Id,
                KiraSozlesmeNo = rental?.SozlesmeNo,
                MusteriAd = rental is null ? null : custNameById.GetValueOrDefault(rental.MusteriId),
                KiraBitTar = rental?.BitTar,
                KiraBakiye = rental?.Bakiye
            };
        });

        // Kirada filtresi (rental varlığına bağlı → projeksiyon sonrası).
        if (filter.KiradaMi is { } kirada)
            rows = rows.Where(r => r.Kirada == kirada);

        return rows.ToList();
    }
}
