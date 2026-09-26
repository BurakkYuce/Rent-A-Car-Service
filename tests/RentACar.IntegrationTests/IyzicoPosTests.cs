using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Application.Integrations;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>İsteği yakalayıp sabit yanıt döndüren HTTP katmanı (ağa çıkılmaz).</summary>
internal sealed class YakalayanHandler(string yanit, HttpStatusCode kod = HttpStatusCode.OK) : HttpMessageHandler
{
    public HttpRequestMessage? Istek { get; private set; }
    public string? Govde { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Istek = request;
        Govde = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new HttpResponseMessage(kod) { Content = new StringContent(yanit) };
    }
}

internal sealed class TekHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// iyzico adaptörü — ağa çıkmadan doğrulanabilen davranışlar.
///
/// <para>BAĞIMSIZ ORACLE: beklenen istek gövdesi ve başlıklar iyzico'nun sözleşmesinden
/// (dokümantasyon + sandbox'a karşı yapılan ampirik uç taraması) türetildi, adaptörün kendi
/// mantığından değil. Canlı sandbox doğrulaması ayrı bir sınıftadır
/// (<c>IyzicoCanliSandboxTests</c> — kimlik yoksa atlanır).</para>
/// </summary>
public sealed class IyzicoPosTests
{
    private static readonly IyzicoAyar Setting = new()
    {
        BaseUrl = "https://sandbox-api.iyzipay.com",
        ApiKey = "test-api-key",
        SecretKey = "test-secret-key",
    };

    private static (IyzicoPosService Servis, YakalayanHandler Handler) Setup(
        string response, HttpStatusCode code = HttpStatusCode.OK)
    {
        var h = new YakalayanHandler(response, code);
        return (new IyzicoPosService(new TekHandlerFactory(h), Setting, NullLogger<IyzicoPosService>.Instance), h);
    }

    private static PosOdemeIstegi Request(bool preAuth = true, decimal amount = 1500m) => new(
        amount, "TRY", "RZ-000123", "https://firma.local/odeme/donus",
        new PosAlici("MUS-1", "Ahmet", "Yılmaz", "ahmet@ornek.com", "+905350000000",
            "74300864791", "Kadıköy Moda Cad. No:1", "İstanbul", "Turkey", "85.34.78.112"),
        "Depozito bloke", preAuth);

    private const string SuccessfulStart =
        """{"status":"success","token":"tok-123","paymentPageUrl":"https://sandbox-cpp.iyzipay.com?token=tok-123"}""";

    [Fact]
    public async Task Provizyon_preauth_ucuna_gider_tahsilat_auth_ucuna()
    {
        var (svc, h) = Setup(SuccessfulStart);
        await svc.StartAsync(Request(preAuth: true));
        Assert.EndsWith("/payment/iyzipos/checkoutform/initialize/preauth/ecom", h.Istek!.RequestUri!.AbsolutePath);

        var (svc2, h2) = Setup(SuccessfulStart);
        await svc2.StartAsync(Request(preAuth: false));
        Assert.EndsWith("/payment/iyzipos/checkoutform/initialize/auth/ecom", h2.Istek!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Imza_basliklari_gonderilir()
    {
        var (svc, h) = Setup(SuccessfulStart);
        await svc.StartAsync(Request());

        var auth = Assert.Single(h.Istek!.Headers.GetValues("Authorization"));
        Assert.StartsWith("IYZWSv2 ", auth);
        Assert.Single(h.Istek.Headers.GetValues("x-iyzi-rnd"));

        // İmza, GÖNDERİLEN gövdenin birebir metni üzerinden hesaplanmalı. Yeniden serileştirme
        // (alan sırası/boşluk farkı) "Geçersiz imza" üretirdi — bu testin varlık sebebi bu.
        var rnd = h.Istek.Headers.GetValues("x-iyzi-rnd").Single();
        var path = h.Istek.RequestUri!.AbsolutePath;
        var expected = IyzicoSignature.Generate(Setting.ApiKey, Setting.SecretKey, path, h.Govde!, rnd).Authorization;
        Assert.Equal(expected, auth);
    }

    [Fact]
    public async Task Kart_verisi_isteğe_HİÇ_konmaz()
    {
        // Barındırılan sayfa modelinin tek sebebi bu: kart bize uğramaz (PCI kapsamı dışı).
        var (svc, h) = Setup(SuccessfulStart);
        await svc.StartAsync(Request());

        foreach (var alan in new[] { "cardNumber", "cvc", "expireYear", "expireMonth", "paymentCard" })
            Assert.DoesNotContain(alan, h.Govde, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sepet_toplami_tutara_esit_ve_taksit_kapali()
    {
        var (svc, h) = Setup(SuccessfulStart);
        await svc.StartAsync(Request(amount: 1500m));

        using var doc = JsonDocument.Parse(h.Govde!);
        var root = doc.RootElement;
        Assert.Equal("1500.0", root.GetProperty("price").GetString());
        Assert.Equal("1500.0", root.GetProperty("paidPrice").GetString());   // vade farkı bindirilmez
        var items = root.GetProperty("basketItems").EnumerateArray().ToList();
        Assert.Equal("1500.0", Assert.Single(items).GetProperty("price").GetString());
        Assert.Equal(1, root.GetProperty("enabledInstallments").EnumerateArray().Single().GetInt32());
    }

    [Theory]
    [InlineData(1500, "1500.0")]
    [InlineData(0.5, "0.5")]
    [InlineData(1234.56, "1234.56")]
    [InlineData(100.10, "100.1")]
    public void Tutar_bicimi_nokta_ondalik_ve_kulturden_bagimsiz(decimal value, string expected)
    {
        // Türkçe kültürde ToString() virgül üretir ve sağlayıcı isteği reddeder.
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal(expected, IyzicoPosService.Amount(value));
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    [Fact]
    public async Task Saglayici_reddederse_hata_mesaji_ve_kodu_tasinir()
    {
        var (svc, _) = Setup("""{"status":"failure","errorCode":"5057","errorMessage":"basketItemType geçersizdir"}""");
        var result = await svc.StartAsync(Request());

        Assert.False(result.Ok);
        Assert.Null(result.Token);
        Assert.Contains("basketItemType", result.Hata);
        Assert.Contains("5057", result.Hata);
    }

    [Fact]
    public async Task Sonuc_sorgusu_odeme_basarisizsa_BASARILI_saymaz()
    {
        // KRİTİK: status=success yalnız "sorgu başarılı" demektir. paymentStatus SUCCESS değilse
        // ödeme tamamlanmamıştır — ikisini karıştırmak ödenmemiş kiralamayı ödenmiş saymak olurdu.
        var (svc, _) = Setup(
            """{"status":"success","paymentStatus":"FAILURE","paymentId":"99","errorMessage":"Kart limiti yetersiz"}""");
        var result = await svc.ResultAsync("tok-1");

        Assert.False(result.Ok);
        Assert.Equal("FAILURE", result.Durum);
        Assert.Equal("99", result.OdemeId);         // teşhis için ödeme kimliği yine taşınır
        Assert.Contains("limiti", result.Hata);
    }

    [Fact]
    public async Task Sonuc_sorgusu_basarilida_odeme_ve_islem_kimligini_cikarir()
    {
        var (svc, _) = Setup("""
        {"status":"success","paymentStatus":"SUCCESS","paymentId":"12345","paidPrice":"1500.0",
         "conversationId":"RZ-000123","lastFourDigits":"0008","cardAssociation":"MASTER_CARD",
         "itemTransactions":[{"paymentTransactionId":"77777"}]}
        """);
        var result = await svc.ResultAsync("tok-1");

        Assert.True(result.Ok);
        Assert.Equal("12345", result.OdemeId);
        Assert.Equal("77777", result.IslemId);   // iade bunu ister, paymentId'yi DEĞİL
        Assert.Equal(1500.0m, result.Tutar);
        Assert.Equal("RZ-000123", result.Referans);
        Assert.Contains("0008", result.KartOzet);
        Assert.DoesNotContain("5528", result.KartOzet ?? ""); // tam kart numarası ASLA taşınmaz
    }

    [Fact]
    public async Task Kapatma_iptal_iade_dogru_uclara_ve_alanlarla_gider()
    {
        var (k, hk) = Setup("""{"status":"success","paymentId":"12345"}""");
        await k.CloseAsync("12345", 1200m, "85.34.78.112");
        Assert.EndsWith("/payment/postauth", hk.Istek!.RequestUri!.AbsolutePath);
        Assert.Contains("\"paidPrice\":\"1200.0\"", hk.Govde);

        var (i, hi) = Setup("""{"status":"success","paymentId":"12345"}""");
        await i.CancelAsync("12345", "85.34.78.112");
        Assert.EndsWith("/payment/cancel", hi.Istek!.RequestUri!.AbsolutePath);

        var (d, hd) = Setup("""{"status":"success","paymentId":"12345"}""");
        await d.RefundAsync("77777", 500m, "85.34.78.112");
        Assert.EndsWith("/payment/refund", hd.Istek!.RequestUri!.AbsolutePath);
        // İade KALEM işlem kimliğini ister; paymentId göndermek "kırılım kaydı bulunamadı" verirdi.
        Assert.Contains("\"paymentTransactionId\":\"77777\"", hd.Govde);
    }

    [Fact]
    public async Task Http_hatasi_ve_bozuk_yanit_istisna_sizdirmaz()
    {
        var (svc, _) = Setup("{}", HttpStatusCode.BadGateway);
        var result = await svc.StartAsync(Request());
        Assert.False(result.Ok);
        Assert.Contains("502", result.Hata);

        var (svc2, _) = Setup("bu json değil");
        var result2 = await svc2.StartAsync(Request());
        Assert.False(result2.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result2.Hata));
    }

    [Fact]
    public async Task Sifir_veya_negatif_tutar_aga_cikmadan_reddedilir()
    {
        var (svc, h) = Setup(SuccessfulStart);
        foreach (var amount in new[] { 0m, -5m })
        {
            var result = await svc.StartAsync(Request(amount: amount));
            Assert.False(result.Ok);
        }
        Assert.Null(h.Istek); // hiç istek gitmedi
    }

    [Fact]
    public async Task Yapilandirilmamis_ayar_aga_cikmadan_reddeder()
    {
        var h = new YakalayanHandler(SuccessfulStart);
        var svc = new IyzicoPosService(new TekHandlerFactory(h),
            new IyzicoAyar { ApiKey = "", SecretKey = "" }, NullLogger<IyzicoPosService>.Instance);

        Assert.False((await svc.StartAsync(Request())).Ok);
        Assert.False((await svc.ResultAsync("t")).Ok);
        Assert.False((await svc.CloseAsync("1", 1m, "1.2.3.4")).Success);
        Assert.Null(h.Istek);
    }
}
