using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.Jobs;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Jobs;

namespace RentACar.IntegrationTests;

/// <summary>
/// Arka plan işi — üretici yalıtımı ve koşu günlüğünün hata yolu (#265 takibi).
///
/// <para><b>Ne kilitleniyor:</b> (1) bir üretici bağlantıyı kıracak biçimde patlasa bile koşu
/// günlüğüne <c>Basarisiz</c> satırı DÜŞER (eskiden hata satırı taze, GUC'suz bağlantıda RLS'e —
/// 42501 — takılıp yutuluyordu); (2) patlayan üretici, SONRAKİ üreticileri ve WhatsApp özetini
/// düşürmez, onlar kendi başarı satırlarını yazar; (3) log hangi üreticinin hangi tenant için
/// patladığını söyler; (4) kaydedicinin kendi hatası sessiz değil, Warning'dir.</para>
///
/// <para>Gerçek yol: job'ın GERÇEK adım listesi (<see cref="DueNotificationJob.DbSteps"/>) alınır,
/// biri #265'in gerçek arıza biçimiyle (Npgsql'in bağlantıyı kıran +03:00 timestamptz parametresi)
/// patlayan adımla değiştirilir ve <see cref="DueNotificationJob.TenantKosAsync"/> koşulur. Bağlantı
/// <c>racar_app</c> (NOBYPASSRLS) — RLS açık.</para>
///
/// <para><b>BAĞIMSIZ ORACLE:</b> sahne elle kurulur — Kasko 5 gün sonra biter (vade: 1 bildirim),
/// Km 9.500 / bakım hedefi 10.000 (filo: 1 "Bakım-Km"), rezervasyon/kira yok (müşteri: 0), WhatsApp
/// özeti açık (1 gönderim). Beklenen sayılar bu sahneden yazılmış sabitlerdir.</para>
/// </summary>
[Collection("postgres")]
public sealed class JobUreticiYalitimTests(PostgresFixture fx)
{
    // 09:00Z = İstanbul 12:00 → WhatsApp özetinin 08:00 saat kapısı açık (dilim UTC'ye düşse de 09:00 ≥ 08:00).
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Order =
        [JobRunRecorder.DueNotification, JobRunRecorder.FleetNotification, JobRunRecorder.CustomerNotification];

    /// <summary>Elle kurulmuş sahneden beklenen üretim sayıları (sıra ile aynı).</summary>
    private static readonly int[] Expected = [1, 1, 0];

    private static readonly string[] ExpectedType = ["Kasko", "Bakım-Km", ""];

    private DbContextOptions<AppDbContext> AppOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;

    private sealed class WhatsAppFake : IWhatsAppService
    {
        public int Calls;
        public Task<bool> SendTemplateAsync(string phone, string templateName,
            IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
        { Calls++; return Task.FromResult(true); }
    }

    private sealed record LogKaydi(LogLevel Seviye, string Mesaj, Exception? Hata, IReadOnlyDictionary<string, object?> Alanlar);

    private sealed class LogCapturer : ILogger
    {
        public List<LogKaydi> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> kv
                ? kv.ToDictionary(k => k.Key, k => k.Value)
                : new Dictionary<string, object?>();
            lock (Records) Records.Add(new LogKaydi(logLevel, formatter(state, exception), exception, fields));
        }
    }

    /// <summary>
    /// #265'in GERÇEK arıza biçimi: +03:00 ofsetli timestamptz parametresi → Npgsql parametreyi
    /// yazarken patlar ve bağlantıyı kırar. Öncesinde izleyiciye "yarım iş" bırakır: paylaşılan bir
    /// context'te sonraki üreticinin SaveChanges'ı onu da yazmaya kalkardı.
    /// </summary>
    private static UreticiAdimi Failing(string name, Guid tenant) => new(name, async (db, ct) =>
    {
        db.Bildirimler.Add(new Bildirim
        {
            TenantId = tenant, Tur = "YARIM-IS", VehicleId = Guid.NewGuid(), VadeTarihi = Now, Mesaj = "kaydedilmemeli",
        });
        var withOffset = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.FromHours(3));
        return await db.Reservations.CountAsync(r => r.BasTar >= withOffset, ct);
    });

    private static async Task SceneAsync(IServiceProvider sp)
    {
        var vehicle = sp.GetRequiredService<VehicleService>();
        var dueVehicle = await vehicle.CreateAsync(new VehicleInput { Plaka = "34 YL 01", Durum = VehicleStatus.Musait });
        await sp.GetRequiredService<RegulationService>().AddInsuranceAsync(dueVehicle, InsuranceType.Kasko,
            Now.AddDays(-360), Now.AddDays(5), 1000m, "P-YL", "Sig", null);

        var maintenanceVehicle = await vehicle.CreateAsync(new VehicleInput { Plaka = "34 YL 02", Km = 9_500 });
        var service = sp.GetRequiredService<ServiceRecordService>();
        var sid = await service.CreateAsync(new ServiceRecordInput { VehicleId = maintenanceVehicle, GirisKm = 9_000 });
        Assert.True(await service.StartAsync(sid));
        Assert.True(await service.CompleteAsync(sid, 9_500, nextMaintenanceKm: 10_000));

        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        db.TenantSettings.Add(new TenantSettings { WhatsAppGunlukOzet = true, WhatsAppNumarasi = "0532 111 22 33" });
        await db.SaveChangesAsync();
    }

    private static DueNotificationJob Job(IServiceProvider sp, IWhatsAppService wa) => new(
        new ConfigurationBuilder().Build(), wa,
        sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IEmailSender>(),
        sp.GetRequiredService<ISmsService>(), TutSatEsikleri.Default, NullLogger<DueNotificationJob>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Patlayan_uretici_Basarisiz_satiri_yazar_sonrakiler_ve_ozet_yine_calisir(int failing)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SceneAsync(sp);

        var wa = new WhatsAppFake();
        var job = Job(sp, wa);
        var steps = job.DbSteps(tenant, Now).ToList();
        Assert.Equal(Order, steps.Select(a => a.Ad).ToArray()); // job'ın GERÇEK sırası
        steps[failing] = Failing(steps[failing].Ad, tenant);

        var log = new LogCapturer();
        var total = await DueNotificationJob.TenantKosAsync(AppOptions(), tenant, steps,
            job.SummaryStep(AppOptions(), tenant, Now), log, CancellationToken.None);

        // Sonraki (ve önceki) üreticiler koştu: dönen toplam yalnız patlayanın payı kadar eksik.
        Assert.Equal(Expected.Where((_, i) => i != failing).Sum(), total);

        // (a) + (b): koşu günlüğü — patlayan Basarisiz, diğerleri kendi başarı satırıyla.
        var logs = await sp.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(3, logs.Count);
        for (var i = 0; i < Order.Length; i++)
        {
            var row = Assert.Single(logs, l => l.JobAdi == Order[i]);
            if (i == failing)
            {
                Assert.False(row.Basarili);
                Assert.Null(row.SonucSayisi);
                Assert.Contains("Offset=03:00:00", row.Detay);
            }
            else
            {
                Assert.True(row.Basarili, $"{Order[i]}: {row.Detay}");
                Assert.Equal(Expected[i], row.SonucSayisi);
            }
        }

        // Diğer üreticilerin işi GERÇEKTEN yazıldı (RLS altında görünür) ve "yarım iş" hiçbir yere sızmadı.
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var types = await db.Bildirimler.AsNoTracking().Select(b => b.Tur).ToListAsync();
            var expectedTypes = ExpectedType.Where((t, i) => i != failing && t != "").Order().ToArray();
            Assert.Equal(expectedTypes, types.Order().ToArray());
        }

        // WhatsApp özeti de düşmedi.
        Assert.Equal(1, wa.Calls);

        // (3) Log: TEK hata, hangi üretici + hangi tenant; kaydedici kendi satırını yazabildi (Warning yok).
        var error = Assert.Single(log.Records, k => k.Seviye >= LogLevel.Error);
        Assert.Equal(Order[failing], error.Alanlar["Uretici"]);
        Assert.Equal(tenant, error.Alanlar["Tenant"]);
        Assert.IsType<ArgumentException>(error.Hata);
        Assert.DoesNotContain(log.Records, k => k.Seviye == LogLevel.Warning);
    }

    [Fact]
    public async Task WhatsApp_ozeti_patlarsa_loglanir_ve_uretici_sonuclari_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SceneAsync(sp);

        var job = Job(sp, new WhatsAppFake());
        var log = new LogCapturer();
        var total = await DueNotificationJob.TenantKosAsync(AppOptions(), tenant, job.DbSteps(tenant, Now),
            _ => throw new HttpRequestException("WhatsApp sağlayıcısı yanıt vermedi"), log, CancellationToken.None);

        Assert.Equal(Expected.Sum(), total);
        var logs = await sp.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, l => Assert.True(l.Basarili, $"{l.JobAdi}: {l.Detay}"));

        var error = Assert.Single(log.Records, k => k.Seviye >= LogLevel.Error);
        Assert.Equal(JobRunRecorder.WhatsAppSummary, error.Alanlar["Uretici"]);
        Assert.Equal(tenant, error.Alanlar["Tenant"]);
    }

    /// <summary>
    /// Kaydedicinin kendisi: üretici PAYLAŞILAN bir context'te bağlantıyı kırsa bile hata satırı yazılır.
    /// Eski kodda satır aynı (kırılmış → EF'in GUC'suz yeniden açtığı) bağlantıda yazılıyor, RLS 42501
    /// ile reddediliyor ve yutuluyordu → günlükte SIFIR satır.
    /// </summary>
    [Fact]
    public async Task Kaydedici_kirilan_baglantida_da_Basarisiz_satirini_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);

        var sys = new SystemTenantContext { TenantId = tenant };
        await using (var db = new AppDbContext(AppOptions(), sys, sys))
        {
            await TenantGuc.OpenAsync(db, tenant);
            var withOffset = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.FromHours(3));
            await Assert.ThrowsAsync<ArgumentException>(() => JobRunRecorder.RunAsync(db, tenant,
                "kirik-baglanti", () => db.Reservations.CountAsync(r => r.BasTar >= withOffset)));
        }

        var row = Assert.Single(await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
        Assert.Equal("kirik-baglanti", row.JobAdi);
        Assert.False(row.Basarili);
        Assert.Contains("Offset=03:00:00", row.Detay);
    }

    [Fact]
    public async Task Kaydedicinin_kendi_hatasi_isi_dusurmez_ama_Warning_loglanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var log = new LogCapturer();

        var sys = new SystemTenantContext { TenantId = tenant };
        int result;
        await using (var db = new AppDbContext(AppOptions(), sys, sys))
        {
            await TenantGuc.OpenAsync(db, tenant);
            // JobAdi kolonu varchar(64) → 100 karakterlik ad INSERT'te reddedilir (22001).
            result = await JobRunRecorder.RunAsync(db, tenant, new string('x', 100),
                () => Task.FromResult(7), n => n, log: log);
        }

        Assert.Equal(7, result); // işin sonucu aynen döndü, günlük hatası işi düşürmedi
        var warning = Assert.Single(log.Records, k => k.Seviye == LogLevel.Warning);
        Assert.NotNull(warning.Hata);
        Assert.Equal(tenant, warning.Alanlar["Tenant"]);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
    }
}
