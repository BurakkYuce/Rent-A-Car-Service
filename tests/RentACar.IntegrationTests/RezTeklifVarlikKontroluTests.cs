using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.FiloKiralamalar;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// DEVIR §6 Low (#328 inceleme Low-1): rezervasyon oluştur/düzenle ve teklif oluştur, müşteri/araç varlığını SERVİS
/// girişinde denetler — harici API, <c>/api/ui</c> ve Blazor formu aynı kuraldan geçer. Hiç olmayan ya da BAŞKA
/// KİRACININ kimliği 400 (<c>musteriId</c> / <c>vehicleId</c>) ile reddedilir ve hiçbir satır yazılmaz; geçerli
/// kimlikle aynı istek başarılı olur. <c>racar_app</c> (RLS) + iki kiracı. Beklenen tutar elle: 2 gün × 100 = 200.
/// </summary>
[Collection("postgres")]
public sealed class RezTeklifVarlikKontroluTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = TestZaman.DaysLater(10);
    private static readonly DateTimeOffset Bit = Start.AddDays(2);

    private static BookingInput Reservation(Guid m, Guid v) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Bit, GunlukUcret = 100m, CikisOfisi = "MERKEZ"
    };

    private static QuotationInput Quotation(Guid m, Guid v) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Bit, GunlukUcret = 100m, CikisOfisi = "MERKEZ"
    };

    /// <summary>A kiracısında gerçek müşteri+araç, B kiracısında yabancı müşteri+araç.</summary>
    private async Task<(TestHost host, Guid a, Guid m, Guid v, Guid yabanciM, Guid yabanciV)> ExchangeRateAsync()
    {
        var host = new TestHost(fx.AppConnectionString);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var m = await TestCustomer.NewAsync(host, a, "Varlik");
        var v = await TestVehicle.NewAsync(host, a);
        var ym = await TestCustomer.NewAsync(host, b, "Yabanci");
        var yv = await TestVehicle.NewAsync(host, b);
        return (host, a, m, v, ym, yv);
    }

    public static TheoryData<string> Statuses => ["yokMusteri", "yabanciMusteri", "yokArac", "yabanciArac"];

    private static (Guid m, Guid v, string alan, string mesaj) Select(
        string status, Guid m, Guid v, Guid ym, Guid yv) => status switch
    {
        "yokMusteri" => (Guid.NewGuid(), v, "musteriId", "Müşteri bulunamadı."),
        "yabanciMusteri" => (ym, v, "musteriId", "Müşteri bulunamadı."),
        "yokArac" => (m, Guid.NewGuid(), "vehicleId", "Araç bulunamadı."),
        "yabanciArac" => (m, yv, "vehicleId", "Araç bulunamadı."),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Rezervasyon_olusturma_yabanci_ya_da_olmayan_kimligi_reddeder(string status)
    {
        var (host, a, m, v, ym, yv) = await ExchangeRateAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (mm, vv, alan, message) = Select(status, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Reservation(mm, vv)));
        Assert.Equal(message, ex.Message);
        Assert.Equal(alan, ex.Alan);
        Assert.Empty(await svc.ListAsync()); // hiçbir satır yazılmadı
    }

    [Fact]
    public async Task Rezervasyon_olusturma_gecerli_kimlikle_basarili()
    {
        var (host, a, m, v, _, _) = await ExchangeRateAsync();
        using var _h = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();

        var id = await svc.CreateAsync(Reservation(m, v));

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal(m, r!.MusteriId);
        Assert.Equal(v, r.VehicleId);
        Assert.Equal(200m, r.Tutar); // 2 gün × 100 (elle)
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Rezervasyon_duzenleme_yabanci_ya_da_olmayan_kimligi_reddeder_kayit_degismez(string status)
    {
        var (host, a, m, v, ym, yv) = await ExchangeRateAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var id = await svc.CreateAsync(Reservation(m, v));
        var (mm, vv, alan, message) = Select(status, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(id, Reservation(mm, vv)));
        Assert.Equal(message, ex.Message);
        Assert.Equal(alan, ex.Alan);

        var r = await svc.GetAsync(id);
        Assert.Equal(m, r!.MusteriId); // kayıt değişmedi
        Assert.Equal(v, r.VehicleId);
        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Rezervasyon_duzenleme_gecerli_kimlikle_basarili()
    {
        var (host, a, m, v, _, _) = await ExchangeRateAsync();
        using var _h = host;
        var m2 = await TestCustomer.NewAsync(host, a, "Ikinci");
        var v2 = await TestVehicle.NewAsync(host, a);
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var id = await svc.CreateAsync(Reservation(m, v));

        Assert.True(await svc.UpdateAsync(id, Reservation(m2, v2)));

        var r = await svc.GetAsync(id);
        Assert.Equal(m2, r!.MusteriId);
        Assert.Equal(v2, r.VehicleId);
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Teklif_olusturma_yabanci_ya_da_olmayan_kimligi_reddeder(string status)
    {
        var (host, a, m, v, ym, yv) = await ExchangeRateAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (mm, vv, alan, message) = Select(status, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Quotation(mm, vv)));
        Assert.Equal(message, ex.Message);
        Assert.Equal(alan, ex.Alan);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Teklif_olusturma_gecerli_kimlikle_basarili()
    {
        var (host, a, m, v, _, _) = await ExchangeRateAsync();
        using var _h = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();

        var id = await svc.CreateAsync(Quotation(m, v));

        var q = await svc.GetAsync(id);
        Assert.NotNull(q);
        Assert.Equal(m, q!.MusteriId);
        Assert.Equal(v, q.VehicleId);
        Assert.Equal(200m, q.Tutar); // 2 gün × 100 (elle)
    }

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task Filo_kiralama_servis_yolu_yabanci_ya_da_olmayan_kimligi_reddeder(string status)
    {
        // #332 review L2: Blazor /filo-kiralama formu FiloKiralamaService.CreateAsync'e gider (API ucu değil).
        var (host, a, m, v, ym, yv) = await ExchangeRateAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<FleetRentalService>();
        var (mm, vv, alan, message) = Select(status, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new FiloKiralamaInput
        {
            MusteriId = mm, VehicleId = vv, BasTar = Start, SureAy = 12, AylikUcret = 1000m
        }));
        Assert.Equal(message, ex.Message);
        Assert.Equal(alan, ex.Alan);
        Assert.Empty(await svc.ListAsync()); // hiçbir satır yazılmadı
    }
}
