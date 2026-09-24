using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// ILocationRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// F11.1b güvenlik H1: ofis ADI anahtarı da kiracı içinde benzersiz (<see cref="RequireUniqueNameAsync"/>).
/// </summary>
public sealed class LocationRepository(IDbContextFactory<AppDbContext> factory) : ILocationRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking().OrderBy(l => l.Kod).ToListAsync(ct);
    }

    public async Task<Location?> FindByAdAsync(string ad, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // F4.1 adversarial N1: eşleşme OfficeBranchInterceptor ile AYNI anahtarla ve BELLEKTE (SQL lower()
        // Türkçe 'İ'yi .NET'ten farklı küçültüyordu).
        var anahtar = OfisAdiAnahtari.Uret(ad);
        var matches = (await db.Locations.AsNoTracking().ToListAsync(ct))
            .Where(l => OfisAdiAnahtari.Uret(l.Ad) == anahtar)
            .OrderBy(l => l.Kod)
            .ToList();
        if (matches.Count == 0) return null;
        // F11.1b güvenlik H1: farklı şubelere bağlı aynı adlı (tarihsel) ofisler BELİRSİZ → null; interceptor ile
        // aynı kural. Hepsi aynı şubedeyse (ya da hepsi şubesiz) ilk kayıt.
        return matches.Select(l => l.SubeId).Distinct().Count() == 1 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking().Where(l => l.Aktif).OrderBy(l => l.Ad).ToListAsync(ct);
    }

    public async Task<Location?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Locations.AsNoTracking()
            .Where(l => l.Kod == k && (excludeId == null || l.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Location location, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await RequireUniqueNameAsync(db, location.Ad, null, ct);
            db.Locations.Add(location);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{location.Kod}' kodlu ofis zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Location> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (loc is null) return false;

        apply(loc);
        try
        {
            await RequireUniqueNameAsync(db, loc.Ad, id, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{loc.Kod}' kodlu ofis zaten var.");
        }
        return true;
    }

    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması (<see cref="SatirSurumu"/>) + ad benzersizliği
    /// AYNI işlemde (advisory kilit bul-callback'inde alınır; uygulanacak ad geçici kopyadan hesaplanır).</summary>
    public async Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<Location> apply, CancellationToken ct = default)
    {
        string? code = null;
        try
        {
            return await SatirSurumu.GuncelleAsync(_factory, SatirSurumu.Locations, id, expectedVersion,
                async (db, k, c) =>
                {
                    var probe = new Location();
                    apply(probe);
                    await RequireUniqueNameAsync(db, probe.Ad, k, c);
                    return await db.Locations.FirstOrDefaultAsync(x => x.Id == k, c);
                },
                x => { apply(x); code = x.Kod; }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{code}' kodlu ofis zaten var.");
        }
    }

    public async Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.Locations, id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var loc = await db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (loc is null) return false;

        db.Locations.Remove(loc);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// F11.1b güvenlik H1 — ofis adı anahtarı (<see cref="OfisAdiAnahtari.Uret"/>) kiracı içinde benzersiz. Kontrol
    /// kiracı başına işlem-içi advisory kilit ALTINDA yapılır: iki eşzamanlı yazım ikisi de "yok" görüp geçemez.
    /// Aynı adlı ofis, ofis→şube türetmesini belirsizleştirip başka şubenin kayıtlarını ele geçirmeye yarıyordu.
    /// DB unique index BİLİNÇLİ yok: anahtar .NET <c>ToLowerInvariant</c> ile üretilir (Postgres <c>lower()</c> 'İ'yi
    /// farklı küçültür) ve canlı veride tarihsel çakışma olabilir — index migration'ı kırardı. Tarihsel çakışmalar
    /// artık belirsiz sayılır ve hiçbir şubeye çözülmez (interceptor).
    /// </summary>
    private static async Task RequireUniqueNameAsync(AppDbContext db, string ad, Guid? excludeId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended({0}, 0))", ["location-name:" + db.TenantId], ct);
        var key = OfisAdiAnahtari.Uret(ad ?? "");
        var names = await db.Locations.AsNoTracking()
            .Where(l => excludeId == null || l.Id != excludeId)
            .Select(l => l.Ad).ToListAsync(ct);
        if (names.Any(a => OfisAdiAnahtari.Uret(a) == key))
            throw new ValidationException($"Ofis adı '{(ad ?? "").Trim()}' zaten kullanılıyor (büyük/küçük harf ve boşluk farkı sayılmaz).");
    }
}
