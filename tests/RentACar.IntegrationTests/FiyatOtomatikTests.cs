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
/// tarifeye KARŞI yok sayılır (tarife matrisi tek gerçek kaynak). Tarife çözülemezse (matris yok /
/// TRY-dışı matris) girilen ücret NET kurtarma değeri olur → +KDV; ücret de yoksa temiz red
/// (sessiz 0-TL sözleşme oluşmaz — bkz. <see cref="OtomatikFiyatKurtarmaTests"/>).
/// Otomatik DEĞİLKEN eski kurallar aynen: manuel &gt;0 kazanır; 0 + matris → matris (regresyon).
/// Beklenen değerler ELLE kurulmuş senaryodan: matris Gün3=240 → 3 gün × 240 = 720 (koddan değil).
/// </summary>
[Collection("postgres")]
public sealed class FiyatOtomatikTests(PostgresFixture fx)
{
    // Whole-second UTC taban (PG µs dersi); rezervasyon geçmişe kapalı → gelecek tarih.
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);
    private static DateTimeOffset Bit(int day) => Start.AddDays(day);

    /// <summary>Araç (grup B) + cari tohumlar; istenirse ONAYLI matris (Gün1 300 / Gün2 280 / Gün3 240).</summary>
    private static async Task<(Guid musteri, Guid arac)> SeedAsync(
        IServiceScope s, bool matrix = true, string? matrixCurrency = "TRY")
    {
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var customers = s.ServiceProvider.GetRequiredService<CustomerService>();
        var vehicle = await vehicles.CreateAsync(new VehicleInput { Plaka = "34OTM01", Grup = "B" });
        var customer = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Otomatik", Soyad = "Test" });
        if (matrix)
        {
            var matrices = s.ServiceProvider.GetRequiredService<RateMatrixService>();
            await matrices.CreateAsync(new RateMatrixInput
            {
                Kod = "OTM-B", Ad = "Otomatik B", AracGrupKod = "B", ParaBirimi = matrixCurrency,
                Gun1 = 300m, Gun2 = 280m, Gun3 = 240m,
                OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "test"
            });
        }
        return (musteri: customer, arac: vehicle);
    }

    private static BookingInput Booking(Guid m, Guid v, int day, decimal daily, string? priceType, int offsetDays = 0) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Start.AddDays(offsetDays), BitTar = Bit(offsetDays + day),
        GunlukUcret = daily, FiyatTuru = priceType
    };

    // (a) Otomatik + onaylı TRY matris → manuel 999 YOK SAYILIR; kira matris fiyatını alır (3×240=720).
    [Fact]
    public async Task Rental_otomatik_ignores_manual_rate_and_uses_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        var id = await rental.CreateDirectAsync(Booking(m, v, 3, daily: 999m, priceType: "Otomatik"));
        var r = await rental.GetAsync(id);
        Assert.Equal(3, r!.Gun);
        Assert.Equal(240m, r.GunlukUcret);   // matris Gün3, manuel 999 değil
        Assert.Equal(720m, r.Tutar);         // 3 × 240 (elle)
        Assert.Equal(720m, r.GenelToplam);
        Assert.Equal(720m, r.Bakiye);
    }

    // (b) Otomatik + matris yok + GİRİLEN ücret → red DEĞİL: girilen 999 NET kabul edilir, +KDV.
    // (Eski davranış koşulsuz reddediyordu; kullanıcı fiyatı yazmışken de hata alıyordu.)
    [Fact]
    public async Task Rental_otomatik_without_matrix_girilen_ucrete_kdv_ekler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope, matrix: false);

        var id = await rental.CreateDirectAsync(Booking(m, v, 3, daily: 999m, priceType: "Otomatik"));
        var r = (await rental.GetAsync(id))!;
        Assert.Equal(1198.80m, r.GunlukUcret);   // 999 × 1,20 (elle)
        Assert.Equal(3596.40m, r.Tutar);         // 3 × 1.198,80 (elle)
    }

    // (b-0) Otomatik + matris yok + ücret de yok → HÂLÂ temiz red (sessiz 0-TL kira oluşmaz).
    [Fact]
    public async Task Rental_otomatik_without_matrix_and_without_rate_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope, matrix: false);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rental.CreateDirectAsync(Booking(m, v, 3, daily: 0m, priceType: "Otomatik")));
        Assert.Contains("Otomatik tarife", ex.Message);
    }

    // (b-FX) Otomatik + yalnız EUR matris → TRY-dışı auto uygulanmaz (booking tek-döviz) → temiz red.
    [Fact]
    public async Task Rental_otomatik_with_fx_only_matrix_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope, matrixCurrency: "EUR");

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => rental.CreateDirectAsync(Booking(m, v, 3, daily: 0m, priceType: "Otomatik")));
        Assert.Contains("Otomatik tarife", ex.Message);
    }

    // (c) Otomatik DEĞİL (FiyatTuru boş ya da farklı) + manuel 500 → 500 kalır (matris 240 olsa bile) — regresyon.
    [Fact]
    public async Task Manual_rate_still_wins_when_not_otomatik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        // FiyatTuru boş (null)
        var id1 = await rental.CreateDirectAsync(Booking(m, v, 3, daily: 500m, priceType: null));
        var r1 = await rental.GetAsync(id1);
        Assert.Equal(500m, r1!.GunlukUcret);
        Assert.Equal(1500m, r1.Tutar); // 3 × 500 (elle)

        // FiyatTuru farklı ama Otomatik değil ("KDV Dahil Günlük" = brüt günlük; motora bakılmaz, manuel kazanır).
        // ("Günlük" = NET mod artık brüte çevirir → PR-F3; bu test motor-ezme niyetini test eder, dönüşümü değil.)
        var id2 = await rental.CreateDirectAsync(Booking(m, v, 3, daily: 500m, priceType: "KDV Dahil Günlük", offsetDays: 10));
        var r2 = await rental.GetAsync(id2);
        Assert.Equal(500m, r2!.GunlukUcret);
        Assert.Equal(1500m, r2.Tutar);
    }

    // (d) FiyatTuru boş + ücret 0 + matris var → matris fiyatı (mevcut auto davranış regresyonu).
    [Fact]
    public async Task Empty_fiyatturu_with_zero_rate_still_resolves_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var rental = scope.ServiceProvider.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(scope);

        var id = await rental.CreateDirectAsync(Booking(m, v, 3, daily: 0m, priceType: null));
        var r = await rental.GetAsync(id);
        Assert.Equal(240m, r!.GunlukUcret);
        Assert.Equal(720m, r.Tutar);
    }

    // (e) Rezervasyon yolu, (a): Otomatik + matris → manuel yok sayılır (küçük harf "otomatik" de tetikler).
    [Fact]
    public async Task Reservation_otomatik_ignores_manual_rate_and_uses_matrix()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await res.CreateAsync(Booking(m, v, 3, daily: 999m, priceType: "otomatik"));
        var r = await res.GetAsync(id);
        Assert.Equal(3, r!.Gun);
        Assert.Equal(240m, r.GunlukUcret);
        Assert.Equal(720m, r.Tutar);
    }

    // (e) Rezervasyon yolu, (b) ile AYNI: kurtarma ortak facade'da olduğu için üç create yolu da kapsanır.
    [Fact]
    public async Task Reservation_otomatik_without_matrix_girilen_ucrete_kdv_ekler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope, matrix: false);

        var id = await res.CreateAsync(Booking(m, v, 3, daily: 999m, priceType: "Otomatik"));
        var r = (await res.GetAsync(id))!;
        Assert.Equal(1198.80m, r.GunlukUcret);   // 999 × 1,20 (elle)
        Assert.Equal(3596.40m, r.Tutar);         // 3 × 1.198,80 (elle)
    }

    // (e+) Rezervasyon UPDATE de kapsanır: manuel 500 ile açılan rezervasyon, Otomatik ile
    // güncellenince matris fiyatına döner (manuel 999 yok sayılır).
    [Fact]
    public async Task Reservation_update_otomatik_overrides_manual()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (m, v) = await SeedAsync(scope);

        var id = await res.CreateAsync(Booking(m, v, 3, daily: 500m, priceType: null));
        Assert.True(await res.UpdateAsync(id, Booking(m, v, 3, daily: 999m, priceType: "Otomatik")));

        var r = await res.GetAsync(id);
        Assert.Equal(240m, r!.GunlukUcret);
        Assert.Equal(720m, r.Tutar);
    }
}
