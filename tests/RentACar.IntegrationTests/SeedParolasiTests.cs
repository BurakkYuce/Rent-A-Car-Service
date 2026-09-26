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
    private const string SeedLine = "Seed kullanıcı parolası: ";

    // ---- saf karar ----

    [Fact]
    public void Yapilandirilmis_parola_her_ortamda_kazanir_ve_uretilmis_sayilmaz()
    {
        Assert.Equal(new DbInitializer.SeedParolaKarari("verilen-parola", false),
            DbInitializer.ResolveSeedPassword("verilen-parola", development: true));
        Assert.Equal(new DbInitializer.SeedParolaKarari("verilen-parola", false),
            DbInitializer.ResolveSeedPassword("verilen-parola", development: false));
    }

    [Fact]
    public void Development_disinda_yapilandirma_yoksa_seed_parolasi_yok()
    {
        Assert.Null(DbInitializer.ResolveSeedPassword(null, development: false).Parola);
        Assert.Null(DbInitializer.ResolveSeedPassword("   ", development: false).Parola);
    }

    [Fact]
    public void Development_ta_yapilandirma_yoksa_her_seferinde_farkli_guclu_parola_uretilir()
    {
        var a = DbInitializer.ResolveSeedPassword(null, development: true);
        var b = DbInitializer.ResolveSeedPassword("", development: true);
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
            var log1 = new LogCollector();
            await SeedAsync(pg, "Development", seedPassword: null, log1);

            var row = Assert.Single(log1.Records, k => k.Mesaj.StartsWith(SeedLine, StringComparison.Ordinal));
            Assert.Equal(LogLevel.Warning, row.Seviye);
            var password = row.Mesaj[SeedLine.Length..].Split(' ')[0];
            Assert.True(password.Length >= 20, $"üretilen parola beklenenden kısa: {password.Length}");

            var once = await UsersAsync(pg);
            Assert.Equal(3, once.Count);
            foreach (var (company, user) in SeedIdentities)
                Assert.True(await LoginAsync(pg, company, user, password), $"{company}/{user}: loglanan parola girişi açmıyor");

            // İkinci açılış — bu kez parola YAPILANDIRILMIŞ olsa bile mevcut kullanıcılara dokunulmaz,
            // yeni parola da üretilip basılmaz.
            var log2 = new LogCollector();
            await SeedAsync(pg, "Development", seedPassword: "baska-bir-parola-123", log2);

            var after = await UsersAsync(pg);
            Assert.Equal(once.Select(u => (u.Id, u.PasswordHash)).OrderBy(x => x.Id),
                         after.Select(u => (u.Id, u.PasswordHash)).OrderBy(x => x.Id));
            Assert.DoesNotContain(log2.Records, k => k.Mesaj.Contains(SeedLine, StringComparison.Ordinal));
            Assert.False(await LoginAsync(pg, "yucerent", "umit", "baska-bir-parola-123"));
            Assert.True(await LoginAsync(pg, "yucerent", "umit", password));
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
            var log1 = new LogCollector();
            await SeedAsync(pg, "Production", seedPassword: null, log1);

            Assert.Empty(await UsersAsync(pg));
            await using (var db = OwnerDb(pg))
                Assert.False(await db.Tenants.AnyAsync(t => t.Code == "yucerent" || t.Code == "demo"));
            Assert.Contains(log1.Records, k => k.Seviye == LogLevel.Warning && k.Mesaj.StartsWith("Seed atlandı", StringComparison.Ordinal));
            Assert.DoesNotContain(log1.Records, k => k.Mesaj.Contains(SeedLine, StringComparison.Ordinal));

            // Operatör parolayı açıkça verdi → seed o parolayla koşar; parola hiçbir log satırına girmez.
            const string given = "UretimdeVerilen-Parola-7Q";
            var log2 = new LogCollector();
            await SeedAsync(pg, "Production", given, log2);

            Assert.Equal(3, (await UsersAsync(pg)).Count);
            foreach (var (company, user) in SeedIdentities)
                Assert.True(await LoginAsync(pg, company, user, given), $"{company}/{user}: verilen parola girişi açmıyor");
            Assert.DoesNotContain(log2.Records, k => k.Mesaj.Contains(given, StringComparison.Ordinal));
        }
        finally { await pg.DisposeAsync(); }
    }

    private static async Task SeedAsync(PostgresFixture pg, string environment, string? seedPassword, LogCollector log)
    {
        var setting = new Dictionary<string, string?>();
        if (seedPassword is not null) setting[DbInitializer.SeedPasswordKey] = seedPassword;

        using var host = new TestHost(pg.AppConnectionString, s =>
        {
            s.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(setting).Build());
            s.AddSingleton<IHostEnvironment>(new SahteOrtam(environment));
            s.AddSingleton<ILoggerProvider>(log);
        });
        using var scope = host.ScopeFor(null);
        await DbInitializer.MigrateAndSeedAsync(scope.ServiceProvider, pg.OwnerConnectionString);
    }

    private static readonly (string Firma, string Kullanici)[] SeedIdentities =
        [("yucerent", "umit"), ("yucerent", "operator"), ("demo", "umit")];

    /// <summary>Gerçek giriş doğrulaması (Web + API'nin ortak <see cref="LoginService"/>'i, racar_app bağlantısı).</summary>
    private static async Task<bool> LoginAsync(PostgresFixture pg, string company, string user, string password)
    {
        using var host = new TestHost(pg.AppConnectionString);
        using var scope = host.ScopeFor(null);
        return await scope.ServiceProvider.GetRequiredService<LoginService>().ValidateAsync(company, user, password) is not null;
    }

    private static async Task<List<User>> UsersAsync(PostgresFixture pg)
    {
        await using var db = OwnerDb(pg);
        var seedCompanies = await db.Tenants.Where(t => t.Code == "yucerent" || t.Code == "demo").Select(t => t.Id).ToListAsync();
        return await db.Users.AsNoTracking().Where(u => seedCompanies.Contains(u.TenantId)).ToListAsync();
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

    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<LogKaydi> Records { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(Records);
        public void Dispose() { }

        private sealed class Recorder(ConcurrentQueue<LogKaydi> target) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => target.Enqueue(new LogKaydi(logLevel, formatter(state, exception)));
        }
    }
}
