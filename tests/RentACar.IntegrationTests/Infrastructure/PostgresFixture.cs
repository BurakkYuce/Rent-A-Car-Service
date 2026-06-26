using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests.Infrastructure;

/// <summary>
/// Test başına gerçek PostgreSQL: admin bağlantısıyla racar_owner/racar_app rollerini
/// (idempotent) ve çalıştırma başına BENZERSİZ bir DB oluşturur, migration'ı owner ile
/// uygular (RLS policy + grant'lar dahil), sonra app (racar_app — KISITLI) bağlantısını
/// sunar. İzolasyon testleri MUTLAKA racar_app ile bağlanmalıdır; aksi halde RLS bypass
/// olur ve test bir şey kanıtlamaz.
///
/// Docker Hub blob host'u bu ortamda egress politikasıyla engelli olduğundan Testcontainers
/// yerine ortamdaki PostgreSQL kullanılır: admin bağlantısı RACAR_TEST_PG_ADMIN env'inden
/// ya da yerel varsayılandan (postgres/postgres) gelir. CI'da postgres service container'ı
/// aynı env'i sağlar.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string OwnerPassword = "racar_owner_pw";
    private const string AppPassword = "racar_app_pw";

    private string _adminConn = default!;
    private string _dbName = default!;

    public string AppConnectionString { get; private set; } = default!;
    public string OwnerConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        _adminConn = Environment.GetEnvironmentVariable("RACAR_TEST_PG_ADMIN")
            ?? "Host=127.0.0.1;Port=5432;Username=postgres;Password=postgres;Database=postgres";

        var admin = new NpgsqlConnectionStringBuilder(_adminConn);

        await using (var conn = new NpgsqlConnection(_adminConn))
        {
            await conn.OpenAsync();
            await EnsureRoleAsync(conn, "racar_owner", OwnerPassword);
            await EnsureRoleAsync(conn, "racar_app", AppPassword);

            _dbName = "racar_test_" + Guid.NewGuid().ToString("N");
            await ExecAsync(conn, $"CREATE DATABASE \"{_dbName}\" OWNER racar_owner;");
        }

        OwnerConnectionString = Build(admin, "racar_owner", OwnerPassword, _dbName);
        AppConnectionString = Build(admin, "racar_app", AppPassword, _dbName);

        // racar_app yeni DB'ye bağlanabilsin.
        await using (var conn = new NpgsqlConnection(Build(admin, admin.Username!, admin.Password!, _dbName)))
        {
            await conn.OpenAsync();
            await ExecAsync(conn, "GRANT CONNECT ON DATABASE \"" + _dbName + "\" TO racar_app;");
        }

        // Migration'ı owner ile uygula (RLS + grant'lar bu migration'da).
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(OwnerConnectionString)
            .Options;
        await using var db = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        try
        {
            await using var conn = new NpgsqlConnection(_adminConn);
            await conn.OpenAsync();
            await ExecAsync(conn,
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_dbName}' AND pid <> pg_backend_pid();");
            await ExecAsync(conn, $"DROP DATABASE IF EXISTS \"{_dbName}\";");
        }
        catch
        {
            // best-effort temizlik
        }
    }

    private static async Task EnsureRoleAsync(NpgsqlConnection conn, string role, string password)
    {
        var sql =
            $"DO $$ BEGIN " +
            $"IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='{role}') THEN " +
            $"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS; " +
            $"END IF; END $$;";
        await ExecAsync(conn, sql);
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string Build(NpgsqlConnectionStringBuilder admin, string user, string password, string db)
        => new NpgsqlConnectionStringBuilder
        {
            Host = admin.Host,
            Port = admin.Port,
            Username = user,
            Password = password,
            Database = db
        }.ConnectionString;
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
