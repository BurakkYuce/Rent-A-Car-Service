using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Regulation;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

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

    public async Task<MtvRecord?> FindMtvAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MtvRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task PostMtvOdemeAsync(Guid mtvId, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default)
    {
        var debit = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        if (debit != credit) throw new ValidationException($"MTV ödeme defteri dengesiz: borç {debit} ≠ alacak {credit}.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var rec = await db.MtvRecords.FirstOrDefaultAsync(x => x.Id == mtvId, ct)
            ?? throw new ValidationException("MTV kaydı bulunamadı.");
        if (rec.Odendi) throw new ValidationException("MTV zaten ödendi.");
        rec.Odendi = true;
        rec.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.AccountLedgerEntries.AddRange(entries);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Eşzamanlı çift-ödeme: deterministik SourceId=mtvId idem index'ine takıldı → ilk ödeme kazandı.
            await tx.RollbackAsync(ct);
            throw new ValidationException("MTV zaten ödendi.");
        }
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
