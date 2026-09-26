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
/// <para>Gerçek yol: job'ın GERÇEK adım listesi (<see cref="VadeBildirimJob.DbAdimlari"/>) alınır,
/// biri #265'in gerçek arıza biçimiyle (Npgsql'in bağlantıyı kıran +03:00 timestamptz parametresi)
/// patlayan adımla değiştirilir ve <see cref="VadeBildirimJob.TenantKosAsync"/> koşulur. Bağlantı
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

    private static readonly string[] Sira =
        [JobRunRecorder.DueNotification, JobRunRecorder.FleetNotification, JobRunRecorder.CustomerNotification];

    /// <summary>Elle kurulmuş sahneden beklenen üretim sayıları (sıra ile aynı).</summary>
    private static readonly int[] Beklenen = [1, 1, 0];

    private static readonly string[] BeklenenTur = ["Kasko", "Bakım-Km", ""];

    private DbContextOptions<AppDbContext> AppOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;

    private sealed class SahteWhatsApp : IWhatsAppService
    {
        public int Cagri;
        public Task<bool> SendTemplateAsync(string phone, string templateName,
            IReadOnlyDictionary<string, string> parameters, CancellationToken ct = default)
        { Cagri++; return Task.FromResult(true); }
    }

    private sealed record LogKaydi(LogLevel Seviye, string Mesaj, Exception? Hata, IReadOnlyDictionary<string, object?> Alanlar);

    private sealed class LogYakalayici : ILogger
    {
        public List<LogKaydi> Kayitlar { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var alanlar = state is IEnumerable<KeyValuePair<string, object?>> kv
                ? kv.ToDictionary(k => k.Key, k => k.Value)
                : new Dictionary<string, object?>();
            lock (Kayitlar) Kayitlar.Add(new LogKaydi(logLevel, formatter(state, exception), exception, alanlar));
        }
    }

    /// <summary>
    /// #265'in GERÇEK arıza biçimi: +03:00 ofsetli timestamptz parametresi → Npgsql parametreyi
    /// yazarken patlar ve bağlantıyı kırar. Öncesinde izleyiciye "yarım iş" bırakır: paylaşılan bir
    /// context'te sonraki üreticinin SaveChanges'ı onu da yazmaya kalkardı.
    /// </summary>
    private static UreticiAdimi Patlayan(string ad, Guid tenant) => new(ad, async (db, ct) =>
    {
        db.Bildirimler.Add(new Bildirim
        {
            TenantId = tenant, Tur = "YARIM-IS", VehicleId = Guid.NewGuid(), VadeTarihi = Now, Mesaj = "kaydedilmemeli",
        });
        var ofsetli = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.FromHours(3));
        return await db.Reservations.CountAsync(r => r.BasTar >= ofsetli, ct);
    });

    private static async Task SahneAsync(IServiceProvider sp)
    {
        var arac = sp.GetRequiredService<VehicleService>();
        var vadeArac = await arac.CreateAsync(new VehicleInput { Plaka = "34 YL 01", Durum = VehicleStatus.Musait });
        await sp.GetRequiredService<RegulationService>().AddInsuranceAsync(vadeArac, InsuranceType.Kasko,
            Now.AddDays(-360), Now.AddDays(5), 1000m, "P-YL", "Sig", null);

        var bakimArac = await arac.CreateAsync(new VehicleInput { Plaka = "34 YL 02", Km = 9_500 });
        var servis = sp.GetRequiredService<ServiceRecordService>();
        var sid = await servis.CreateAsync(new ServiceRecordInput { VehicleId = bakimArac, GirisKm = 9_000 });
        Assert.True(await servis.StartAsync(sid));
        Assert.True(await servis.CompleteAsync(sid, 9_500, nextMaintenanceKm: 10_000));

        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        db.TenantSettings.Add(new TenantSettings { WhatsAppGunlukOzet = true, WhatsAppNumarasi = "0532 111 22 33" });
        await db.SaveChangesAsync();
    }

    private static VadeBildirimJob Job(IServiceProvider sp, IWhatsAppService wa) => new(
        new ConfigurationBuilder().Build(), wa,
        sp.GetRequiredService<ISecretProtector>(), sp.GetRequiredService<IEmailSender>(),
        sp.GetRequiredService<ISmsService>(), TutSatEsikleri.Default, NullLogger<VadeBildirimJob>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Patlayan_uretici_Basarisiz_satiri_yazar_sonrakiler_ve_ozet_yine_calisir(int patlayan)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SahneAsync(sp);

        var wa = new SahteWhatsApp();
        var job = Job(sp, wa);
        var adimlar = job.DbAdimlari(tenant, Now).ToList();
        Assert.Equal(Sira, adimlar.Select(a => a.Ad).ToArray()); // job'ın GERÇEK sırası
        adimlar[patlayan] = Patlayan(adimlar[patlayan].Ad, tenant);

        var log = new LogYakalayici();
        var toplam = await VadeBildirimJob.TenantKosAsync(AppOptions(), tenant, adimlar,
            job.OzetAdimi(AppOptions(), tenant, Now), log, CancellationToken.None);

        // Sonraki (ve önceki) üreticiler koştu: dönen toplam yalnız patlayanın payı kadar eksik.
        Assert.Equal(Beklenen.Where((_, i) => i != patlayan).Sum(), toplam);

        // (a) + (b): koşu günlüğü — patlayan Basarisiz, diğerleri kendi başarı satırıyla.
        var loglar = await sp.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(3, loglar.Count);
        for (var i = 0; i < Sira.Length; i++)
        {
            var satir = Assert.Single(loglar, l => l.JobAdi == Sira[i]);
            if (i == patlayan)
            {
                Assert.False(satir.Basarili);
                Assert.Null(satir.SonucSayisi);
                Assert.Contains("Offset=03:00:00", satir.Detay);
            }
            else
            {
                Assert.True(satir.Basarili, $"{Sira[i]}: {satir.Detay}");
                Assert.Equal(Beklenen[i], satir.SonucSayisi);
            }
        }

        // Diğer üreticilerin işi GERÇEKTEN yazıldı (RLS altında görünür) ve "yarım iş" hiçbir yere sızmadı.
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var turler = await db.Bildirimler.AsNoTracking().Select(b => b.Tur).ToListAsync();
            var beklenenTurler = BeklenenTur.Where((t, i) => i != patlayan && t != "").Order().ToArray();
            Assert.Equal(beklenenTurler, turler.Order().ToArray());
        }

        // WhatsApp özeti de düşmedi.
        Assert.Equal(1, wa.Cagri);

        // (3) Log: TEK hata, hangi üretici + hangi tenant; kaydedici kendi satırını yazabildi (Warning yok).
        var hata = Assert.Single(log.Kayitlar, k => k.Seviye >= LogLevel.Error);
        Assert.Equal(Sira[patlayan], hata.Alanlar["Uretici"]);
        Assert.Equal(tenant, hata.Alanlar["Tenant"]);
        Assert.IsType<ArgumentException>(hata.Hata);
        Assert.DoesNotContain(log.Kayitlar, k => k.Seviye == LogLevel.Warning);
    }

    [Fact]
    public async Task WhatsApp_ozeti_patlarsa_loglanir_ve_uretici_sonuclari_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SahneAsync(sp);

        var job = Job(sp, new SahteWhatsApp());
        var log = new LogYakalayici();
        var toplam = await VadeBildirimJob.TenantKosAsync(AppOptions(), tenant, job.DbAdimlari(tenant, Now),
            _ => throw new HttpRequestException("WhatsApp sağlayıcısı yanıt vermedi"), log, CancellationToken.None);

        Assert.Equal(Beklenen.Sum(), toplam);
        var loglar = await sp.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(3, loglar.Count);
        Assert.All(loglar, l => Assert.True(l.Basarili, $"{l.JobAdi}: {l.Detay}"));

        var hata = Assert.Single(log.Kayitlar, k => k.Seviye >= LogLevel.Error);
        Assert.Equal(JobRunRecorder.WhatsAppSummary, hata.Alanlar["Uretici"]);
        Assert.Equal(tenant, hata.Alanlar["Tenant"]);
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
            var ofsetli = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.FromHours(3));
            await Assert.ThrowsAsync<ArgumentException>(() => JobRunRecorder.RunAsync(db, tenant,
                "kirik-baglanti", () => db.Reservations.CountAsync(r => r.BasTar >= ofsetli)));
        }

        var satir = Assert.Single(await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
        Assert.Equal("kirik-baglanti", satir.JobAdi);
        Assert.False(satir.Basarili);
        Assert.Contains("Offset=03:00:00", satir.Detay);
    }

    [Fact]
    public async Task Kaydedicinin_kendi_hatasi_isi_dusurmez_ama_Warning_loglanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var log = new LogYakalayici();

        var sys = new SystemTenantContext { TenantId = tenant };
        int sonuc;
        await using (var db = new AppDbContext(AppOptions(), sys, sys))
        {
            await TenantGuc.OpenAsync(db, tenant);
            // JobAdi kolonu varchar(64) → 100 karakterlik ad INSERT'te reddedilir (22001).
            sonuc = await JobRunRecorder.RunAsync(db, tenant, new string('x', 100),
                () => Task.FromResult(7), n => n, log: log);
        }

        Assert.Equal(7, sonuc); // işin sonucu aynen döndü, günlük hatası işi düşürmedi
        var uyari = Assert.Single(log.Kayitlar, k => k.Seviye == LogLevel.Warning);
        Assert.NotNull(uyari.Hata);
        Assert.Equal(tenant, uyari.Alanlar["Tenant"]);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
    }
}
