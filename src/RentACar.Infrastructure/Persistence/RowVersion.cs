using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F11.1a — optimistic concurrency for full-replacement PUTs on definition (master) tables, generic over the entity
/// type. Same mechanism as <see cref="SatirSurumu"/> (Postgres <c>xmin</c> as an opaque version text + row lock
/// <c>FOR UPDATE</c>), but the table name comes from the EF model of <typeparamref name="T"/>, never from input —
/// so no whitelist is needed and a new definition table needs no change here.
/// Reads run on the app role: RLS limits them to the current tenant (another tenant's row reads as "missing").
/// </summary>
internal static class RowVersion
{
    private static string TableOf<T>(AppDbContext db) where T : class
        => db.Model.FindEntityType(typeof(T))?.GetTableName()
           ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped to a table.");

    private static async Task<System.Data.Common.DbCommand> CommandAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = sql;
        return cmd;
    }

    /// <summary>Version of one row; <c>null</c> if the row does not exist or is outside the tenant.</summary>
    public static async Task<string?> ReadAsync<T>(AppDbContext db, Guid id, CancellationToken ct) where T : class
    {
        await using var cmd = await CommandAsync(db, $"SELECT xmin::text FROM \"{TableOf<T>(db)}\" WHERE \"Id\" = @id", ct);
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = id;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Versions of every visible row (list responses carry the version so a row can be edited directly).</summary>
    public static async Task<IReadOnlyDictionary<Guid, string>> ReadAllAsync<T>(
        IDbContextFactory<AppDbContext> factory, CancellationToken ct) where T : class
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var cmd = await CommandAsync(db, $"SELECT \"Id\", xmin::text FROM \"{TableOf<T>(db)}\"", ct);
        var result = new Dictionary<Guid, string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetGuid(0)] = reader.GetString(1);
        return result;
    }

    public static async Task<string?> ReadAsync<T>(IDbContextFactory<AppDbContext> factory, Guid id, CancellationToken ct)
        where T : class
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ReadAsync<T>(db, id, ct);
    }

    /// <summary>
    /// <see cref="UpdateAsync{T}(IDbContextFactory{AppDbContext}, Guid, string, Action{T}, CancellationToken)"/> with the
    /// natural-key unique violation (23505) turned into the entity's own <see cref="ValidationException"/> message
    /// (same text as the create path, so the endpoint maps it to the same field).
    /// </summary>
    public static async Task<bool> UpdateAsync<T>(
        IDbContextFactory<AppDbContext> factory, Guid id, string expectedVersion, Action<T> apply,
        Func<T, string> duplicateMessage, CancellationToken ct)
        where T : class
    {
        T? applied = null;
        try
        {
            return await UpdateAsync<T>(factory, id, expectedVersion, e => { apply(e); applied = e; }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException(applied is null ? "Kayıt zaten var." : duplicateMessage(applied));
        }
    }

    /// <summary>
    /// Lock + version comparison + <paramref name="apply"/> in ONE transaction. Different version →
    /// <see cref="EszamanliDegisiklikException"/> (409 <c>cakisma</c>), nothing is written. Missing row → <c>false</c>.
    /// </summary>
    public static Task<bool> UpdateAsync<T>(
        IDbContextFactory<AppDbContext> factory, Guid id, string expectedVersion, Action<T> apply, CancellationToken ct)
        where T : class
        => PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Table name from the EF model (not input); the id is a parameter.
            var lockSql = $"SELECT 1 FROM \"{TableOf<T>(db)}\" WHERE \"Id\" = {{0}} FOR UPDATE";
            await db.Database.ExecuteSqlRawAsync(lockSql, [id], ct);
            var current = await ReadAsync<T>(db, id, ct);
            if (current is null) return false;
            if (!string.Equals(current, expectedVersion.Trim(), StringComparison.Ordinal))
                throw new EszamanliDegisiklikException(EszamanliDegisiklikException.KayitMesaji);
            var entity = await db.Set<T>().FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id, ct);
            if (entity is null) return false;
            apply(entity);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
}
