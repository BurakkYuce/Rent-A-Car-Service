using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.TabloDuzenleri;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Kişisel tablo düzenleri (F3.5). Tenant: merkezi EF filtresi + FORCE RLS. Kullanıcı: her sorguda
/// açık <c>UserId</c> yüklemi (çağıran servis oturumdan verir).
/// </summary>
public sealed class TableLayoutRepository(IDbContextFactory<AppDbContext> factory) : ITableLayoutRepository
{
    public async Task<TabloDuzeniKaydi?> FetchAsync(Guid userId, string tableCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.TabloDuzenleri.AsNoTracking()
            .Where(d => d.UserId == userId && d.TabloKodu == tableCode)
            .Select(d => new TabloDuzeniKaydi(d.Duzen, d.UpdatedAtUtc))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<DateTimeOffset> WriteAsync(
        Guid userId, string tableCode, string layoutJson, DateTimeOffset now, CancellationToken ct = default)
    {
        // İki deneme yeter: ilk INSERT eşzamanlı başka bir ilk yazıma (iki sekme) çarparsa satır artık
        // vardır ve ikinci tur onu GÜNCELLER (son yazan kazanır). Satır ikinci turda da yoksa (arada
        // silindi) yeniden eklenir; üçüncü bir yarış gerçekçi değil, hata yukarı çıkar.
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var existing = await db.TabloDuzenleri
                .SingleOrDefaultAsync(d => d.UserId == userId && d.TabloKodu == tableCode, ct);
            if (existing is null)
            {
                db.TabloDuzenleri.Add(new TabloDuzeni
                {
                    UserId = userId,
                    TabloKodu = tableCode,
                    Duzen = layoutJson,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
            }
            else
            {
                existing.Duzen = layoutJson;
                existing.UpdatedAtUtc = now;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return now;
            }
            catch (DbUpdateException ex) when (attempt == 1 && existing is null
                                               && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Eşzamanlı ilk yazım kazandı → güncelleme turuna geç.
            }
        }
    }

    public async Task DeleteAsync(Guid userId, string tableCode, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.TabloDuzenleri
            .Where(d => d.UserId == userId && d.TabloKodu == tableCode)
            .ExecuteDeleteAsync(ct);
    }
}
