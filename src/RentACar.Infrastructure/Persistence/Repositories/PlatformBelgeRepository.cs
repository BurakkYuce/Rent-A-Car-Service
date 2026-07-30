using Microsoft.EntityFrameworkCore;
using RentACar.Application.PlatformBelgeler;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IPlatformBelgeRepository (PR-B). <see cref="PlatformBelge"/> PLATFORM tablosudur — merkezi tenant
/// query filter'ı YOK ve RLS YOK. Bu yüzden hedef/yayın/rol filtresi <b>burada, her sorguda elle</b>
/// uygulanır (<c>UserRepository</c>'nin "Users platform tablosudur → filtre BURADA" deseni).
///
/// <para>Filtre TEK bir private ifadede toplandı: liste ve indirme aynı kuralı paylaşır. İki yerde
/// ayrı yazılsaydı listede görünmeyen bir belge indirilebilir hale gelebilirdi — bu PR'ın en kritik
/// hata sınıfı tam olarak budur.</para>
/// </summary>
public sealed class PlatformBelgeRepository(IDbContextFactory<AppDbContext> factory) : IPlatformBelgeRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    /// <summary>Son bu kadar günde güncellenen belge "Yeni" etiketi alır.</summary>
    private const int YeniGunSayisi = 14;

    /// <summary>
    /// Görünürlük yüklemi — DÖRT koşul:
    /// (1) yayında · (2) global VEYA bu tenant'a hedefli · (3) herkese açık VEYA yönetici ·
    /// (4) [çağıranda] oturum açık. (4) uygulama katmanında (uç/servis) sağlanır.
    /// </summary>
    private static IQueryable<PlatformBelge> Gorunur(AppDbContext db, Guid tenantId, bool yoneticiMi)
        => db.PlatformBelgeler.AsNoTracking()
            .Where(b => b.Durum == PlatformBelgeDurum.Yayinda)
            .Where(b => !db.PlatformBelgeHedefler.Any(h => h.BelgeId == b.Id)          // global
                     || db.PlatformBelgeHedefler.Any(h => h.BelgeId == b.Id && h.TenantId == tenantId))
            .Where(b => !b.YalnizYoneticiler || yoneticiMi);

    public async Task<IReadOnlyList<FirmaBelgeSatiri>> ListeleAsync(
        Guid tenantId, bool yoneticiMi, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var esik = DateTimeOffset.UtcNow.AddDays(-YeniGunSayisi);
        // Select PROJEKSİYONU: `Bytes` kolonuna DOKUNULMAZ (bytea/TOAST faturası).
        return await Gorunur(db, tenantId, yoneticiMi)
            .OrderByDescending(b => b.GuncellemeUtc).ThenBy(b => b.Baslik)
            .Select(b => new FirmaBelgeSatiri(
                b.Id, b.Baslik, b.Aciklama, b.Surum, b.GuncellemeUtc, b.Boyut, b.GuncellemeUtc >= esik))
            .ToListAsync(ct);
    }

    public async Task<BelgeIcerik?> IndirAsync(
        Guid belgeId, Guid tenantId, bool yoneticiMi, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // AYNI yüklem + id. Id'ye asla tek başına güvenilmez.
        return await Gorunur(db, tenantId, yoneticiMi)
            .Where(b => b.Id == belgeId)
            .Select(b => new BelgeIcerik(b.Bytes, b.DosyaAdi, b.Surum))
            .FirstOrDefaultAsync(ct);
    }
}
