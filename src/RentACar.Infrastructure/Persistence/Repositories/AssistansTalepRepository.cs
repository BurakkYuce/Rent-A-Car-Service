using Microsoft.EntityFrameworkCore;
using RentACar.Application.Crm;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Assistans (yol yardım) talebi kalıcılığı — FAZ-44.</summary>
public sealed class AssistansTalepRepository(IDbContextFactory<AppDbContext> factory) : IAssistansTalepRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<AssistansTalep>> SearchAsync(
        AssistansFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.AssistansTalepleri.AsNoTracking();

        if (filtre.TarihMin is { } min) q = q.Where(x => x.Zaman >= min);
        if (filtre.TarihMax is { } max) q = q.Where(x => x.Zaman <= max);
        if (filtre.Kapandi is bool k) q = q.Where(x => x.Kapandi == k);
        if (filtre.YedekLastikMi is bool y) q = q.Where(x => x.YedekLastikMi == y);
        // "Hareket edemiyor" = AracHareketMi FALSE — çekici gereken çağrılar. Kolonun adı olumlu
        // olduğu için filtrenin adı da olumsuz seçildi; ekranda operatör "çekici gerekenler" arar.
        if (filtre.HareketEdemiyor is bool h) q = q.Where(x => x.AracHareketMi == !h);

        if (!string.IsNullOrWhiteSpace(filtre.Plaka))
        {
            // Plaka DB'de normalize (34AA01); kullanıcı "34 AA" yazabilir → arama terimi de
            // normalize edilir, yoksa boşluklu giriş hiçbir şey bulmaz (FAZ-63 dersi).
            var p = AssistansTalepService.PlakaNormalize(filtre.Plaka);
            if (p.Length > 0) q = q.Where(x => x.Plaka != null && x.Plaka.Contains(p));
        }

        if (!string.IsNullOrWhiteSpace(filtre.Ara))
        {
            var a = filtre.Ara.Trim();
            q = q.Where(x => EF.Functions.ILike(x.Mesaj, $"%{a}%")
                          || (x.Sebep != null && EF.Functions.ILike(x.Sebep, $"%{a}%"))
                          || (x.AdSoyad != null && EF.Functions.ILike(x.AdSoyad, $"%{a}%"))
                          || (x.CepTel != null && EF.Functions.ILike(x.CepTel, $"%{a}%")));
        }

        return await q.OrderByDescending(x => x.Zaman).ToListAsync(ct);
    }

    public async Task<AssistansTalep?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AssistansTalepleri.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task CreateAsync(AssistansTalep row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.AssistansTalepleri.Add(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(Guid id, Action<AssistansTalep> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AssistansTalepleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.AssistansTalepleri.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return false;
        db.AssistansTalepleri.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
