using Microsoft.EntityFrameworkCore;
using RentACar.Application.Notifications;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IBildirimRepository: kısa-ömürlü context (factory). Tenant izolasyonu RLS + query filter.</summary>
public sealed class NotificationRepository(IDbContextFactory<AppDbContext> factory) : INotificationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Bildirim>> ListAsync(bool? isRead = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Bildirimler.AsNoTracking().AsQueryable();
        if (isRead is { } o) q = q.Where(x => x.Okundu == o);
        return await q.OrderByDescending(x => x.OlusturmaTarihi).ThenBy(x => x.VadeTarihi).ToListAsync(ct);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Bildirimler.AsNoTracking().CountAsync(x => !x.Okundu, ct);
    }

    public async Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Bildirimler.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || row.Okundu) return row is not null; // yoksa false; zaten okunduysa idempotent true
        row.Okundu = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var unread = await db.Bildirimler.Where(x => !x.Okundu).ToListAsync(ct);
        foreach (var b in unread) b.Okundu = true;
        if (unread.Count > 0) await db.SaveChangesAsync(ct);
        return unread.Count;
    }
}
