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
    IHttpClientFactory httpFactory, IyzicoAyar ayar, ILogger<IyzicoPosService> log) : IPosService
{
    private const string UcPreauth = "/payment/iyzipos/checkoutform/initialize/preauth/ecom";
    private const string UcAuth = "/payment/iyzipos/checkoutform/initialize/auth/ecom";
    private const string UcSonuc = "/payment/iyzipos/checkoutform/auth/ecom/detail";
    private const string UcKapat = "/payment/postauth";
    private const string UcIptal = "/payment/cancel";
    private const string UcIade = "/payment/refund";

    public async Task<PosBaslatSonuc> BaslatAsync(PosOdemeIstegi istek, CancellationToken ct = default)
    {
        if (!ayar.Yapilandirildi)
            return new PosBaslatSonuc(false, null, null, "Ödeme sağlayıcısı yapılandırılmadı.");
        if (istek.Tutar <= 0)
            return new PosBaslatSonuc(false, null, null, "Ödeme tutarı sıfırdan büyük olmalıdır.");

        var tutar = Tutar(istek.Tutar);
        var govde = new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = istek.Referans,
            ["price"] = tutar,
            // paidPrice = price: komisyon/vade farkı BİNDİRİLMEZ. Farklı olsaydı müşteriden çekilen
            // tutarla sözleşmedeki tutar ayrışır, mutabakat bozulurdu.
            ["paidPrice"] = tutar,
            ["currency"] = istek.ParaBirimi,
            ["basketId"] = istek.Referans,
            ["paymentGroup"] = "PRODUCT",
            ["callbackUrl"] = istek.DonusUrl,
            ["enabledInstallments"] = new[] { 1 }, // taksit KAPALI: kira bedeli taksitlendirilmiyor
            ["buyer"] = new Dictionary<string, object?>
            {
                ["id"] = istek.Alici.Id,
                ["name"] = istek.Alici.Ad,
                ["surname"] = istek.Alici.Soyad,
                ["gsmNumber"] = istek.Alici.Telefon,
                ["email"] = istek.Alici.Eposta,
                ["identityNumber"] = istek.Alici.KimlikNo,
                ["registrationAddress"] = istek.Alici.Adres,
                ["ip"] = istek.Alici.Ip,
                ["city"] = istek.Alici.Sehir,
                ["country"] = istek.Alici.Ulke,
            },
            ["shippingAddress"] = Adres(istek.Alici),
            ["billingAddress"] = Adres(istek.Alici),
            // Sepet toplamı price'a EŞİT olmak zorunda (sağlayıcı doğrular) → tek kalem.
            ["basketItems"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = istek.Referans,
                    ["name"] = Kisalt(istek.Aciklama, 100),
                    ["category1"] = "Arac Kiralama",
                    ["itemType"] = "VIRTUAL", // araç kiralama fiziksel teslimat değil
                    ["price"] = tutar,
                },
            },
        };

        var (ok, kok, hata) = await IstekAsync(istek.Provizyon ? UcPreauth : UcAuth, govde, ct);
        if (!ok) return new PosBaslatSonuc(false, null, null, hata);

        var token = Metin(kok, "token");
        var url = Metin(kok, "paymentPageUrl");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(url))
            return new PosBaslatSonuc(false, null, null, "Sağlayıcı ödeme sayfası bilgisi döndürmedi.");
        return new PosBaslatSonuc(true, token, url, null);
    }

    public async Task<PosDurumSonuc> SonucAsync(string token, CancellationToken ct = default)
    {
        if (!ayar.Yapilandirildi)
            return new PosDurumSonuc(false, null, null, null, null, null, null, "Ödeme sağlayıcısı yapılandırılmadı.");
        if (string.IsNullOrWhiteSpace(token))
            return new PosDurumSonuc(false, null, null, null, null, null, null, "Ödeme jetonu boş.");

        var (ok, kok, hata) = await IstekAsync(UcSonuc,
            new Dictionary<string, object?> { ["locale"] = "tr", ["conversationId"] = "sonuc", ["token"] = token }, ct);
        if (!ok) return new PosDurumSonuc(false, null, null, null, null, null, null, hata);

        // paymentStatus SUCCESS değilse ödeme TAMAMLANMAMIŞTIR (kart reddi, 3DS başarısız, vazgeçme).
        // status=success yalnız "sorgu başarılı" demektir — ikisini karıştırmak, ödenmemiş bir
        // kiralamayı ödenmiş saymak olurdu.
        var odemeDurum = Metin(kok, "paymentStatus");
        var basarili = string.Equals(odemeDurum, "SUCCESS", StringComparison.OrdinalIgnoreCase);
        var odemeId = Metin(kok, "paymentId");
        var islemId = IlkIslemId(kok);
        var tutar = Ondalik(kok, "paidPrice");
        var kart = KartOzet(kok);
        var referans = Metin(kok, "conversationId");

        if (!basarili)
        {
            var mesaj = Metin(kok, "errorMessage")
                        ?? $"Ödeme tamamlanmadı (durum: {odemeDurum ?? "bilinmiyor"}).";
            return new PosDurumSonuc(false, odemeId, islemId, odemeDurum, tutar, kart, referans, mesaj);
        }
        return new PosDurumSonuc(true, odemeId, islemId, odemeDurum, tutar, kart, referans, null);
    }

    public Task<PosResult> KapatAsync(string odemeId, decimal tutar, string ip, CancellationToken ct = default)
        => BasitAsync(UcKapat, new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = "kapat-" + odemeId,
            ["paymentId"] = odemeId,
            ["paidPrice"] = Tutar(tutar),
            ["ip"] = ip,
        }, "paymentId", ct);

    public Task<PosResult> IptalAsync(string odemeId, string ip, CancellationToken ct = default)
        => BasitAsync(UcIptal, new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = "iptal-" + odemeId,
            ["paymentId"] = odemeId,
            ["ip"] = ip,
        }, "paymentId", ct);

    public Task<PosResult> IadeAsync(string islemId, decimal tutar, string ip, CancellationToken ct = default)
        => BasitAsync(UcIade, new Dictionary<string, object?>
        {
            ["locale"] = "tr",
            ["conversationId"] = "iade-" + islemId,
            ["paymentTransactionId"] = islemId,
            ["price"] = Tutar(tutar),
            ["ip"] = ip,
        }, "paymentId", ct);

    // ---- ortak ----

    private async Task<PosResult> BasitAsync(
        string uri, Dictionary<string, object?> govde, string refAlan, CancellationToken ct)
    {
        if (!ayar.Yapilandirildi) return new PosResult(false, null, "Ödeme sağlayıcısı yapılandırılmadı.");
        var (ok, kok, hata) = await IstekAsync(uri, govde, ct);
        return ok ? new PosResult(true, Metin(kok, refAlan), null) : new PosResult(false, null, hata);
    }

    /// <summary>İsteği gönderir; <c>status=="success"</c> değilse sağlayıcının Türkçe hatasını taşır.</summary>
    private async Task<(bool Ok, JsonElement Kok, string? Hata)> IstekAsync(
        string uri, Dictionary<string, object?> govde, CancellationToken ct)
    {
        // Gövde TEK KEZ serileştirilir: imza ile gönderilen metin BİREBİR aynı olmak zorunda.
        var metin = JsonSerializer.Serialize(govde);
        var (auth, rnd) = IyzicoImza.Uret(ayar.ApiKey, ayar.SecretKey, uri, metin);

        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var req = new HttpRequestMessage(HttpMethod.Post, ayar.BaseUrl.TrimEnd('/') + uri)
            {
                Content = new StringContent(metin, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("Authorization", auth);
            req.Headers.TryAddWithoutValidation("x-iyzi-rnd", rnd);

            var resp = await http.SendAsync(req, ct);
            var govdeMetni = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("iyzico {Uri} HTTP {Kod}: {Govde}", uri, (int)resp.StatusCode, govdeMetni);
                return (false, default, $"Ödeme sağlayıcısına ulaşılamadı (HTTP {(int)resp.StatusCode}).");
            }

            using var doc = JsonDocument.Parse(govdeMetni);
            var kok = doc.RootElement.Clone(); // doc dispose olduktan sonra da okunabilsin
            if (!string.Equals(Metin(kok, "status"), "success", StringComparison.OrdinalIgnoreCase))
            {
                var kod = Metin(kok, "errorCode");
                var mesaj = Metin(kok, "errorMessage") ?? "Ödeme sağlayıcısı isteği reddetti.";
                log.LogWarning("iyzico {Uri} reddetti: {Kod} {Mesaj}", uri, kod, mesaj);
                return (false, kok, kod is null ? mesaj : $"{mesaj} (kod {kod})");
            }
            return (true, kok, null);
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

    private static Dictionary<string, object?> Adres(PosAlici a) => new()
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
    public static string Tutar(decimal deger)
        => deger.ToString("0.0#####", CultureInfo.InvariantCulture);

    private static string Kisalt(string? s, int n)
        => string.IsNullOrWhiteSpace(s) ? "Arac kiralama" : (s.Length <= n ? s : s[..n]);

    private static string? Metin(JsonElement kok, string alan)
        => kok.ValueKind == JsonValueKind.Object && kok.TryGetProperty(alan, out var v)
           && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            : null;

    private static decimal? Ondalik(JsonElement kok, string alan)
    {
        var m = Metin(kok, alan);
        return decimal.TryParse(m, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>İade ucu ödeme kimliğini değil KALEM işlem kimliğini ister — ilk kalemden okunur.</summary>
    private static string? IlkIslemId(JsonElement kok)
    {
        if (kok.ValueKind != JsonValueKind.Object
            || !kok.TryGetProperty("itemTransactions", out var kalemler)
            || kalemler.ValueKind != JsonValueKind.Array) return null;
        foreach (var k in kalemler.EnumerateArray())
            if (Metin(k, "paymentTransactionId") is { Length: > 0 } id) return id;
        return null;
    }

    /// <summary>Kartın son 4 hanesi + ailesi — makbuzda/ekranda gösterilir (tam numara ASLA tutulmaz).</summary>
    private static string? KartOzet(JsonElement kok)
    {
        var son4 = Metin(kok, "lastFourDigits");
        var aile = Metin(kok, "cardAssociation");
        if (son4 is null && aile is null) return null;
        return string.Join(' ', new[] { aile, son4 is null ? null : "**** " + son4 }.Where(x => x is not null));
    }
}

/// <summary>
/// iyzico kurulumu — Web ve PublicSite AYNI çağrıyı kullanır (ikisi de ödeme başlatabiliyor:
/// ERP'de depozito provizyonu, halka açık sitede online rezervasyon).
/// </summary>
public static class IyzicoKurulum
{
    /// <summary>
    /// Config'te <c>Iyzico:ApiKey</c> + <c>Iyzico:SecretKey</c> VARSA gerçek adaptörü kaydeder
    /// (stub'ı override eder); yoksa hiçbir şey yapmaz ve stub dürüstçe "yapılandırılmadı" döner.
    /// </summary>
    public static IServiceCollection AddIyzico(
        this IServiceCollection services, IConfiguration config, bool gelistirmeOrtami, ILogger? log = null)
    {
        var ayar = new IyzicoAyar
        {
            BaseUrl = config["Iyzico:BaseUrl"] ?? "https://sandbox-api.iyzipay.com",
            ApiKey = config["Iyzico:ApiKey"] ?? string.Empty,
            SecretKey = config["Iyzico:SecretKey"] ?? string.Empty,
        };
        if (!ayar.Yapilandirildi) return services;

        // Üretimde SANDBOX anahtarıyla çalışmak = hiç para tahsil etmemek, üstelik ekranda "ödendi"
        // görmek. Açılışı reddetmek yerine gürültülü uyarı: staging ortamları bilinçli olarak
        // sandbox kullanır ve onları kilitlemek istemiyoruz.
        if (!gelistirmeOrtami && ayar.ApiKey.StartsWith("sandbox-", StringComparison.OrdinalIgnoreCase))
            log?.LogWarning("iyzico SANDBOX anahtarıyla çalışıyor ({Ortam} ortamı) — gerçek tahsilat YAPILMAZ.",
                gelistirmeOrtami ? "Development" : "üretim/staging");

        services.AddHttpClient();
        services.AddSingleton(ayar);
        services.AddSingleton<IPosService, IyzicoPosService>();
        return services;
    }
}
