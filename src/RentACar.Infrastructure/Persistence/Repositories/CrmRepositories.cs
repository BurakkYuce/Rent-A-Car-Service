using Microsoft.EntityFrameworkCore;
using RentACar.Application.Crm;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>CRM anket repo'su (roadmap C3). Tenant izolasyonu RLS + query filter ile otomatik.</summary>
public sealed class AnketRepository(IDbContextFactory<AppDbContext> factory) : IAnketRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Anket>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Anketler.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<Anket?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Anketler.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateAsync(Anket row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Anketler.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Anket> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Anketler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Anketler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        db.Anketler.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>CRM şikayet repo'su (roadmap C3). Tenant izolasyonu RLS + query filter ile otomatik.</summary>
public sealed class SikayetRepository(IDbContextFactory<AppDbContext> factory) : ISikayetRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Sikayet>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Sikayetler.AsNoTracking().OrderByDescending(r => r.Tarih).ToListAsync(ct);
    }

    public async Task<Sikayet?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Sikayetler.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateAsync(Sikayet row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Sikayetler.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Sikayet> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Sikayetler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Sikayetler.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        db.Sikayetler.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
