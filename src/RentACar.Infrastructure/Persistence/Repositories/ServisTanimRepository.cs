using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.ServisTanimlari;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Servis tanım kalıcılığı (roadmap N1). Kod benzersizliği DB unique index; 23505→ValidationException.</summary>
public sealed class ServisTanimRepository(IDbContextFactory<AppDbContext> factory) : IServiceDefinitionRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<ServisTanim>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ServisTanimlari.AsNoTracking().OrderBy(c => c.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ServisTanim>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ServisTanimlari.AsNoTracking().Where(c => c.Aktif).OrderBy(c => c.AracTipi).ToListAsync(ct);
    }

    public async Task<ServisTanim?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ServisTanimlari.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task CreateAsync(ServisTanim row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ServisTanimlari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Kod}' kodlu servis tanımı zaten var."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<ServisTanim> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.ServisTanimlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Kod}' kodlu servis tanımı zaten var."); }
        return true;
    }

    public async Task<IReadOnlyList<FiloKombinasyon>> FleetCombinationsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Yalnız KİRALANABİLİR filo: satılmış/pasif araca bakım tanımı önermek gürültüdür.
        var ham = await db.Vehicles.AsNoTracking()
            .Where(v => v.Durum != RentACar.Domain.Enums.VehicleStatus.Satildi
                        && v.Durum != RentACar.Domain.Enums.VehicleStatus.Pasif)
            .Select(v => new { v.Marka, v.Tip, v.Yakit, v.Vites })
            .ToListAsync(ct);

        // Gruplama BELLEKTE ve normalize anahtarla: "BMW" ile "bmw " tek kombinasyon sayılır
        // (DB tarafında yapsaydık collation'a bağımlı olurdu).
        return ham
            .GroupBy(v => ServiceDefinitionCombination.Key(v.Marka, v.Tip, v.Yakit?.ToString(), v.Vites?.ToString()),
                StringComparer.Ordinal)
            .Select(g => new FiloKombinasyon(
                Temsilci(g.Select(x => x.Marka)),
                Temsilci(g.Select(x => x.Tip)),
                Temsilci(g.Select(x => x.Yakit?.ToString())),
                Temsilci(g.Select(x => x.Vites?.ToString())),
                g.Count()))
            .OrderByDescending(x => x.AracSayisi).ThenBy(x => x.Etiket, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// Grubun GÖSTERİLECEK yazımı. `g.First()` KULLANILMAZ: anahtar harf duyarsız olduğundan
    /// "Renault" ve " renault " aynı gruba düşer ve First() DB satır sırasına göre değişir —
    /// öneri etiketi (ve kabul edilince kaydedilen değer) koşudan koşuya farklı çıkardı.
    /// Kural: EN SIK geçen yazım kazanır, eşitlikte alfabetik ilk (deterministik).
    /// </summary>
    private static string? Temsilci(IEnumerable<string?> degerler)
        => degerler.Select(x => x?.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .GroupBy(x => x, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.ServisTanimlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        db.ServisTanimlari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
