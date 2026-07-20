using RentACar.Web.Observability;

namespace RentACar.IntegrationTests;

/// <summary>
/// Grafana alarm köprüsü (AlertWebhook) — gizli-anahtar kapısı + özet çıkarımı. Saf mantık birim testi.
/// </summary>
public sealed class AlertWebhookTests
{
    // ---- Anahtar kapısı: config boşsa/uyuşmazsa yetki YOK; birebir eşleşmede VAR ----
    [Theory]
    [InlineData("s3cr3t", "s3cr3t", true)]
    [InlineData("s3cr3t", "yanlis", false)]
    [InlineData("s3cr3t", "", false)]      // provided boş
    [InlineData("", "s3cr3t", false)]      // configured boş → uç kapalı
    [InlineData(null, "s3cr3t", false)]
    [InlineData("s3cr3t", null, false)]
    [InlineData("s3cr3t", "s3cr3", false)] // uzunluk farkı
    public void Anahtar_kapisi(string? configured, string? provided, bool beklenen)
        => Assert.Equal(beklenen, AlertWebhook.Authorized(provided, configured));

    // ---- Anahtar çıkarımı: X-Alert-Token öncelikli; yoksa Authorization: Bearer ----
    [Theory]
    [InlineData("tok1", null, "tok1")]                       // X-Alert-Token
    [InlineData("", "Bearer tok2", "tok2")]                  // Authorization Bearer
    [InlineData(null, "bearer tok3", "tok3")]                // case-insensitive
    [InlineData(null, "Basic abc", null)]                    // Bearer değil → yok
    [InlineData("", null, null)]                             // ikisi de yok
    public void Anahtar_cikarimi(string? xToken, string? authz, string? beklenen)
        => Assert.Equal(beklenen, AlertWebhook.ExtractToken(xToken, authz));

    // ---- Özet: Grafana alerts[] gövdesinden alertname+summary+status ----
    [Fact]
    public void Ozet_grafana_alerts_govdesinden()
    {
        var json = """
        {"alerts":[{"status":"firing","labels":{"alertname":"HighErrorRate"},
          "annotations":{"summary":"5xx oranı %10 üstünde"}}]}
        """;
        var s = AlertWebhook.Summarize(json);
        Assert.Contains("firing", s);
        Assert.Contains("HighErrorRate", s);
        Assert.Contains("5xx", s);
    }

    // ---- Özet: title/message gövdesi ----
    [Fact]
    public void Ozet_title_message_govdesinden()
    {
        var s = AlertWebhook.Summarize("""{"title":"DB down","message":"readiness kırmızı"}""");
        Assert.Contains("DB down", s);
        Assert.Contains("readiness kırmızı", s);
    }

    // ---- Özet: geçersiz JSON → ham metin (çökME), boş → etiket ----
    [Fact]
    public void Ozet_gecersiz_ve_bos()
    {
        Assert.Equal("(boş alarm gövdesi)", AlertWebhook.Summarize(""));
        Assert.Contains("ham degil json", AlertWebhook.Summarize("ham degil json")); // JsonException → ham'a düşer
    }

    // ---- Özet 400 char'da kırpılır ----
    [Fact]
    public void Ozet_uzun_metin_kirpilir()
    {
        var s = AlertWebhook.Summarize(new string('x', 1000));
        Assert.True(s.Length <= 401);
        Assert.EndsWith("…", s);
    }
}
