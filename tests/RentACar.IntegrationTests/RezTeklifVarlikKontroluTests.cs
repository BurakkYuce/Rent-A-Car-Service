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
    private static readonly DateTimeOffset Bas = TestZaman.GunSonra(10);
    private static readonly DateTimeOffset Bit = Bas.AddDays(2);

    private static BookingInput Rez(Guid m, Guid v) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bit, GunlukUcret = 100m, CikisOfisi = "MERKEZ"
    };

    private static QuotationInput Teklif(Guid m, Guid v) => new()
    {
        MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bit, GunlukUcret = 100m, CikisOfisi = "MERKEZ"
    };

    /// <summary>A kiracısında gerçek müşteri+araç, B kiracısında yabancı müşteri+araç.</summary>
    private async Task<(TestHost host, Guid a, Guid m, Guid v, Guid yabanciM, Guid yabanciV)> KurAsync()
    {
        var host = new TestHost(fx.AppConnectionString);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var m = await TestCari.YeniAsync(host, a, "Varlik");
        var v = await TestArac.YeniAsync(host, a);
        var ym = await TestCari.YeniAsync(host, b, "Yabanci");
        var yv = await TestArac.YeniAsync(host, b);
        return (host, a, m, v, ym, yv);
    }

    public static TheoryData<string> Durumlar => ["yokMusteri", "yabanciMusteri", "yokArac", "yabanciArac"];

    private static (Guid m, Guid v, string alan, string mesaj) Sec(
        string durum, Guid m, Guid v, Guid ym, Guid yv) => durum switch
    {
        "yokMusteri" => (Guid.NewGuid(), v, "musteriId", "Müşteri bulunamadı."),
        "yabanciMusteri" => (ym, v, "musteriId", "Müşteri bulunamadı."),
        "yokArac" => (m, Guid.NewGuid(), "vehicleId", "Araç bulunamadı."),
        "yabanciArac" => (m, yv, "vehicleId", "Araç bulunamadı."),
        _ => throw new ArgumentOutOfRangeException(nameof(durum)),
    };

    [Theory]
    [MemberData(nameof(Durumlar))]
    public async Task Rezervasyon_olusturma_yabanci_ya_da_olmayan_kimligi_reddeder(string durum)
    {
        var (host, a, m, v, ym, yv) = await KurAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (mm, vv, alan, mesaj) = Sec(durum, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Rez(mm, vv)));
        Assert.Equal(mesaj, ex.Message);
        Assert.Equal(alan, ex.Alan);
        Assert.Empty(await svc.ListAsync()); // hiçbir satır yazılmadı
    }

    [Fact]
    public async Task Rezervasyon_olusturma_gecerli_kimlikle_basarili()
    {
        var (host, a, m, v, _, _) = await KurAsync();
        using var _h = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();

        var id = await svc.CreateAsync(Rez(m, v));

        var r = await svc.GetAsync(id);
        Assert.NotNull(r);
        Assert.Equal(m, r!.MusteriId);
        Assert.Equal(v, r.VehicleId);
        Assert.Equal(200m, r.Tutar); // 2 gün × 100 (elle)
    }

    [Theory]
    [MemberData(nameof(Durumlar))]
    public async Task Rezervasyon_duzenleme_yabanci_ya_da_olmayan_kimligi_reddeder_kayit_degismez(string durum)
    {
        var (host, a, m, v, ym, yv) = await KurAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var id = await svc.CreateAsync(Rez(m, v));
        var (mm, vv, alan, mesaj) = Sec(durum, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateAsync(id, Rez(mm, vv)));
        Assert.Equal(mesaj, ex.Message);
        Assert.Equal(alan, ex.Alan);

        var r = await svc.GetAsync(id);
        Assert.Equal(m, r!.MusteriId); // kayıt değişmedi
        Assert.Equal(v, r.VehicleId);
        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Rezervasyon_duzenleme_gecerli_kimlikle_basarili()
    {
        var (host, a, m, v, _, _) = await KurAsync();
        using var _h = host;
        var m2 = await TestCari.YeniAsync(host, a, "Ikinci");
        var v2 = await TestArac.YeniAsync(host, a);
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var id = await svc.CreateAsync(Rez(m, v));

        Assert.True(await svc.UpdateAsync(id, Rez(m2, v2)));

        var r = await svc.GetAsync(id);
        Assert.Equal(m2, r!.MusteriId);
        Assert.Equal(v2, r.VehicleId);
    }

    [Theory]
    [MemberData(nameof(Durumlar))]
    public async Task Teklif_olusturma_yabanci_ya_da_olmayan_kimligi_reddeder(string durum)
    {
        var (host, a, m, v, ym, yv) = await KurAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();
        var (mm, vv, alan, mesaj) = Sec(durum, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Teklif(mm, vv)));
        Assert.Equal(mesaj, ex.Message);
        Assert.Equal(alan, ex.Alan);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Teklif_olusturma_gecerli_kimlikle_basarili()
    {
        var (host, a, m, v, _, _) = await KurAsync();
        using var _h = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<QuotationService>();

        var id = await svc.CreateAsync(Teklif(m, v));

        var q = await svc.GetAsync(id);
        Assert.NotNull(q);
        Assert.Equal(m, q!.MusteriId);
        Assert.Equal(v, q.VehicleId);
        Assert.Equal(200m, q.Tutar); // 2 gün × 100 (elle)
    }

    [Theory]
    [MemberData(nameof(Durumlar))]
    public async Task Filo_kiralama_servis_yolu_yabanci_ya_da_olmayan_kimligi_reddeder(string durum)
    {
        // #332 review L2: Blazor /filo-kiralama formu FiloKiralamaService.CreateAsync'e gider (API ucu değil).
        var (host, a, m, v, ym, yv) = await KurAsync();
        using var _ = host;
        using var scope = host.ScopeFor(a);
        var svc = scope.ServiceProvider.GetRequiredService<FiloKiralamaService>();
        var (mm, vv, alan, mesaj) = Sec(durum, m, v, ym, yv);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new FiloKiralamaInput
        {
            MusteriId = mm, VehicleId = vv, BasTar = Bas, SureAy = 12, AylikUcret = 1000m
        }));
        Assert.Equal(mesaj, ex.Message);
        Assert.Equal(alan, ex.Alan);
        Assert.Empty(await svc.ListAsync()); // hiçbir satır yazılmadı
    }
}
