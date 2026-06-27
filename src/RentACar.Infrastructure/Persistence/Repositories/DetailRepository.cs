using Microsoft.EntityFrameworkCore;
using RentACar.Application.Details;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Salt-okunur 360° detay sorguları. Araç: kira/servis/ceza/hasar. Cari: bakiye (Σ cari defter
/// işaretli) + kiralar + son ekstre. Tenant izolasyonu otomatik (query filter + RLS).
/// </summary>
public sealed class DetailRepository(IDbContextFactory<AppDbContext> factory) : IDetailRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<VehicleDetailDto?> GetVehicleDetailAsync(Guid vehicleId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vehicleId, ct);
        if (vehicle is null) return null;

        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId).OrderByDescending(r => r.BasTar).ToListAsync(ct);
        var services = await db.ServiceRecords.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.VehicleId == vehicleId).OrderByDescending(s => s.GirisTarihi).ToListAsync(ct);
        var penalties = await db.Penalties.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId).OrderByDescending(p => p.TebligTarihi).ToListAsync(ct);
        var damages = await db.DamageFiles.AsNoTracking()
            .Where(d => d.VehicleId == vehicleId).OrderByDescending(d => d.AcilisTarihi).ToListAsync(ct);

        return new VehicleDetailDto(vehicle, rentals, services, penalties, damages);
    }

    public async Task<CustomerDetailDto?> GetCustomerDetailAsync(Guid customerId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null) return null;

        var rentals = await db.Rentals.AsNoTracking()
            .Where(r => r.MusteriId == customerId).OrderByDescending(r => r.BasTar).ToListAsync(ct);

        // Cari bakiye = Σ (Borç +AmountInBase, Alacak −AmountInBase) (AmountInBase = Amount × Rate).
        var ledger = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.AccountRef == customerId)
            .OrderByDescending(e => e.EntryDateUtc)
            .ToListAsync(ct);
        var bakiye = ledger.Sum(e => e.SignedBase);
        var recent = ledger.Take(20).ToList();

        return new CustomerDetailDto(customer, bakiye, rentals, recent);
    }
}
