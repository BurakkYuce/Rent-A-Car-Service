using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.DropTanimlari;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>Drop matris kalıcılığı (roadmap N2). (Lokasyon,Sube) benzersiz; 23505→ValidationException.</summary>
public sealed class DropTanimRepository(IDbContextFactory<AppDbContext> factory) : IDropDefinitionRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<DropTanim>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DropTanimlari.AsNoTracking().OrderBy(c => c.Lokasyon).ThenBy(c => c.Sube).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DropTanim>> SearchAsync(
        RentACar.Application.DropTanimlari.DropTanimFilter filtre, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Durum süzgeci SQL'de (kültürden bağımsız), METİN karşılaştırması BELLEKTE.
        //
        // NEDEN BELLEKTE: SQL tarafında lower()/ILIKE kullanınca sonuç DB'nin collation'ına
        // bağlanıyor ve motorla ayrışıyor. Ampirik: PG (en_US.UTF-8) lower('İstanbul') = 'istanbul',
        // .NET/ICU ise 'i̇stanbul' (i + U+0307) üretiyor → eşleşme kaybı. Yerelde geçip CI'da
        // (postgres:16, farklı libc) patlayan tam olarak buydu. Karşılaştırma artık motorun
        // kullandığı OrdinalIgnoreCase ile AYNI yerde ve AYNI kuralla yapılıyor → liste ile ücret
        // motoru asla ayrışamaz (bu filtrenin varlık sebebi de buydu).
        // Maliyet: DropTanimlari küçük bir master tablo (tenant başına onlarca satır).
        var q = db.DropTanimlari.AsNoTracking();
        if (filtre.Aktif is bool a) q = q.Where(x => x.Aktif == a);
        var rows = await q.ToListAsync(ct);

        static bool Es(string? x, string? v)
            => string.Equals((x ?? "").Trim(), (v ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        IEnumerable<DropTanim> sonuc = rows;
        if (!string.IsNullOrWhiteSpace(filtre.DonusLokasyon))
            sonuc = sonuc.Where(x => Es(x.Lokasyon, filtre.DonusLokasyon));
        if (!string.IsNullOrWhiteSpace(filtre.CikisLokasyon))
            sonuc = sonuc.Where(x => x.CikisLokasyon is not null && Es(x.CikisLokasyon, filtre.CikisLokasyon));
        if (!string.IsNullOrWhiteSpace(filtre.Sube))
            sonuc = sonuc.Where(x => Es(x.Sube, filtre.Sube));

        return sonuc
            .OrderBy(c => c.Lokasyon, StringComparer.CurrentCulture)
            .ThenBy(c => c.Sube, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<DropTanim?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DropTanimlari.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task CreateAsync(DropTanim row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.DropTanimlari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Lokasyon} → {row.Sube}' drop tanımı zaten var."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<DropTanim> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DropTanimlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        apply(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Lokasyon} → {row.Sube}' drop tanımı zaten var."); }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DropTanimlari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        db.DropTanimlari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // F11.1a — IVersionedRepository<DropTanim> (generic RowVersion helper).
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => RowVersion.ReadAsync<DropTanim>(_factory, id, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => RowVersion.ReadAllAsync<DropTanim>(_factory, ct);

    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<DropTanim> apply, CancellationToken ct = default)
        => RowVersion.UpdateAsync(_factory, id, expectedVersion, apply, r => $"'{r.Lokasyon} → {r.Sube}' drop tanımı zaten var.", ct);
}
