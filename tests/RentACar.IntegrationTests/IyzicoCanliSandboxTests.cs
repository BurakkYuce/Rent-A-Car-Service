using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kimlik bilgisi yoksa testi ATLAYAN Fact. Kimlikler repo dışındadır (<c>~/.racar-iyzico.env</c>),
/// bu yüzden CI'da bu testler koşmaz ve sarı/kırmızıya düşmez.
/// </summary>
public sealed class IyzicoCanliFactAttribute : FactAttribute
{
    public IyzicoCanliFactAttribute()
    {
        if (!IyzicoCanliSandboxTests.KimlikVar)
            Skip = "iyzico sandbox kimliği yok (RACAR_IYZICO_APIKEY/SECRET ya da ~/.racar-iyzico.env) — atlandı.";
    }
}

/// <summary>
/// CANLI sandbox doğrulaması — gerçek C# adaptörü, gerçek iyzico sandbox'ı.
///
/// <para><b>Neden gerekli:</b> sahte HTTP katmanlı testler "biz ne gönderiyoruz"u doğrular,
/// "sağlayıcı bunu kabul ediyor mu"yu doğrulamaz. Uç yolları, alan adları ve tutar biçimi ancak
/// gerçek çağrıyla kanıtlanır — para yolunda bu fark önemlidir. Repo'nun güven sözleşmesindeki
/// "canlı parite" adımının ödeme karşılığı budur.</para>
///
/// <para>Kimlikler ortam değişkeninden ya da repo DIŞINDAKİ <c>~/.racar-iyzico.env</c>'den okunur;
/// koda gömülmez ve CI'da bulunmadığı için testler atlanır.</para>
/// </summary>
public sealed class IyzicoCanliSandboxTests
{
    internal static bool KimlikVar => Ayar() is not null;

    private static IyzicoAyar? Ayar()
    {
        var api = Environment.GetEnvironmentVariable("RACAR_IYZICO_APIKEY");
        var sec = Environment.GetEnvironmentVariable("RACAR_IYZICO_SECRET");
        var url = Environment.GetEnvironmentVariable("RACAR_IYZICO_BASEURL");

        if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(sec))
        {
            var yol = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".racar-iyzico.env");
            if (!File.Exists(yol)) return null;
            foreach (var satir in File.ReadAllLines(yol))
            {
                var s = satir.Trim();
                if (s.Length == 0 || s.StartsWith('#') || !s.Contains('=')) continue;
                var i = s.IndexOf('=');
                var (ad, deger) = (s[..i].Trim(), s[(i + 1)..].Trim());
                if (ad is "Iyzico__ApiKey") api = deger;
                else if (ad is "Iyzico__SecretKey") sec = deger;
                else if (ad is "Iyzico__BaseUrl") url = deger;
            }
        }
        if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(sec)) return null;
        return new IyzicoAyar
        {
            ApiKey = api,
            SecretKey = sec,
            BaseUrl = string.IsNullOrWhiteSpace(url) ? "https://sandbox-api.iyzipay.com" : url,
        };
    }

    private static IyzicoPosService Servis()
        => new(new VarsayilanHttpFactory(), Ayar()!, NullLogger<IyzicoPosService>.Instance);

    private sealed class VarsayilanHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static PosOdemeIstegi Istek(bool provizyon, decimal tutar = 250m) => new(
        tutar, "TRY", "TEST-" + Guid.NewGuid().ToString("N")[..12],
        "https://ornek.local/odeme/donus",
        new PosAlici("MUS-TEST", "Ahmet", "Yilmaz", "ahmet@ornek.com", "+905350000000",
            "74300864791", "Kadikoy Moda Cad. No:1", "Istanbul", "Turkey", "85.34.78.112"),
        "Arac kiralama testi", provizyon);

    [IyzicoCanliFact]
    public async Task Provizyon_sayfasi_gercekten_acilir()
    {
        var sonuc = await Servis().StartAsync(Istek(provizyon: true));

        Assert.True(sonuc.Ok, sonuc.Hata);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Token));
        Assert.Contains("token=", sonuc.OdemeSayfasiUrl);
    }

    [IyzicoCanliFact]
    public async Task Tahsilat_sayfasi_gercekten_acilir()
    {
        var sonuc = await Servis().StartAsync(Istek(provizyon: false));

        Assert.True(sonuc.Ok, sonuc.Hata);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.OdemeSayfasiUrl));
    }

    [IyzicoCanliFact]
    public async Task Odenmemis_jetonun_sonucu_BASARILI_donmez()
    {
        // Sayfa açıldı ama müşteri ödemedi: sonuç sorgusu "başarılı" DEMEMELİ. Bu, adaptörün
        // status(sorgu) ile paymentStatus(ödeme) ayrımını gerçek yanıt üzerinde kanıtlar.
        var baslat = await Servis().StartAsync(Istek(provizyon: true));
        Assert.True(baslat.Ok, baslat.Hata);

        var sonuc = await Servis().ResultAsync(baslat.Token!);
        Assert.False(sonuc.Ok);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Hata));
    }

    [IyzicoCanliFact]
    public async Task Gecersiz_jeton_gurultulu_reddedilir()
    {
        var sonuc = await Servis().ResultAsync("olmayan-jeton-" + Guid.NewGuid().ToString("N"));

        Assert.False(sonuc.Ok);
        Assert.False(string.IsNullOrWhiteSpace(sonuc.Hata));
    }

    [IyzicoCanliFact]
    public async Task Olmayan_odemenin_kapatilmasi_iptali_iadesi_reddedilir()
    {
        var svc = Servis();
        var sahteId = "999999999";

        var kapat = await svc.CloseAsync(sahteId, 100m, "85.34.78.112");
        Assert.False(kapat.Success);
        Assert.False(string.IsNullOrWhiteSpace(kapat.Error));

        var iptal = await svc.CancelAsync(sahteId, "85.34.78.112");
        Assert.False(iptal.Success);

        var iade = await svc.RefundAsync(sahteId, 100m, "85.34.78.112");
        Assert.False(iade.Success);
    }
}
