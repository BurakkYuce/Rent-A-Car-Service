using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IVehicleGroupRepository: kısa-ömürlü context'ler (factory). Tenant izolasyonu RLS + query
/// filter ile otomatik. Kod benzersizliği DB unique index ile; ihlal (23505) ValidationException.
/// </summary>
public sealed class VehicleGroupRepository(IDbContextFactory<AppDbContext> factory) : IVehicleGroupRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<VehicleGroup>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleGroups.AsNoTracking().OrderBy(g => g.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VehicleGroup>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleGroups.AsNoTracking().Where(g => g.Aktif).OrderBy(g => g.Ad).ToListAsync(ct);
    }

    public async Task<VehicleGroup?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VehicleGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.VehicleGroups.AsNoTracking()
            .Where(g => g.Kod == k && (excludeId == null || g.Id != excludeId))
            .AnyAsync(ct);
    }

    /// <summary>Türkçe katlama (İ/I/ı/i) bir .NET comparer'dır, SQL'e çevrilemez → adaylar (grup sayısı
    /// azdır) belleğe çekilip <see cref="TurkishText"/> ile karşılaştırılır. Ordinal `==` kullanmak
    /// "EKONOMİ" ile "ekonomi"yi FARKLI sayıp çakışmayı sessizce geçirirdi.</summary>
    public async Task<bool> NameExistsAsync(string ad, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var adaylar = await db.VehicleGroups.AsNoTracking()
            .Where(g => excludeId == null || g.Id != excludeId)
            .Select(g => g.Ad)
            .ToListAsync(ct);
        return adaylar.Any(x => TurkishText.EqualsIgnoreTurkishCase(x, ad));
    }

    public async Task CreateAsync(VehicleGroup group, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.VehicleGroups.Add(group);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{group.Kod}' kodlu araç grubu zaten var.");
        }
    }

    public async Task<GrupGuncellemeSonuc> UpdateAsync(Guid id, Action<VehicleGroup> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var group = await db.VehicleGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return new GrupGuncellemeSonuc(false, 0);

        var eskiAd = group.Ad;
        apply(group);

        // Rename cascade — AYNI SaveChanges. Ad değişip araçlar taşınmazsa filo sessizce eşleşmez
        // hale gelir (vitrin/arama boşalır). Grup pasifleştirilerek yeniden adlandırılırsa araçlar
        // yine taşınır ama vitrinden düşer: İSTENEN davranış (pasif grup yayınlanmaz).
        var tasinan = TurkishText.EqualsIgnoreTurkishCase(eskiAd, group.Ad)
            ? 0
            : await TasiAsync(db, grup => TurkishText.EqualsIgnoreTurkishCase(grup, eskiAd), group.Ad, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{group.Kod}' kodlu araç grubu zaten var.");
        }
        return new GrupGuncellemeSonuc(true, tasinan);
    }

    /// <summary>
    /// F11.1b — sürümlü tam değiştirme: satır kilidi + xmin karşılaştırması + (ad değiştiyse) rename cascade
    /// TEK işlemde. Sürüm uyuşmazlığında hiçbir şey yazılmaz (<see cref="ConcurrentModificationException"/>).
    /// </summary>
    public async Task<GrupGuncellemeSonuc> UpdateAsync(Guid id, string? expectedVersion, Action<VehicleGroup> apply, CancellationToken ct = default)
    {
        string? code = null;
        try
        {
            return await PgRetry.RunAsync(async () =>
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await SatirSurumu.KilitleAsync(db, SatirSurumu.VehicleGroups, id, ct);
                if (expectedVersion is not null
                    && await SatirSurumu.OkuAsync(db, SatirSurumu.VehicleGroups, id, ct) is { } current
                    && !string.Equals(current, expectedVersion.Trim(), StringComparison.Ordinal))
                    throw new ConcurrentModificationException(ConcurrentModificationException.RecordMessage);

                var group = await db.VehicleGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
                if (group is null) return new GrupGuncellemeSonuc(false, 0);
                var oldName = group.Ad;
                apply(group);
                code = group.Kod;
                var moved = TurkishText.EqualsIgnoreTurkishCase(oldName, group.Ad)
                    ? 0
                    : await TasiAsync(db, grup => TurkishText.EqualsIgnoreTurkishCase(grup, oldName), group.Ad, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return new GrupGuncellemeSonuc(true, moved);
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{code}' kodlu araç grubu zaten var.");
        }
    }

    public async Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await SatirSurumu.OkuAsync(db, SatirSurumu.VehicleGroups, id, ct);
    }

    public async Task<int> MoveGroupValueAsync(string? kaynakDeger, bool bosOlanlar, string hedefAd, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var n = bosOlanlar
            ? await TasiAsync(db, string.IsNullOrWhiteSpace, hedefAd, ct)
            : await TasiAsync(db, grup => TurkishText.EqualsIgnoreTurkishCase(grup, kaynakDeger), hedefAd, ct);
        if (n > 0) await db.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>Eşleme + rename-cascade'in ORTAK ÇEKİRDEĞİ (ikisi de buradan geçer, tek davranış).
    /// <c>ExecuteUpdateAsync</c> BİLİNÇLİ OLARAK kullanılmaz: (a) <see cref="TurkishText"/> bir .NET
    /// comparer'dır, SQL'e çevrilemez; (b) bulk update SaveChanges interceptor'larını atlar → audit ve
    /// <c>UpdatedAtUtc</c> yazılmaz. Filo tenant başına onlarca satırdır; adaylar belleğe çekilip
    /// TRACKING ile güncellenir. <b>SaveChanges çağırana aittir</b> — rename bunu grup güncellemesiyle
    /// aynı transaction'da yapar.</summary>
    private static async Task<int> TasiAsync(
        AppDbContext db, Func<string?, bool> eslesir, string hedefAd, CancellationToken ct)
    {
        var adaylar = await db.Vehicles.ToListAsync(ct);
        var n = 0;
        foreach (var v in adaylar.Where(v => eslesir(v.Grup)))
        {
            v.Grup = hedefAd;
            v.UpdatedAtUtc = DateTimeOffset.UtcNow;
            n++;
        }
        return n;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var group = await db.VehicleGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group is null) return false;

        db.VehicleGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
