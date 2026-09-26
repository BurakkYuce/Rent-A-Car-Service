using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F9.1 — <see cref="IRowVersionStore"/>: same mechanism as <see cref="SatirSurumu"/> (Postgres <c>xmin</c> as an
/// opaque version + <c>FOR UPDATE</c> row lock) but generic over the entity type. The table name is taken from the EF
/// model of <typeparamref name="T"/> (never from input), so no whitelist is needed. Runs on the app role: RLS limits
/// every read/lock to the current tenant (another tenant's row reads as "missing").
/// </summary>
public sealed class RowVersionStore(IDbContextFactory<AppDbContext> factory) : IRowVersionStore
{
    private static string TableOf<T>(AppDbContext db) where T : class
        => db.Model.FindEntityType(typeof(T))?.GetTableName()
           ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped to a table.");

    private static async Task<string?> ReadAsync<T>(AppDbContext db, Guid id, CancellationToken ct) where T : class
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = $"SELECT xmin::text FROM \"{TableOf<T>(db)}\" WHERE \"Id\" = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = id;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task<string?> GetVersionAsync<T>(Guid id, CancellationToken ct = default) where T : class
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await ReadAsync<T>(db, id, ct);
    }

    public async Task<bool> UpdateAsync<T>(Guid id, string expectedVersion, Action<T> apply, string duplicateMessage,
        CancellationToken ct = default) where T : class
    {
        try
        {
            return await PgRetry.RunAsync(async () =>
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var lockSql = $"SELECT 1 FROM \"{TableOf<T>(db)}\" WHERE \"Id\" = {{0}} FOR UPDATE";
                await db.Database.ExecuteSqlRawAsync(lockSql, [id], ct);
                var current = await ReadAsync<T>(db, id, ct);
                if (current is null) return false;
                if (!string.Equals(current, expectedVersion.Trim(), StringComparison.Ordinal))
                    throw new ConcurrentModificationException(ConcurrentModificationException.RecordMessage);
                var entity = await db.Set<T>().FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id, ct);
                if (entity is null) return false;
                apply(entity);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ValidationException(duplicateMessage);
        }
    }
}
