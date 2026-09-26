using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RentACar.Application.Integrations;

namespace RentACar.Infrastructure.Integrations;

/// <summary>iyzico bağlantı ayarları (config: <c>Iyzico:*</c>).</summary>
public sealed class IyzicoAyar
{
    public string BaseUrl { get; init; } = "https://sandbox-api.iyzipay.com";
    public string ApiKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public bool Yapilandirildi => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>
/// iyzico ödeme adaptörü — BARINDIRILAN ödeme sayfası (Checkout Form) modeli.
///
/// <para><b>Uç haritası sandbox'a karşı AMPİRİK doğrulandı</b> (tahminle yazılmadı; para yolunda
/// tahmin kabul edilemez):</para>
/// <list type="table">
/// <item><term>Ön provizyon başlat</term><description><c>/payment/iyzipos/checkoutform/initialize/preauth/ecom</c></description></item>
/// <item><term>Tahsilat başlat</term><description><c>/payment/iyzipos/checkoutform/initialize/auth/ecom</c></description></item>
/// <item><term>Sonuç sorgula</term><description><c>/payment/iyzipos/checkoutform/auth/ecom/detail</c></description></item>
/// <item><term>Provizyon kapat</term><description><c>/payment/postauth</c></description></item>
/// <item><term>İptal</term><description><c>/payment/cancel</c></description></item>
/// <item><term>İade</term><description><c>/payment/refund</c></description></item>
/// </list>
///
/// <para><b>Kart verisi buraya UĞRAMAZ.</b> Doğrudan API'nin <c>/payment/preauth</c> ucu kart
/// numarasını istek gövdesinde ister; o yol PCI kapsamını üstümüze alacağı için BİLİNÇLİ olarak
/// kullanılmıyor. Müşteri iyzico'nun sayfasında kartını girer, biz jeton ve sonuç görürüz.</para>
///
/// <para><b>İmza gövdenin BİREBİR metni üzerinden hesaplanır</b> — bu yüzden gövde bir kez
/// serileştirilip hem imzaya hem isteğe aynı string olarak verilir. Yeniden serileştirme
/// (alan sırası/boşluk farkı) "Geçersiz imza" üretir.</para>
///
/// <para>Hata/timeout (20 sn) → <c>Ok=false</c> + Türkçe cümle; istisna YUKARI SIZMAZ.</para>
/// </summary>
public sealed class IyzicoPosService(
    IHttpClientFactory httpFactory, IyzicoAyar setting, ILogger<IyzicoPosService> log) : IPosService
{
    private const string EndpointPreauth = "/payment/iyzipos/checkoutform/initialize/preauth/ecom";
    private const string EndpointAuth = "/payment/iyzipos/checkoutform/initialize/auth/ecom";
    private const string EndpointResult = "/payment/iyzipos/checkoutform/auth/ecom/detail";
    private const string EndpointClose = "/payment/postauth";
    private const string EndpointCancel = "/payment/cancel";
    private const string EndpointRefund = "/payment/refund";

    public async Task<PosBaslatSonuc> StartAsync(PosOdemeIstegi request, CancellationToken ct = default)
    {
        if (!setting.Yapilandirildi)
            return new PosBaslatSonuc(false, null, null, "Ödeme sağlayıcısı yapılandırılmadı.");
        if (request.Tutar <= 0)
            return new PosBaslatSonuc(false, null, null, "Ödeme tutarı sıfırdan büyük olmalıdır.");

        var amount = Amount(request.Tutar);
        var body = new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = request.Referans,
            ["price"] = amount,
            // paidPrice = price: komisyon/vade farkı BİNDİRİLMEZ. Farklı olsaydı müşteriden çekilen
            // tutarla sözleşmedeki tutar ayrışır, mutabakat bozulurdu.
            ["paidPrice"] = amount,
            ["currency"] = request.ParaBirimi,
            ["basketId"] = request.Referans,
            ["paymentGroup"] = "PRODUCT",
            ["callbackUrl"] = request.DonusUrl,
            ["enabledInstallments"] = new[] { 1 }, // taksit KAPALI: kira bedeli taksitlendirilmiyor
            ["buyer"] = new Dictionary<string, object?>
            {
                ["id"] = request.Alici.Id,
                ["name"] = request.Alici.Ad,
                ["surname"] = request.Alici.Soyad,
                ["gsmNumber"] = request.Alici.Telefon,
                ["email"] = request.Alici.Eposta,
                ["identityNumber"] = request.Alici.KimlikNo,
                ["registrationAddress"] = request.Alici.Adres,
                ["ip"] = request.Alici.Ip,
                ["city"] = request.Alici.Sehir,
                ["country"] = request.Alici.Ulke,
            },
            ["shippingAddress"] = Address(request.Alici),
            ["billingAddress"] = Address(request.Alici),
            // Sepet toplamı price'a EŞİT olmak zorunda (sağlayıcı doğrular) → tek kalem.
            ["basketItems"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = request.Referans,
                    ["name"] = Shorten(request.Aciklama, 100),
                    ["category1"] = "Arac Kiralama",
                    ["itemType"] = "VIRTUAL", // araç kiralama fiziksel teslimat değil
                    ["price"] = amount,
                },
            },
        };

        var (ok, root, error) = await RequestAsync(request.Provizyon ? EndpointPreauth : EndpointAuth, body, ct);
        if (!ok) return new PosBaslatSonuc(false, null, null, error);

        var token = Text(root, "token");
        var url = Text(root, "paymentPageUrl");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(url))
            return new PosBaslatSonuc(false, null, null, "Sağlayıcı ödeme sayfası bilgisi döndürmedi.");
        return new PosBaslatSonuc(true, token, url, null);
    }

    public async Task<PosDurumSonuc> ResultAsync(string token, CancellationToken ct = default)
    {
        if (!setting.Yapilandirildi)
            return new PosDurumSonuc(false, null, null, null, null, null, null, "Ödeme sağlayıcısı yapılandırılmadı.");
        if (string.IsNullOrWhiteSpace(token))
            return new PosDurumSonuc(false, null, null, null, null, null, null, "Ödeme jetonu boş.");

        var (ok, root, error) = await RequestAsync(EndpointResult,
            new Dictionary<string, object?> { ["locale"] = "tr", ["conversationId"] = "sonuc", ["token"] = token }, ct);
        if (!ok) return new PosDurumSonuc(false, null, null, null, null, null, null, error);

        // paymentStatus SUCCESS değilse ödeme TAMAMLANMAMIŞTIR (kart reddi, 3DS başarısız, vazgeçme).
        // status=success yalnız "sorgu başarılı" demektir — ikisini karıştırmak, ödenmemiş bir
        // kiralamayı ödenmiş saymak olurdu.
        var paymentStatus = Text(root, "paymentStatus");
        var successful = string.Equals(paymentStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        var paymentId = Text(root, "paymentId");
        var transactionId = FirstTransactionId(root);
        var amount = ReadDecimal(root, "paidPrice");
        var card = CardSummary(root);
        var reference = Text(root, "conversationId");

        if (!successful)
        {
            var message = Text(root, "errorMessage")
                        ?? $"Ödeme tamamlanmadı (durum: {paymentStatus ?? "bilinmiyor"}).";
            return new PosDurumSonuc(false, paymentId, transactionId, paymentStatus, amount, card, reference, message);
        }
        return new PosDurumSonuc(true, paymentId, transactionId, paymentStatus, amount, card, reference, null);
    }

    public Task<PosResult> CloseAsync(string paymentId, decimal amount, string ip, CancellationToken ct = default)
        => SimpleAsync(EndpointClose, new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = "kapat-" + paymentId,
            ["paymentId"] = paymentId,
            ["paidPrice"] = Amount(amount),
            ["ip"] = ip,
        }, "paymentId", ct);

    public Task<PosResult> CancelAsync(string paymentId, string ip, CancellationToken ct = default)
        => SimpleAsync(EndpointCancel, new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = "iptal-" + paymentId,
            ["paymentId"] = paymentId,
            ["ip"] = ip,
        }, "paymentId", ct);

    public Task<PosResult> RefundAsync(string transactionId, decimal amount, string ip, CancellationToken ct = default)
        => SimpleAsync(EndpointRefund, new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = "iade-" + transactionId,
            ["paymentTransactionId"] = transactionId,
            ["price"] = Amount(amount),
            ["ip"] = ip,
        }, "paymentId", ct);

    // ---- ortak ----

    private async Task<PosResult> SimpleAsync(
        string uri, Dictionary<string, object?> body, string refAlan, CancellationToken ct)
    {
        if (!setting.Yapilandirildi) return new PosResult(false, null, "Ödeme sağlayıcısı yapılandırılmadı.");
        var (ok, root, error) = await RequestAsync(uri, body, ct);
        return ok ? new PosResult(true, Text(root, refAlan), null) : new PosResult(false, null, error);
    }

    /// <summary>İsteği gönderir; <c>status=="success"</c> değilse sağlayıcının Türkçe hatasını taşır.</summary>
    private async Task<(bool Ok, JsonElement Kok, string? Hata)> RequestAsync(
        string uri, Dictionary<string, object?> body, CancellationToken ct)
    {
        // Gövde TEK KEZ serileştirilir: imza ile gönderilen metin BİREBİR aynı olmak zorunda.
        var text = JsonSerializer.Serialize(body);
        var (auth, rnd) = IyzicoSignature.Generate(setting.ApiKey, setting.SecretKey, uri, text);

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var req = new HttpRequestMessage(HttpMethod.Post, setting.BaseUrl.TrimEnd('/') + uri)
            {
                Content = new StringContent(text, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            req.Headers.TryAddWithoutValidation("x-iyzi-rnd", rnd);

            var resp = await http.SendAsync(req, ct);
            var bodyText = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("iyzico {Uri} HTTP {Kod}: {Govde}", uri, (int)resp.StatusCode, bodyText);
                return (false, default, $"Ödeme sağlayıcısına ulaşılamadı (HTTP {(int)resp.StatusCode}).");
            }

            using var doc = JsonDocument.Parse(bodyText);
            var root = doc.RootElement.Clone(); // doc dispose olduktan sonra da okunabilsin
            if (!string.Equals(Text(root, "status"), "success", StringComparison.OrdinalIgnoreCase))
            {
                var code = Text(root, "errorCode");
                var message = Text(root, "errorMessage") ?? "Ödeme sağlayıcısı isteği reddetti.";
                log.LogWarning("iyzico {Uri} reddetti: {Kod} {Mesaj}", uri, code, message);
                return (false, root, code is null ? message : $"{message} (kod {code})");
            }
            return (true, root, null);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("iyzico {Uri} zaman aşımı.", uri);
            return (false, default, "Ödeme sağlayıcısı zamanında yanıt vermedi.");
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "iyzico {Uri} yanıtı çözümlenemedi.", uri);
            return (false, default, "Ödeme sağlayıcısının yanıtı okunamadı.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "iyzico {Uri} çağrısı başarısız.", uri);
            return (false, default, "Ödeme sağlayıcısına bağlanılamadı.");
        }
    }

    private static Dictionary<string, object?> Address(PosAlici a) => new()
    {
        ["contactName"] = $"{a.Ad} {a.Soyad}".Trim(),
        ["city"] = a.Sehir,
        ["country"] = a.Ulke,
        ["address"] = a.Adres,
    };

    /// <summary>
    /// Sağlayıcının beklediği tutar biçimi: nokta ondalık, InvariantCulture, en az bir ondalık hane.
    /// Türkçe kültürde <c>ToString()</c> virgül üretir ve istek reddedilir — bu yüzden kültür AÇIK.
    /// </summary>
    public static string Amount(decimal value)
        => value.ToString("0.0#####", CultureInfo.InvariantCulture);

    private static string Shorten(string? s, int n)
        => string.IsNullOrWhiteSpace(s) ? "Arac kiralama" : (s.Length <= n ? s : s[..n]);

    private static string? Text(JsonElement root, string alan)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(alan, out var v)
           && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static decimal? ReadDecimal(JsonElement root, string alan)
    {
        var m = Text(root, alan);
        return decimal.TryParse(m, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>İade ucu ödeme kimliğini değil KALEM işlem kimliğini ister — ilk kalemden okunur.</summary>
    private static string? FirstTransactionId(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("itemTransactions", out var items)
            || items.ValueKind != JsonValueKind.Array) return null;
        foreach (var k in items.EnumerateArray())
            if (Text(k, "paymentTransactionId") is { Length: > 0 } id) return id;
        return null;
    }

    /// <summary>Kartın son 4 hanesi + ailesi — makbuzda/ekranda gösterilir (tam numara ASLA tutulmaz).</summary>
    private static string? CardSummary(JsonElement root)
    {
        var last4 = Text(root, "lastFourDigits");
        var aile = Text(root, "cardAssociation");
        if (last4 is null && aile is null) return null;
        return string.Join(' ', new[] { aile, last4 is null ? null : "**** " + last4 }.Where(x => x is not null));
    }
}

/// <summary>
/// iyzico kurulumu — Web ve PublicSite AYNI çağrıyı kullanır (ikisi de ödeme başlatabiliyor:
/// ERP'de depozito provizyonu, halka açık sitede online rezervasyon).
/// </summary>
public static class IyzicoSetup
{
    /// <summary>
    /// Config'te <c>Iyzico:ApiKey</c> + <c>Iyzico:SecretKey</c> VARSA gerçek adaptörü kaydeder
    /// (stub'ı override eder); yoksa hiçbir şey yapmaz ve stub dürüstçe "yapılandırılmadı" döner.
    /// </summary>
    public static IServiceCollection AddIyzico(
        this IServiceCollection services, IConfiguration config, bool developmentEnvironment, ILogger? log = null)
    {
        var setting = new IyzicoAyar
        {
            BaseUrl = config["Iyzico:BaseUrl"] ?? "https://sandbox-api.iyzipay.com",
            ApiKey = config["Iyzico:ApiKey"] ?? string.Empty,
            SecretKey = config["Iyzico:SecretKey"] ?? string.Empty,
        };
        if (!setting.Yapilandirildi) return services;

        // Üretimde SANDBOX anahtarıyla çalışmak = hiç para tahsil etmemek, üstelik ekranda "ödendi"
        // görmek. Açılışı reddetmek yerine gürültülü uyarı: staging ortamları bilinçli olarak
        // sandbox kullanır ve onları kilitlemek istemiyoruz.
        if (!developmentEnvironment && setting.ApiKey.StartsWith("sandbox-", StringComparison.OrdinalIgnoreCase))
            log?.LogWarning("iyzico SANDBOX anahtarıyla çalışıyor ({Ortam} ortamı) — gerçek tahsilat YAPILMAZ.",
                developmentEnvironment ? "Development" : "üretim/staging");

        services.AddHttpClient();
        services.AddSingleton(setting);
        services.AddSingleton<IPosService, IyzicoPosService>();
        return services;
    }
}
