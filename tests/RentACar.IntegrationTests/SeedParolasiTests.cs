using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// Seed parolası repoda değil yapılandırmada (<c>Seed:Parola</c>). Kurallar:
/// yapılandırılmış parola her ortamda kazanır ve ASLA loglanmaz; yoksa Development'ta ilk kurulumda
/// rastgele üretilip WARNING olarak BİR KEZ basılır; Development dışında yoksa demo firma/kullanıcı
/// oluşturulmaz. Seed yalnız boş DB'de koşar → mevcut kullanıcıların parola özeti hiçbir açılışta değişmez.
/// Her senaryo kendi boş DB'sini kurar (paylaşımlı DB'de tenant zaten var → seed hiç koşmazdı).
/// </summary>
[Collection("postgres")]
public sealed class SeedParolasiTests
{
    private const string SeedSatiri = "Seed kullanıcı parolası: ";

    // ---- saf karar ----

    [Fact]
    public void Yapilandirilmis_parola_her_ortamda_kazanir_ve_uretilmis_sayilmaz()
    {
        Assert.Equal(new DbInitializer.SeedParolaKarari("verilen-parola", false),
            DbInitializer.SeedParolasiCoz("verilen-parola", gelistirme: true));
        Assert.Equal(new DbInitializer.SeedParolaKarari("verilen-parola", false),
            DbInitializer.SeedParolasiCoz("verilen-parola", gelistirme: false));
    }

    [Fact]
    public void Development_disinda_yapilandirma_yoksa_seed_parolasi_yok()
    {
        Assert.Null(DbInitializer.SeedParolasiCoz(null, gelistirme: false).Parola);
        Assert.Null(DbInitializer.SeedParolasiCoz("   ", gelistirme: false).Parola);
    }

    [Fact]
    public void Development_ta_yapilandirma_yoksa_her_seferinde_farkli_guclu_parola_uretilir()
    {
        var a = DbInitializer.SeedParolasiCoz(null, gelistirme: true);
        var b = DbInitializer.SeedParolasiCoz("", gelistirme: true);
        Assert.True(a.Uretildi);
        Assert.True(b.Uretildi);
        Assert.True(a.Parola!.Length >= 20);
        Assert.NotEqual(a.Parola, b.Parola);
    }

    // ---- gerçek açılış yolu (DbInitializer.MigrateAndSeedAsync, boş DB) ----

    [Fact]
    public async Task Development_uretilen_parola_loglanir_girisi_acar_ve_sonraki_acilis_parolaya_dokunmaz()
    {
        var pg = new PostgresFixture();
        await pg.InitializeAsync();
        try
        {
            var log1 = new LogYakalayici();
            await SeedAsync(pg, "Development", seedParola: null, log1);

            var satir = Assert.Single(log1.Kayitlar, k => k.Mesaj.StartsWith(SeedSatiri, StringComparison.Ordinal));
            Assert.Equal(LogLevel.Warning, satir.Seviye);
            var parola = satir.Mesaj[SeedSatiri.Length..].Split(' ')[0];
            Assert.True(parola.Length >= 20, $"üretilen parola beklenenden kısa: {parola.Length}");

            var once = await KullanicilarAsync(pg);
            Assert.Equal(3, once.Count);
            foreach (var (firma, kullanici) in SeedKimlikleri)
                Assert.True(await GirisAsync(pg, firma, kullanici, parola), $"{firma}/{kullanici}: loglanan parola girişi açmıyor");

            // İkinci açılış — bu kez parola YAPILANDIRILMIŞ olsa bile mevcut kullanıcılara dokunulmaz,
            // yeni parola da üretilip basılmaz.
            var log2 = new LogYakalayici();
            await SeedAsync(pg, "Development", seedParola: "baska-bir-parola-123", log2);

            var sonra = await KullanicilarAsync(pg);
            Assert.Equal(once.Select(u => (u.Id, u.PasswordHash)).OrderBy(x => x.Id),
                         sonra.Select(u => (u.Id, u.PasswordHash)).OrderBy(x => x.Id));
            Assert.DoesNotContain(log2.Kayitlar, k => k.Mesaj.Contains(SeedSatiri, StringComparison.Ordinal));
            Assert.False(await GirisAsync(pg, "yucerent", "umit", "baska-bir-parola-123"));
            Assert.True(await GirisAsync(pg, "yucerent", "umit", parola));
        }
        finally { await pg.DisposeAsync(); }
    }

    [Fact]
    public async Task Development_disinda_yapilandirma_yoksa_demo_kullanici_olusmaz_verilirse_o_kullanilir_ve_loglanmaz()
    {
        var pg = new PostgresFixture();
        await pg.InitializeAsync();
        try
        {
            var log1 = new LogYakalayici();
            await SeedAsync(pg, "Production", seedParola: null, log1);

            Assert.Empty(await KullanicilarAsync(pg));
            await using (var db = OwnerDb(pg))
                Assert.False(await db.Tenants.AnyAsync(t => t.Code == "yucerent" || t.Code == "demo"));
            Assert.Contains(log1.Kayitlar, k => k.Seviye == LogLevel.Warning && k.Mesaj.StartsWith("Seed atlandı", StringComparison.Ordinal));
            Assert.DoesNotContain(log1.Kayitlar, k => k.Mesaj.Contains(SeedSatiri, StringComparison.Ordinal));

            // Operatör parolayı açıkça verdi → seed o parolayla koşar; parola hiçbir log satırına girmez.
            const string verilen = "UretimdeVerilen-Parola-7Q";
            var log2 = new LogYakalayici();
            await SeedAsync(pg, "Production", verilen, log2);

            Assert.Equal(3, (await KullanicilarAsync(pg)).Count);
            foreach (var (firma, kullanici) in SeedKimlikleri)
                Assert.True(await GirisAsync(pg, firma, kullanici, verilen), $"{firma}/{kullanici}: verilen parola girişi açmıyor");
            Assert.DoesNotContain(log2.Kayitlar, k => k.Mesaj.Contains(verilen, StringComparison.Ordinal));
        }
        finally { await pg.DisposeAsync(); }
    }

    private static async Task SeedAsync(PostgresFixture pg, string ortam, string? seedParola, LogYakalayici log)
    {
        var ayar = new Dictionary<string, string?>();
        if (seedParola is not null) ayar[DbInitializer.SeedParolaAnahtari] = seedParola;

        using var host = new TestHost(pg.AppConnectionString, s =>
        {
            s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(ayar).Build());
            s.AddSingleton<IHostEnvironment>(new SahteOrtam(ortam));
            s.AddSingleton<ILoggerProvider>(log);
        });
        using var scope = host.ScopeFor(null);
        await DbInitializer.MigrateAndSeedAsync(scope.ServiceProvider, pg.OwnerConnectionString);
    }

    private static readonly (string Firma, string Kullanici)[] SeedKimlikleri =
        [("yucerent", "umit"), ("yucerent", "operator"), ("demo", "umit")];

    /// <summary>Gerçek giriş doğrulaması (Web + API'nin ortak <see cref="LoginService"/>'i, racar_app bağlantısı).</summary>
    private static async Task<bool> GirisAsync(PostgresFixture pg, string firma, string kullanici, string parola)
    {
        using var host = new TestHost(pg.AppConnectionString);
        using var scope = host.ScopeFor(null);
        return await scope.ServiceProvider.GetRequiredService<LoginService>().ValidateAsync(firma, kullanici, parola) is not null;
    }

    private static async Task<List<User>> KullanicilarAsync(PostgresFixture pg)
    {
        await using var db = OwnerDb(pg);
        var seedFirmalari = await db.Tenants.Where(t => t.Code == "yucerent" || t.Code == "demo").Select(t => t.Id).ToListAsync();
        return await db.Users.AsNoTracking().Where(u => seedFirmalari.Contains(u.TenantId)).ToListAsync();
    }

    private static AppDbContext OwnerDb(PostgresFixture pg)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(pg.OwnerConnectionString).Options,
               NullTenantContext.Instance, NullCurrentUser.Instance);

    private sealed class SahteOrtam(string ad) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = ad;
        public string ApplicationName { get; set; } = "RentACar.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record LogKaydi(LogLevel Seviye, string Mesaj);

    private sealed class LogYakalayici : ILoggerProvider
    {
        public ConcurrentQueue<LogKaydi> Kayitlar { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Kaydedici(Kayitlar);
        public void Dispose() { }

        private sealed class Kaydedici(ConcurrentQueue<LogKaydi> hedef) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => hedef.Enqueue(new LogKaydi(logLevel, formatter(state, exception)));
        }
    }
}
