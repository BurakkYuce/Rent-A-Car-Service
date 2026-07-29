using Microsoft.EntityFrameworkCore;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IWebIlanRepository (PR-13). Kısa-ömürlü context'ler (factory); tenant izolasyonu query filter +
/// RLS ile otomatik. Yazma yolları TEK SaveChanges kullanır — sihirbaz yarıda kalırsa filo
/// yarı-ilanlı kalmamalı.
/// </summary>
public sealed class WebIlanRepository(IDbContextFactory<AppDbContext> factory) : IWebIlanRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    /// <summary>İlan + üye araç + özellik: ÜÇ sorgu (ilan sayısı kadar DEĞİL). Bellekte birleştirilir.</summary>
    public async Task<IReadOnlyList<WebIlanDetay>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ilanlar = await db.WebIlanlar.AsNoTracking()
            .OrderBy(i => i.Sira).ThenBy(i => i.Baslik) // tie-break ŞART: eşit Sira'da Postgres sıra garanti etmez
            .ToListAsync(ct);
        if (ilanlar.Count == 0) return [];

        var idler = ilanlar.Select(i => i.Id).ToList();
        var araclar = await db.Vehicles.AsNoTracking()
            .Where(v => v.WebIlanId != null && idler.Contains(v.WebIlanId.Value))
            .ToListAsync(ct);
        var ozellikler = await db.WebIlanOzellikler.AsNoTracking()
            .Where(o => idler.Contains(o.IlanId))
            .OrderBy(o => o.Sira).ToListAsync(ct);

        return [.. ilanlar.Select(i => new WebIlanDetay(i,
            araclar.Where(v => v.WebIlanId == i.Id).OrderBy(v => v.Plaka).ToList(),
            ozellikler.Where(o => o.IlanId == i.Id).ToList()))];
    }

    public async Task<WebIlanDetay?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ilan = await db.WebIlanlar.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (ilan is null) return null;
        var araclar = await db.Vehicles.AsNoTracking().Where(v => v.WebIlanId == id)
            .OrderBy(v => v.Plaka).ToListAsync(ct);
        var ozellikler = await db.WebIlanOzellikler.AsNoTracking().Where(o => o.IlanId == id)
            .OrderBy(o => o.Sira).ToListAsync(ct);
        return new WebIlanDetay(ilan, araclar, ozellikler);
    }

    public async Task<WebIlanDetay?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var id = await db.WebIlanlar.AsNoTracking().Where(i => i.Slug == slug)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
        return id is { } g ? await FindAsync(g, ct) : null;
    }

    public async Task<IReadOnlyList<string>> ListSluglarAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WebIlanlar.AsNoTracking().Select(i => i.Slug).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WebIlan>> FindByAnahtarAsync(string anahtar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.WebIlanlar.AsNoTracking()
            .Where(i => i.EslesmeAnahtari == anahtar)
            .OrderBy(i => i.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task CreateWithUyelikAsync(
        IReadOnlyList<(WebIlan Ilan, bool Yeni, IReadOnlyList<Guid> AracIdler)> gruplar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var tumIdler = gruplar.SelectMany(g => g.AracIdler).Distinct().ToList();
        // Araçlar TRACKING ile yüklenir: bulk update audit interceptor'ını atlardı.
        var araclar = await db.Vehicles.Where(v => tumIdler.Contains(v.Id)).ToListAsync(ct);

        foreach (var (ilan, yeni, aracIdler) in gruplar)
        {
            if (yeni) db.WebIlanlar.Add(ilan); // katılımda INSERT YOK (aksi halde PK ihlali)
            foreach (var v in araclar.Where(v => aracIdler.Contains(v.Id)))
            {
                v.WebIlanId = ilan.Id;
                v.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct); // TEK transaction — yarım kalan üyelik yok
    }

    public async Task<bool> UpdateAsync(Guid id, Action<WebIlan> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ilan = await db.WebIlanlar.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (ilan is null) return false;
        apply(ilan);
        ilan.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Kardeş = AYNI eşleşme anahtarına sahip, HÂLÂ TASLAK olan diğer ilanlar. Yayındaki bir
    /// ilanın fiyatı buradan DEĞİŞTİRİLMEZ — "ayrı" modda 12 taslak yaratılır, fiyat hepsine iner;
    /// sonradan tek tek düzenlenenler yayında olduğu için korunur.</summary>
    public async Task<int> KardeslereFiyatKopyalaAsync(Guid kaynakIlanId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var kaynak = await db.WebIlanlar.AsNoTracking().FirstOrDefaultAsync(i => i.Id == kaynakIlanId, ct);
        if (kaynak?.EslesmeAnahtari is not { } anahtar) return 0;

        var kardesler = await db.WebIlanlar
            .Where(i => i.Id != kaynakIlanId && i.EslesmeAnahtari == anahtar && i.Durum == WebIlanDurum.Taslak)
            .ToListAsync(ct);
        foreach (var k in kardesler)
        {
            k.GunlukFiyat = kaynak.GunlukFiyat;
            k.HaftalikToplam = kaynak.HaftalikToplam;
            k.AylikToplam = kaynak.AylikToplam;
            k.KdvDahil = kaynak.KdvDahil;
            k.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        if (kardesler.Count > 0) await db.SaveChangesAsync(ct);
        return kardesler.Count;
    }

    public async Task ReplaceOzelliklerAsync(
        Guid ilanId, IReadOnlyList<WebIlanOzellik> satirlar, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var mevcut = await db.WebIlanOzellikler.Where(o => o.IlanId == ilanId).ToListAsync(ct);
        db.WebIlanOzellikler.RemoveRange(mevcut);
        foreach (var s in satirlar) { s.IlanId = ilanId; db.WebIlanOzellikler.Add(s); }
        await db.SaveChangesAsync(ct); // sil+yaz TEK transaction (arada özelliksiz ilan görünmez)
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ilan = await db.WebIlanlar.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (ilan is null) return false;
        // Üyelikleri ÖNCE çöz: FK SetNull zaten yapar ama audit/UpdatedAt yazılsın diye açıkça.
        var araclar = await db.Vehicles.Where(v => v.WebIlanId == id).ToListAsync(ct);
        foreach (var v in araclar) { v.WebIlanId = null; v.UpdatedAtUtc = DateTimeOffset.UtcNow; }
        db.WebIlanlar.Remove(ilan);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<Vehicle>> ListIlansizAraclarAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().Where(v => v.WebIlanId == null)
            .OrderBy(v => v.Marka).ThenBy(v => v.Tip).ThenBy(v => v.Plaka).ToListAsync(ct);
    }
}
