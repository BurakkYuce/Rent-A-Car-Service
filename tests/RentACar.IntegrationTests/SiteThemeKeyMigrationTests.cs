using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Migrations;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// <c>RenameSiteThemeKeys</c> veri migration'ı GERÇEK DB'de: kayıtlı <c>Ayarlar.SiteTemasi</c> değerleri
/// (<c>tarife</c> → <c>pazar</c>, <c>kontuar</c> → <c>rota</c>) çevrilir; <c>vitrin</c> ve null dokunulmaz; RLS FORCE
/// migration sonunda geri gelir; Down eski anahtarlara döner.
///
/// <para>Migration'ın SQL işlemleri, EF migrator'ı gibi owner (racar_owner — NOBYPASSRLS) bağlantısında TEK
/// transaction'da koşulur. FORCE RLS altında NO FORCE parantezi olmasaydı UPDATE sıfır satır görürdü — bu test o
/// sessiz no-op'u yakalar. Değerler racar_app ile, her firmanın kendi <c>app.tenant_id</c> bağlamında okunur.
/// Paylaşılan test DB'si en sonda Up'a geri getirilir.</para>
///
/// <para>BAĞIMSIZ ORACLE: başlangıç ve beklenen değerler testte elle yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class SiteThemeKeyMigrationTests(PostgresFixture fx)
{
    [Fact]
    public async Task Kayitli_tema_anahtarlari_cevrilir_RLS_FORCE_geri_gelir_Down_geri_doner()
    {
        // Başlangıç: eski anahtarlar (biri büyük harf + boşluklu, elle yazılmış eski satır gibi), dokunulmayacak
        // vitrin ve null.
        var seeds = new (string? Raw, string? Up, string? Down)[]
        {
            ("tarife", "pazar", "tarife"),
            (" Kontuar ", "rota", "kontuar"),
            ("vitrin", "vitrin", "vitrin"),
            (null, null, null),
        };
        var tenants = new List<Guid>();
        foreach (var (raw, _, _) in seeds)
        {
            var t = await CreateTenantWithSettingsAsync();
            await WriteRawAsync(t, raw);
            tenants.Add(t);
        }

        var migration = new RenameSiteThemeKeys();
        try
        {
            await RunAsync(migration.UpOperations);
            for (var i = 0; i < seeds.Length; i++)
                Assert.Equal(seeds[i].Up, await ReadAsync(tenants[i]));
            await AssertForcedAsync();

            // İdempotent: ikinci kez koşmak bir şey değiştirmez.
            await RunAsync(migration.UpOperations);
            for (var i = 0; i < seeds.Length; i++)
                Assert.Equal(seeds[i].Up, await ReadAsync(tenants[i]));

            await RunAsync(migration.DownOperations);
            for (var i = 0; i < seeds.Length; i++)
                Assert.Equal(seeds[i].Down, await ReadAsync(tenants[i]));
            await AssertForcedAsync();
        }
        finally
        {
            // Paylaşılan DB'yi migrate edilmiş hâline geri getir (diğer testler yeni anahtarları bekler).
            await RunAsync(migration.UpOperations);
        }
        for (var i = 0; i < seeds.Length; i++)
            Assert.Equal(seeds[i].Up, await ReadAsync(tenants[i]));
    }

    /// <summary>Migration'ın SQL işlemleri owner bağlantısında tek transaction'da (EF migrator'ı gibi).</summary>
    private async Task RunAsync(IReadOnlyList<MigrationOperation> operations)
    {
        var sql = operations.Select(o => Assert.IsType<SqlOperation>(o).Sql).ToList();
        Assert.NotEmpty(sql);
        await using var conn = new NpgsqlConnection(fx.OwnerConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        foreach (var statement in sql)
        {
            await using var cmd = new NpgsqlCommand(statement, conn, tx);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private async Task AssertForcedAsync()
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select relrowsecurity, relforcerowsecurity from pg_class where relname = 'Ayarlar'", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.True(r.GetBoolean(0), "Ayarlar: ENABLE ROW LEVEL SECURITY yok");
        Assert.True(r.GetBoolean(1), "Ayarlar: FORCE ROW LEVEL SECURITY geri gelmedi");
    }

    private async Task<Guid> CreateTenantWithSettingsAsync()
    {
        await using (var db = new AppDbContext(
                         new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
                         NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var t = new Tenant { Code = "mg" + Guid.NewGuid().ToString("N")[..10], Name = "Tema Göç", IsActive = true };
            db.Tenants.Add(t);
            await db.SaveChangesAsync();

            // Ayar satırı tembel oluşur ve RLS'li: firmanın kendi bağlamında gerçek servisle yaratılır.
            using var host = new TestHost(fx.AppConnectionString);
            using var s = host.ScopeFor(t.Id);
            var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            await svc.SaveAsync(await svc.GetAsync());
            return t.Id;
        }
    }

    /// <summary>Servisi atlayarak ham değer yazar (servis eski anahtarı normalize ederdi) — racar_app + firma bağlamı.</summary>
    private async Task WriteRawAsync(Guid tenant, string? value)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, tenant);
        await using var cmd = new NpgsqlCommand("update \"Ayarlar\" set \"SiteTemasi\" = @v where \"TenantId\" = @t", conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter("v", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)value ?? DBNull.Value });
        cmd.Parameters.AddWithValue("t", tenant);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        await tx.CommitAsync();
    }

    private async Task<string?> ReadAsync(Guid tenant)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await SetTenantAsync(conn, tx, tenant);
        await using var cmd = new NpgsqlCommand("select \"SiteTemasi\" from \"Ayarlar\" where \"TenantId\" = @t", conn, tx);
        cmd.Parameters.AddWithValue("t", tenant);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), "ayar satırı görünmüyor");
        return r.IsDBNull(0) ? null : r.GetString(0);
    }

    private static async Task SetTenantAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid tenant)
    {
        await using var cmd = new NpgsqlCommand("select set_config('app.tenant_id', @t, true)", conn, tx);
        cmd.Parameters.AddWithValue("t", tenant.ToString());
        await cmd.ExecuteScalarAsync();
    }
}
