using Microsoft.EntityFrameworkCore;
using RentACar.Application.Regulation;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Sigorta/MTV/Muayene CRUD + birleşik vade kaynakları.</summary>
public sealed class RegulationRepository(IDbContextFactory<AppDbContext> factory) : IRegulationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<InsurancePolicy>> ListInsuranceAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InsurancePolicies.AsNoTracking().OrderByDescending(x => x.Bitis).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MtvRecord>> ListMtvAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvRecords.AsNoTracking().OrderByDescending(x => x.Vade).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<InspectionRecord>> ListInspectionAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.InspectionRecords.AsNoTracking().OrderByDescending(x => x.Bitis).ToListAsync(ct);
    }

    public async Task AddInsuranceAsync(InsurancePolicy policy, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InsurancePolicies.Add(policy);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddMtvAsync(MtvRecord record, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.MtvRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddInspectionAsync(InspectionRecord record, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.InspectionRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<VadeSource>> GetVadeSourcesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var insurance = await db.InsurancePolicies.AsNoTracking()
            .Select(x => new VadeSource(x.VehicleId, x.Tip == Domain.Enums.InsuranceType.Kasko ? "Kasko" : "Trafik", x.Bitis))
            .ToListAsync(ct);
        var mtv = await db.MtvRecords.AsNoTracking()
            .Where(x => !x.Odendi)
            .Select(x => new VadeSource(x.VehicleId, "MTV", x.Vade))
            .ToListAsync(ct);
        var inspection = await db.InspectionRecords.AsNoTracking()
            .Select(x => new VadeSource(x.VehicleId, "Muayene", x.Bitis))
            .ToListAsync(ct);

        return [.. insurance, .. mtv, .. inspection];
    }
}
