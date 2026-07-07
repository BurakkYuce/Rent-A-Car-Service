using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// "Fiyat Türü = Otomatik" tetikleyicisi — bağımsız oracle. Otomatik seçiliyken manuel GunlukUcret
/// SUNUCU tarafında yok sayılır (tarife matrisi tek gerçek kaynak); tarife çözülemezse (matris yok /
/// TRY-dışı matris) temiz red. Otomatik DEĞİLKEN eski kurallar aynen: manuel &gt;0 kazanır;
/// 0 + matris → matris (regresyon). Beklenen değerler ELLE kurulmuş senaryodan:
/// matris Gün3=240 → 3 gün × 240 = 720 (koddan değil).
/// </summary>
[Collection("postgres")]
public sealed class FiyatOtomatikTests(PostgresFixture fx)
{
    // Whole-second UTC taban (PG µs dersi); rezervasyon geçmişe kapalı → gelecek tarih.
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);
    private static DateTimeOffset Bit(int gun) => Bas.AddDays(gun);

    /// <summary>Araç (grup B) + cari tohumlar; istenirse ONAYLI matris (Gün1 300 / Gün2 280 / Gün3 240).</summary>
    private static async Task<(Guid musteri, Guid arac)> SeedAsync(
        IServiceScope s, bool matris = true, string? matrisParaBirimi = "TRY")
    {
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var customers = s.ServiceProvider.GetRequiredService<CustomerService>();
        var arac = await vehicles.CreateAsync(new VehicleInput { Plaka = "34OTM01", Grup = "B" });
        var musteri = await customers.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Otomatik", Soyad = "Test" });
        if (matris)
        {
            var matrisler = s.ServiceProvider.GetRequiredService<RateMatrixService>();
            await matrisler.CreateAsync(new RateMatrixInput
            {
                Kod = "OTM-B", Ad = "Otomatik B", AracGrupKod = "B", ParaBirimi = matrisParaBirimi,
                Gun1 = 300m, Gun2 = 280m, Gun3 = 240m,
                OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "test"
            });
        }
        return (musteri, arac);
    }

    private static BookingInput Booking(Guid m, Guid v, int gun, decimal gunluk, string? fiyatTuru, int offsetGun = 0) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Bas.AddDays(offsetGun), BitTar = Bit(offsetGun + gun),
        GunlukUcret = gunluk, FiyatTuru = fiyatTuru
    };

    // (a) Otomatik + onaylı TRY matris → manuel 999 YOK SAYILIR; kira matris fiyatını alır (3×240=720).
    [Fact]
    public async Task Rental_otomatik_ignores_manual_rate_and_uses_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        var id = await kira.CreateDirectAsync(Booking(m, v, 3, gunluk: 999m, fiyatTuru: "Otomatik"));
        var r = await kira.GetAsync(id);
        Assert.Equal(3, r!.Gun);
        Assert.Equal(240m, r.GunlukUcret);   // matris Gün3, manuel 999 değil
        Assert.Equal(720m, r.Tutar);         // 3 × 240 (elle)
        Assert.Equal(720m, r.GenelToplam);
        Assert.Equal(720m, r.Bakiye);
    }

    // (b) Otomatik + matris yok → temiz red (sessiz 0-TL kira oluşmaz).
    [Fact]
    public async Task Rental_otomatik_without_matrix_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope, matris: false);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => kira.CreateDirectAsync(Booking(m, v, 3, gunluk: 999m, fiyatTuru: "Otomatik")));
        Assert.Contains("Otomatik tarife", ex.Message);
    }

    // (b-FX) Otomatik + yalnız EUR matris → TRY-dışı auto uygulanmaz (booking tek-döviz) → temiz red.
    [Fact]
    public async Task Rental_otomatik_with_fx_only_matrix_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope, matrisParaBirimi: "EUR");

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => kira.CreateDirectAsync(Booking(m, v, 3, gunluk: 0m, fiyatTuru: "Otomatik")));
        Assert.Contains("Otomatik tarife", ex.Message);
    }

    // (c) Otomatik DEĞİL (FiyatTuru boş ya da farklı) + manuel 500 → 500 kalır (matris 240 olsa bile) — regresyon.
    [Fact]
    public async Task Manual_rate_still_wins_when_not_otomatik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        // FiyatTuru boş (null)
        var id1 = await kira.CreateDirectAsync(Booking(m, v, 3, gunluk: 500m, fiyatTuru: null));
        var r1 = await kira.GetAsync(id1);
        Assert.Equal(500m, r1!.GunlukUcret);
        Assert.Equal(1500m, r1.Tutar); // 3 × 500 (elle)

        // FiyatTuru farklı ama Otomatik değil ("KDV Dahil Günlük" = brüt günlük; motora bakılmaz, manuel kazanır).
        // ("Günlük" = NET mod artık brüte çevirir → PR-F3; bu test motor-ezme niyetini test eder, dönüşümü değil.)
        var id2 = await kira.CreateDirectAsync(Booking(m, v, 3, gunluk: 500m, fiyatTuru: "KDV Dahil Günlük", offsetGun: 10));
        var r2 = await kira.GetAsync(id2);
        Assert.Equal(500m, r2!.GunlukUcret);
        Assert.Equal(1500m, r2.Tutar);
    }

    // (d) FiyatTuru boş + ücret 0 + matris var → matris fiyatı (mevcut auto davranış regresyonu).
    [Fact]
    public async Task Empty_fiyatturu_with_zero_rate_still_resolves_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var kira = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        var id = await kira.CreateDirectAsync(Booking(m, v, 3, gunluk: 0m, fiyatTuru: null));
        var r = await kira.GetAsync(id);
        Assert.Equal(240m, r!.GunlukUcret);
        Assert.Equal(720m, r.Tutar);
    }

    // (e) Rezervasyon yolu, (a): Otomatik + matris → manuel yok sayılır (küçük harf "otomatik" de tetikler).
    [Fact]
    public async Task Reservation_otomatik_ignores_manual_rate_and_uses_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await rez.CreateAsync(Booking(m, v, 3, gunluk: 999m, fiyatTuru: "otomatik"));
        var r = await rez.GetAsync(id);
        Assert.Equal(3, r!.Gun);
        Assert.Equal(240m, r.GunlukUcret);
        Assert.Equal(720m, r.Tutar);
    }

    // (e) Rezervasyon yolu, (b): Otomatik + matris yok → temiz red.
    [Fact]
    public async Task Reservation_otomatik_without_matrix_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope, matris: false);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rez.CreateAsync(Booking(m, v, 3, gunluk: 999m, fiyatTuru: "Otomatik")));
        Assert.Contains("Otomatik tarife", ex.Message);
    }

    // (e+) Rezervasyon UPDATE de kapsanır: manuel 500 ile açılan rezervasyon, Otomatik ile
    // güncellenince matris fiyatına döner (manuel 999 yok sayılır).
    [Fact]
    public async Task Reservation_update_otomatik_overrides_manual()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rez = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await rez.CreateAsync(Booking(m, v, 3, gunluk: 500m, fiyatTuru: null));
        Assert.True(await rez.UpdateAsync(id, Booking(m, v, 3, gunluk: 999m, fiyatTuru: "Otomatik")));

        var r = await rez.GetAsync(id);
        Assert.Equal(240m, r!.GunlukUcret);
        Assert.Equal(720m, r.Tutar);
    }
}
