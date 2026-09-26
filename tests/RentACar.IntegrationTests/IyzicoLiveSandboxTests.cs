using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kimlik bilgisi yoksa testi ATLAYAN Fact. Kimlikler repo dışındadır (<c>~/.racar-iyzico.env</c>),
/// bu yüzden CI'da bu testler koşmaz ve sarı/kırmızıya düşmez.
/// </summary>
public sealed class IyzicoLiveFactAttribute : FactAttribute
{
    public IyzicoLiveFactAttribute()
    {
        if (!IyzicoLiveSandboxTests.HasCredentials)
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
public sealed class IyzicoLiveSandboxTests
{
    internal static bool HasCredentials => Setting() is not null;

    private static IyzicoAyar? Setting()
    {
        var api = Environment.GetEnvironmentVariable("RACAR_IYZICO_APIKEY");
        var select = Environment.GetEnvironmentVariable("RACAR_IYZICO_SECRET");
        var url = Environment.GetEnvironmentVariable("RACAR_IYZICO_BASEURL");

        if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(select))
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".racar-iyzico.env");
            if (!File.Exists(path)) return null;
            foreach (var row in File.ReadAllLines(path))
            {
                var s = row.Trim();
                if (s.Length == 0 || s.StartsWith('#') || !s.Contains('=')) continue;
                var i = s.IndexOf('=');
                var (name, value) = (s[..i].Trim(), s[(i + 1)..].Trim());
                if (name is "Iyzico__ApiKey") api = value;
                else if (name is "Iyzico__SecretKey") select = value;
                else if (name is "Iyzico__BaseUrl") url = value;
            }
        }
        if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(select)) return null;
        return new IyzicoAyar
        {
            ApiKey = api,
            SecretKey = select,
            BaseUrl = string.IsNullOrWhiteSpace(url) ? "https://sandbox-api.iyzipay.com" : url,
        };
    }

    private static IyzicoPosService Service()
        => new(new DefaultHttpFactory(), Setting()!, NullLogger<IyzicoPosService>.Instance);

    private sealed class DefaultHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static PosOdemeIstegi Request(bool preAuth, decimal amount = 250m) => new(
        amount, "TRY", "TEST-" + Guid.NewGuid().ToString("N")[..12],
        "https://ornek.local/odeme/donus",
        new PosAlici("MUS-TEST", "Ahmet", "Yilmaz", "ahmet@ornek.com", "+905350000000",
            "74300864791", "Kadikoy Moda Cad. No:1", "Istanbul", "Turkey", "85.34.78.112"),
        "Arac kiralama testi", preAuth);

    [IyzicoLiveFact]
    public async Task PreAuth_page_actually_opens()
    {
        var result = await Service().StartAsync(Request(preAuth: true));

        Assert.True(result.Ok, result.Hata);
        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Contains("token=", result.OdemeSayfasiUrl);
    }

    [IyzicoLiveFact]
    public async Task Collection_page_actually_opens()
    {
        var result = await Service().StartAsync(Request(preAuth: false));

        Assert.True(result.Ok, result.Hata);
        Assert.False(string.IsNullOrWhiteSpace(result.OdemeSayfasiUrl));
    }

    [IyzicoLiveFact]
    public async Task Unpaid_token_result_is_not_SUCCESS()
    {
        // Sayfa açıldı ama müşteri ödemedi: sonuç sorgusu "başarılı" DEMEMELİ. Bu, adaptörün
        // status(sorgu) ile paymentStatus(ödeme) ayrımını gerçek yanıt üzerinde kanıtlar.
        var start = await Service().StartAsync(Request(preAuth: true));
        Assert.True(start.Ok, start.Hata);

        var result = await Service().ResultAsync(start.Token!);
        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Hata));
    }

    [IyzicoLiveFact]
    public async Task Invalid_token_is_rejected_loudly()
    {
        var result = await Service().ResultAsync("olmayan-jeton-" + Guid.NewGuid().ToString("N"));

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Hata));
    }

    [IyzicoLiveFact]
    public async Task Close_cancel_refund_of_nonexistent_payment_is_rejected()
    {
        var svc = Service();
        var fakeId = "999999999";

        var close = await svc.CloseAsync(fakeId, 100m, "85.34.78.112");
        Assert.False(close.Success);
        Assert.False(string.IsNullOrWhiteSpace(close.Error));

        var cancel = await svc.CancelAsync(fakeId, "85.34.78.112");
        Assert.False(cancel.Success);

        var refund = await svc.RefundAsync(fakeId, 100m, "85.34.78.112");
        Assert.False(refund.Success);
    }
}
