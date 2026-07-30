using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-A — logo kabul kuralları ve PDF'e etkisi.
///
/// Bağımsız oracle: PNG baytları ELLE kurulur (IHDR'a istenen genişlik/yükseklik yazılır), beklenen
/// davranış senaryodan gelir ("30 px logo yükledim → sözleşmede logo OLMAMALI").
///
/// En kritik test <c>Absurt_kucuk_logo_PDF_yolunda_YOK_SAYILIR</c>: canlıda yucerent'te 1×1 piksellik
/// 70 baytlık bir PNG yüklüydü ve sözleşme başlığına gerilmiş bir leke basıyordu — logo baytı VAR
/// olduğu için PDF'in metin fallback'i devreye girmiyordu. Bu testin kırmızıya dönmesi, o davranışın
/// geri geldiği anlamına gelir.
/// </summary>
[Collection("postgres")]
public sealed class LogoKurallariTests(PostgresFixture fx)
{
    /// <summary>Verilen ölçüde geçerli imzalı, IHDR'ı doğru PNG baytı üretir (decode edilmez, yalnız
    /// başlık okunuyor). Kuyruk dolgusu istenen dosya boyutunu tutturmak için.</summary>
    private static byte[] Png(int genislik, int yukseklik, int toplamBayt = 200)
    {
        var b = new byte[Math.Max(24, toplamBayt)];
        // İmza
        b[0] = 0x89; b[1] = 0x50; b[2] = 0x4E; b[3] = 0x47; b[4] = 0x0D; b[5] = 0x0A; b[6] = 0x1A; b[7] = 0x0A;
        // IHDR uzunluğu (13) + tip
        b[8] = 0; b[9] = 0; b[10] = 0; b[11] = 13;
        b[12] = 0x49; b[13] = 0x48; b[14] = 0x44; b[15] = 0x52; // "IHDR"
        // Genişlik / yükseklik — big-endian
        b[16] = (byte)(genislik >> 24); b[17] = (byte)(genislik >> 16); b[18] = (byte)(genislik >> 8); b[19] = (byte)genislik;
        b[20] = (byte)(yukseklik >> 24); b[21] = (byte)(yukseklik >> 16); b[22] = (byte)(yukseklik >> 8); b[23] = (byte)yukseklik;
        return b;
    }

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[100]];

    // ---- Saf kural katmanı ----

    [Fact]
    public void PngBoyut_IHDR_den_olculeri_okur()
    {
        Assert.Equal((800, 240), PngBoyut.Oku(Png(800, 240)));
        Assert.Null(PngBoyut.Oku(Jpeg()));            // PNG değil
        Assert.Null(PngBoyut.Oku(new byte[10]));      // 24 bayttan kısa
    }

    [Theory]
    [InlineData(30, false)]    // absürt küçük → basılmaz
    [InlineData(49, false)]    // sınırın hemen altı
    [InlineData(50, true)]     // sınır dahil → basılır (uyarılı)
    [InlineData(599, true)]    // önerinin altı → basılır, uyarı
    [InlineData(600, true)]    // öneri sınırı → uyarı YOK
    public void Degerlendir_baskiya_uygunluk_sinirlarini_uygular(int genislik, bool beklenenUygun)
    {
        var d = LogoKurallari.Degerlendir(Png(genislik, genislik / 2 + 1));
        Assert.Equal(beklenenUygun, d.BaskiyaUygun);
        Assert.Equal(genislik, d.Genislik);
        // 600 ve üstünde uyarı olmamalı; altında (basılsa da) uyarı OLMALI.
        Assert.Equal(genislik >= LogoKurallari.OnerilenGenislik, d.Uyari is null);
    }

    [Fact]
    public void Reddet_JPEG_ve_asiri_buyuk_olculeri_reddeder_PNG_i_gecirir()
    {
        Assert.Null(LogoKurallari.Reddet(Png(800, 240)));                       // kabul
        Assert.NotNull(LogoKurallari.Reddet(Jpeg()));                           // PNG-only daraltması
        Assert.NotNull(LogoKurallari.Reddet(Png(2001, 500)));                   // ölçü üst sınırı
        Assert.NotNull(LogoKurallari.Reddet(Png(800, 240, toplamBayt: 1_100_000))); // 1 MB üstü
    }

    [Fact]
    public void Olcusu_okunamayan_PNG_basilmaz()
    {
        // Geçerli imza ama IHDR tipi bozuk → boyut bilinmiyor. QuestPDF'te patlamasına izin vermek
        // sözleşmenin HİÇ basılamaması demek; bilinen-iyi davranışa (metin) düşülür.
        var bozuk = Png(800, 240);
        bozuk[13] = 0x00; // "IHDR" → bozuldu
        var d = LogoKurallari.Degerlendir(bozuk);
        Assert.False(d.BaskiyaUygun);
        Assert.NotNull(d.Uyari);
    }

    // ---- Servis katmanı (derinlik: doğrulama artık BURADA) ----

    [Fact]
    public async Task SetLogoAsync_JPEG_i_SERVIS_seviyesinde_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();

        // PR-A öncesi: yalnız web ucu doğruluyordu, servis her baytı kabul ediyordu.
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetLogoAsync(Jpeg()));
    }

    [Fact]
    public async Task SetLogoAsync_gecerli_PNG_i_kabul_eder_ve_null_ile_kaldirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await svc.SetLogoAsync(Png(800, 240));
        Assert.NotNull((await svc.GetAsync()).LogoBytes);

        await svc.SetLogoAsync(null);
        Assert.Null((await svc.GetAsync()).LogoBytes);
    }

    // ---- PDF yolu (asıl kilit) ----

    [Fact]
    public async Task Absurt_kucuk_logo_PDF_yolunda_YOK_SAYILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        var ayarlar = s.ServiceProvider.GetRequiredService<TenantSettingsService>();

        // 1×1 piksel — canlıda yucerent'te tam bu durum vardı (70 bayt).
        await ayarlar.SetLogoAsync(Png(1, 1));
        Assert.NotNull((await ayarlar.GetAsync()).LogoBytes); // AYAR'da duruyor

        var rentalId = await KiraKurAsync(host, tenant);
        var view = await s.ServiceProvider.GetRequiredService<SozlesmeService>().GetAsync(rentalId);

        // …ama PDF'e GİTMİYOR → mevcut metin fallback'i (firma markası/ünvanı) devreye giriyor.
        Assert.NotNull(view);
        Assert.Null(view!.FirmaLogo);
    }

    [Fact]
    public async Task Yeterli_boyuttaki_logo_PDF_yoluna_GECER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        await s.ServiceProvider.GetRequiredService<TenantSettingsService>().SetLogoAsync(Png(800, 240));

        var rentalId = await KiraKurAsync(host, tenant);
        var view = await s.ServiceProvider.GetRequiredService<SozlesmeService>().GetAsync(rentalId);

        Assert.NotNull(view!.FirmaLogo);
    }

    /// <summary>Sözleşme görünümü için asgari kira: cari + araç + kira.</summary>
    private static async Task<Guid> KiraKurAsync(TestHost host, Guid tenant)
    {
        using var s = host.ScopeFor(tenant);
        var cariId = await s.ServiceProvider.GetRequiredService<Application.Customers.CustomerService>()
            .CreateAsync(new Application.Customers.CustomerInput
            { Tip = Domain.Enums.CariType.Bireysel, Ad = "Logo Testi", CepTel = "0555 000 00 01" });
        var aracId = await s.ServiceProvider.GetRequiredService<Application.Vehicles.VehicleService>()
            .CreateAsync(new Application.Vehicles.VehicleInput
            {
                Plaka = "34LG" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
                Durum = Domain.Enums.VehicleStatus.Musait, GrupBilincliBos = true,
            });
        return await s.ServiceProvider.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cariId,
            VehicleId = aracId,
            BasTar = DateTimeOffset.UtcNow.AddDays(1),
            BitTar = DateTimeOffset.UtcNow.AddDays(4),
            GunlukUcret = 1000m,
        });
    }
}
