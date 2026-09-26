using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// <see cref="IMasterDefinitionRepository{T}"/> generic gövdesi (denetim O12d — 10 birebir-kopya reponun
/// ortak tabanı): kısa-ömürlü context'ler (factory), AsNoTracking, <c>db.Set&lt;T&gt;()</c>.
/// Tenant izolasyonu RLS + query filter ile otomatik. Kod benzersizliği DB unique index ile;
/// ihlal (23505) → adTekil ile entity-özgü ValidationException ("'X' kodlu marka zaten var.").
/// Alt sınıflar İNCEDİR: yalnız factory + adTekil geçirip somut IXRepository'yi işaretler.
/// </summary>
public abstract class MasterTanimRepository<T>(IDbContextFactory<AppDbContext> factory, string adTekil)
    : IMasterDefinitionRepository<T> where T : class, IMasterDefinition
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;
    private readonly string _adTekil = adTekil;

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<T>().AsNoTracking().OrderBy(x => x.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<T>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<T>().AsNoTracking().Where(x => x.Aktif).OrderBy(x => x.Ad).ToListAsync(ct);
    }

    public async Task<T?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var k = kod.Trim().ToUpperInvariant();
        return await db.Set<T>().AsNoTracking()
            .Where(x => x.Kod == k && (excludeId == null || x.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(T entity, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Set<T>().Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{entity.Kod}' kodlu {_adTekil} zaten var.");
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<T> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;

        apply(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException($"'{entity.Kod}' kodlu {_adTekil} zaten var.");
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return false;

        db.Set<T>().Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // F11.1a: a row referenced by a foreign key is "in use" — a clear message instead of a 500.
            throw new ValidationException(DefinitionMessages.InUse(_adTekil));
        }
        return true;
    }

    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersion.ReadAsync<T>(_factory, id, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default)
        => RowVersion.ReadAllAsync<T>(_factory, ct);

    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<T> apply, CancellationToken ct = default)
        => RowVersion.UpdateAsync(_factory, id, expectedVersion, apply, e => $"'{e.Kod}' kodlu {_adTekil} zaten var.", ct);
}
