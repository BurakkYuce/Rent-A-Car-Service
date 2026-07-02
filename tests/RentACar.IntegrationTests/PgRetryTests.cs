using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// P0-5 deadlock retry — BAĞIMSIZ ORACLE: hata kodları GERÇEK Postgres'ten üretilir
/// (RAISE ... ERRCODE / gerçek çapraz-kilit deadlock'u), yapay exception kurulmaz.
/// 40P01/40001 → en fazla 3 deneme; kalıcı hatalar (23505 vb.) retry EDİLMEZ.
/// </summary>
[Collection("postgres")]
public sealed class PgRetryTests(PostgresFixture fx)
{
    /// <summary>Gerçek PostgresException fırlatır (istenen SQLSTATE ile) — DB'den, elle kurulmaz.</summary>
    private async Task RaiseAsync(string errcode)
    {
        await using var c = new NpgsqlConnection(fx.AppConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"DO $$ BEGIN RAISE EXCEPTION 'probe' USING ERRCODE = '{errcode}'; END $$;", c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Deadlock_kodu_iki_kez_gelse_de_ucuncu_deneme_kurtarir()
    {
        var attempts = 0;
        await PgRetry.RunAsync(async () =>
        {
            attempts++;
            if (attempts <= 2) await RaiseAsync("40P01"); // ilk 2 deneme: gerçek deadlock kodu
        });
        Assert.Equal(3, attempts); // 2 başarısız + 1 başarılı
    }

    [Fact]
    public async Task Serialization_failure_retry_edilir()
    {
        var attempts = 0;
        var result = await PgRetry.RunAsync(async () =>
        {
            attempts++;
            if (attempts == 1) await RaiseAsync("40001");
            return 42; // generic overload
        });
        Assert.Equal(2, attempts);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Kalici_hata_retry_edilmez()
    {
        var attempts = 0;
        var ex = await Assert.ThrowsAsync<PostgresException>(() => PgRetry.RunAsync(async () =>
        {
            attempts++;
            await RaiseAsync("23505"); // unique_violation: iş kuralı hatası, tekrar denemek anlamsız
        }));
        Assert.Equal(1, attempts); // hiç retry yok
        Assert.Equal("23505", ex.SqlState);
    }

    [Fact]
    public async Task Uc_denemede_gecmezse_hata_yayilir()
    {
        var attempts = 0;
        var ex = await Assert.ThrowsAsync<PostgresException>(() => PgRetry.RunAsync(async () =>
        {
            attempts++;
            await RaiseAsync("40P01"); // hep deadlock → tükenince dürüstçe fırlat
        }));
        Assert.Equal(3, attempts);
        Assert.Equal("40P01", ex.SqlState);
    }

    [Fact]
    public void Sarmalayici_zincirin_derinligi_ne_olursa_olsun_taninir()
    {
        // EF execution strategy GERÇEK zinciri (adversarial H-1'de canlı PG'de kanıtlandı):
        // InvalidOperationException("transient failure") → DbUpdateException → PostgresException(40P01).
        var pg = new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");
        Assert.True(PgRetry.IsTransientConflict(
            new InvalidOperationException("transient failure", new DbUpdateException("save fail", pg))));
        Assert.True(PgRetry.IsTransientConflict(new DbUpdateException("save fail", pg)));
        Assert.True(PgRetry.IsTransientConflict(pg)); // commit/raw yolu: doğrudan
        Assert.False(PgRetry.IsTransientConflict(new InvalidOperationException("başka hata")));
        // İlk bulunan PostgresException iş-kuralı koduysa retry YOK (sarmalansa bile):
        var uniq = new PostgresException("duplicate", "ERROR", "ERROR", "23505");
        Assert.False(PgRetry.IsTransientConflict(
            new InvalidOperationException("transient failure", new DbUpdateException("save fail", uniq))));
    }

    [Fact]
    public async Task Ef_SaveChanges_uzerinden_gercek_deadlock_retry_ile_kurtarilir()
    {
        // Adversarial H-1 regresyon testi: 40P01, EF pipeline'ının KENDİ sarmalayıcısıyla gelmeli
        // (raw komut değil) — iki AppDbContext, iki araç, ÇAPRAZ güncelleme sırası, explicit tx.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vs = sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>();
        var v1 = await vs.CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 DL 01", Durum = RentACar.Domain.Enums.VehicleStatus.Musait });
        var v2 = await vs.CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 DL 02", Durum = RentACar.Domain.Enums.VehicleStatus.Musait });
        var factory = sp.GetRequiredService<IDbContextFactory<RentACar.Infrastructure.Persistence.AppDbContext>>();

        async Task Work(Guid first, Guid second, string marker) => await PgRetry.RunAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            var f = await db.Vehicles.FirstAsync(x => x.Id == first);
            f.LastikDurumu = (f.LastikDurumu ?? "") + marker; // APPEND: rollback izi bırakmaz, commit izi kalıcı
            await db.SaveChangesAsync();      // ilk satır kilidi
            await Task.Delay(400);            // iki taraf da ilk kilidini alsın
            var s = await db.Vehicles.FirstAsync(x => x.Id == second);
            s.LastikDurumu = (s.LastikDurumu ?? "") + marker;
            await db.SaveChangesAsync();      // çapraz bekleme → kurban 40P01 (EF sarmalı) alır
            await tx.CommitAsync();
        });

        await Task.WhenAll(Work(v1, v2, "A"), Work(v2, v1, "B")); // retry olmasa kurban patlar

        // İki iş de commit ettiyse HER araç HER iki işaretçiyi tam BİRER kez taşır
        // (kurbanın ilk denemesindeki append rollback ile silinmiş olmalı — çift "A"/"B" = sızıntı).
        await using var check = await factory.CreateDbContextAsync();
        var izler = await check.Vehicles.Where(x => x.Id == v1 || x.Id == v2)
            .Select(x => x.LastikDurumu).ToListAsync();
        Assert.Equal(2, izler.Count);
        Assert.All(izler, iz =>
        {
            Assert.NotNull(iz);
            Assert.Equal(1, iz!.Count(c => c == 'A'));
            Assert.Equal(1, iz.Count(c => c == 'B'));
        });
    }

    [Fact]
    public async Task Gercek_capraz_kilit_deadlocku_retry_ile_kurtarilir()
    {
        // İki bağlantı, iki satır, ÇAPRAZ kilit sırası → Postgres birini 40P01 ile öldürür;
        // PgRetry kurbanı baştan koşar → İKİ iş de tamamlanır. (deadlock_timeout ~1sn)
        await using var admin = new NpgsqlConnection(fx.OwnerConnectionString);
        await admin.OpenAsync();
        await using (var setup = new NpgsqlCommand(
            "DROP TABLE IF EXISTS deadlock_probe; CREATE TABLE deadlock_probe(id int PRIMARY KEY, v int NOT NULL); " +
            "INSERT INTO deadlock_probe VALUES (1,0),(2,0);", admin))
            await setup.ExecuteNonQueryAsync();

        async Task Work(int first, int second) => await PgRetry.RunAsync(async () =>
        {
            await using var c = new NpgsqlConnection(fx.OwnerConnectionString);
            await c.OpenAsync();
            await using var tx = await c.BeginTransactionAsync();
            await using (var u1 = new NpgsqlCommand($"UPDATE deadlock_probe SET v=v+1 WHERE id={first}", c, tx))
                await u1.ExecuteNonQueryAsync();
            await Task.Delay(400); // iki taraf da ilk kilidini alsın → çapraz bekleme garantili
            await using (var u2 = new NpgsqlCommand($"UPDATE deadlock_probe SET v=v+1 WHERE id={second}", c, tx))
                await u2.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        });

        await Task.WhenAll(Work(1, 2), Work(2, 1)); // biri kurban olur, retry kurtarır

        await using var check = new NpgsqlCommand("SELECT SUM(v)::int FROM deadlock_probe", admin);
        Assert.Equal(4, (int)(await check.ExecuteScalarAsync())!); // her iş her satırı 1 artırdı: 2×2=4
    }
}
