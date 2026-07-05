using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Integrations;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// WhatsApp günlük operasyon özeti — üretim + gönderim. BAĞIMSIZ ORACLE: sahne elle kurulur (2 çıkış, 1 dönüş,
/// filo 2/1/1, tahsilat 100TL+50EUR@40=2100 TL). Job yolunun BİREBİR aynısı: raw options (interceptor'SIZ) →
/// TenantId ELLE damgalanmazsa RLS reddi test'te YAKALANIR (K1). İstanbul saat-kapısı + idempotent + retry + izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class WhatsAppOzetTests(PostgresFixture fx)
{
    private static readonly TimeZoneInfo Tz =
        TimeZoneInfo.CreateCustomTimeZone("ist3", TimeSpan.FromHours(3), "İstanbul", "İstanbul");
    private static readonly DateTimeOffset Sabah09 = new(2026, 7, 6, 9, 0, 0, TimeSpan.FromHours(3));  // İstanbul 09:00
    private static readonly DateTimeOffset Sabah06 = new(2026, 7, 6, 6, 0, 0, TimeSpan.FromHours(3));  // İstanbul 06:00
    private static readonly DateTimeOffset GunIci = new(2026, 7, 6, 9, 0, 0, TimeSpan.Zero);  // 09:00Z = İstanbul 12:00 (gün-içi; DB'ye UTC yazılır)
    private static readonly DateOnly Gun = new(2026, 7, 6);

    private sealed class FakeWhatsApp : IWhatsAppService
    {
        public int Calls;
        public string? LastPhone;
        public IReadOnlyDictionary<string, string>? LastParams;
        public bool Result = true;
        public Task<bool> SendTemplateAsync(string phone, string t, IReadOnlyDictionary<string, string> p, CancellationToken ct = default)
        { Calls++; LastPhone = phone; LastParams = p; return Task.FromResult(Result); }
    }

    private DbContextOptions<AppDbContext> RawOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;

    private static async Task SeedAsync(IServiceScope scope, bool toggle, string? no, bool sahne = true)
    {
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        db.TenantSettings.Add(new TenantSettings { WhatsAppGunlukOzet = toggle, WhatsAppNumarasi = no });
        if (sahne)
        {
            // Filo: 2 Kirada, 1 Musait (boşta), 1 Serviste
            db.Vehicles.Add(new Vehicle { Plaka = "34 A 1", Durum = VehicleStatus.Kirada });
            db.Vehicles.Add(new Vehicle { Plaka = "34 A 2", Durum = VehicleStatus.Kirada });
            db.Vehicles.Add(new Vehicle { Plaka = "34 A 3", Durum = VehicleStatus.Musait });
            db.Vehicles.Add(new Vehicle { Plaka = "34 A 4", Durum = VehicleStatus.Serviste });
            // Çıkış: 2 rezervasyon bugün başlıyor
            db.Reservations.Add(new Reservation { ReservationNo = "R1", Durum = ReservationStatus.Rezerv, MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), BasTar = GunIci, BitTar = GunIci.AddDays(3) });
            db.Reservations.Add(new Reservation { ReservationNo = "R2", Durum = ReservationStatus.Onayli, MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), BasTar = GunIci, BitTar = GunIci.AddDays(2) });
            // Dönüş: 1 kira bugün bitiyor
            db.Rentals.Add(new RentalContract { SozlesmeNo = "K1", Durum = RentalStatus.Kirada, MusteriId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), BasTar = GunIci.AddDays(-3), BitTar = GunIci });
            // Tahsilat: 100 TL + 50 EUR@40 = 2100 TL (çok-döviz TL-baz)
            db.CashTransactions.Add(new CashTransaction { No = "C1", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = GunIci, Amount = new Money(100m, "TRY", 1m) });
            db.CashTransactions.Add(new CashTransaction { No = "C2", Tip = CashTransactionType.Tahsilat, CariId = Guid.NewGuid(), Tarih = GunIci, Amount = new Money(50m, "EUR", 40m) });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ozet_dogru_sayimlar_ve_coklu_doviz_tahsilat()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var s = host.ScopeFor(tenant)) await SeedAsync(s, toggle: true, no: "0532");
        var f = host.ScopeFor(tenant).ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();

        var ozet = await OperasyonOzetUretici.BuildAsync(db, Gun, Tz);

        Assert.Equal(2, ozet.Cikis);
        Assert.Equal(1, ozet.Donus);
        Assert.Equal(2, ozet.AcikRez);
        Assert.Equal(2100m, ozet.Tahsilat);   // 100×1 + 50×40 (çok-döviz TL-baz; düz 150 DEĞİL)
        Assert.Equal(2, ozet.Kiradaki);
        Assert.Equal(1, ozet.Bosta);
        Assert.Equal(1, ozet.Serviste);
    }

    [Fact]
    public async Task Gonderir_E164_ve_idempotent()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var s = host.ScopeFor(tenant)) await SeedAsync(s, toggle: true, no: "0532 111 22 33");
        var wa = new FakeWhatsApp();

        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), tenant, wa, Sabah09, Tz, NullLogger.Instance);
        Assert.Equal(1, wa.Calls);
        Assert.Equal("+905321112233", wa.LastPhone);   // E.164 normalize
        Assert.Equal("2", wa.LastParams!["1"]);        // çıkış (kültür-güvenli int)
        Assert.Equal("1", wa.LastParams["2"]);         // dönüş
        Assert.Equal("2", wa.LastParams["5"]);         // kiradaki

        // K1 doğrulaması: WhatsAppGonderim satırı RLS-reddi ALMADAN yazıldı (Basarili=true)
        using (var s = host.ScopeFor(tenant))
        {
            var log = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            var row = await log.WhatsAppGonderimler.SingleAsync();
            Assert.True(row.Basarili);
            Assert.Equal(Gun, row.Gun);
        }

        // idempotent: 2. çağrı → gönderim YOK
        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), tenant, wa, Sabah09, Tz, NullLogger.Instance);
        Assert.Equal(1, wa.Calls);
    }

    [Fact]
    public async Task Toggle_kapali_veya_no_bos_ve_saat_kapisi_gondermez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var wa = new FakeWhatsApp();

        var t1 = Guid.NewGuid();
        using (var s = host.ScopeFor(t1)) await SeedAsync(s, toggle: false, no: "0532");     // toggle kapalı
        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), t1, wa, Sabah09, Tz, NullLogger.Instance);

        var t2 = Guid.NewGuid();
        using (var s = host.ScopeFor(t2)) await SeedAsync(s, toggle: true, no: null);          // no boş
        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), t2, wa, Sabah09, Tz, NullLogger.Instance);

        var t3 = Guid.NewGuid();
        using (var s = host.ScopeFor(t3)) await SeedAsync(s, toggle: true, no: "0532");
        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), t3, wa, Sabah06, Tz, NullLogger.Instance); // 06:00 < 08:00

        Assert.Equal(0, wa.Calls);
    }

    [Fact]
    public async Task Basarisiz_gonderim_ertesi_denemede_tekrar_dener()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var s = host.ScopeFor(tenant)) await SeedAsync(s, toggle: true, no: "0532");
        var wa = new FakeWhatsApp { Result = false }; // Twilio başarısız

        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), tenant, wa, Sabah09, Tz, NullLogger.Instance);
        Assert.Equal(1, wa.Calls); // denendi, başarısız → slot Basarili=false

        wa.Result = true;
        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), tenant, wa, Sabah09, Tz, NullLogger.Instance);
        Assert.Equal(2, wa.Calls); // madde1: başarısız satır → TEKRAR denendi (sessiz-düşme yok)
    }

    [Fact]
    public async Task Tenant_izolasyonu()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using (var s = host.ScopeFor(tenantA)) await SeedAsync(s, toggle: true, no: "0532");
        await WhatsAppOzetGonderici.SendDailyAsync(RawOptions(), tenantA, new FakeWhatsApp(), Sabah09, Tz, NullLogger.Instance);

        // B, A'nın gönderim logunu GÖRMEZ (RLS, racar_app)
        using var sb = host.ScopeFor(tenantB);
        var db = await sb.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Empty(await db.WhatsAppGonderimler.ToListAsync());
    }
}
