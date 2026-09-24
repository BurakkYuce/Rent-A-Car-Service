using System.Text.Json;
using RentACar.Web.Api.Sistem;

namespace RentACar.IntegrationTests;

/// <summary>F11.1b güvenlik L1 — denetim yanıtı maskesi: sır + PII anahtarları, iç içe nesne/dizi, harf duyarsız.</summary>
public sealed class AuditMaskTests
{
    [Fact]
    public void Secrets_and_pii_are_masked_recursively_and_case_insensitively()
    {
        const string json = """
            {"SmtpSifreEnc":"c1","passwordhash":"h1","CalendarTOKEN":"t1","TcKimlik":"12345678901","MaasEnc":"m",
             "Musteri":{"EhliyetNo":"E1","Ad":"Ece","Kartlar":[{"Iban":"TR00","Not":"ok"}]},"PasaportNo":null,"Tutar":10}
            """;
        var masked = SystemAdminApi.MaskSecrets(json)!;
        foreach (var leaked in new[] { "c1", "h1", "t1", "12345678901", "E1", "TR00" })
            Assert.DoesNotContain(leaked, masked, StringComparison.Ordinal);

        var root = JsonDocument.Parse(masked).RootElement;
        Assert.Equal("Ece", root.GetProperty("Musteri").GetProperty("Ad").GetString());
        Assert.Equal("ok", root.GetProperty("Musteri").GetProperty("Kartlar")[0].GetProperty("Not").GetString());
        Assert.Equal(10, root.GetProperty("Tutar").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("PasaportNo").ValueKind); // boş değer "***" değil, null kalır
    }

    [Fact]
    public void Tax_and_id_number_keys_and_json_embedded_in_strings_are_masked()
    {
        // 3. tur LOW-2: TcNo/KimlikNo/VergiNo + dize içine serileştirilmiş JSON (ör. jsonb kolon metni).
        const string json = """
            {"TcNo":"11111111111","KimlikNo":"22222222222","VergiNo":"3333333333",
             "Ekler":"{\"SmtpSifreEnc\":\"cipher-x\",\"Ad\":\"Ece\"}","Liste":["[{\"Iban\":\"TR99\"}]"],"Not":"{ düz metin"}
            """;
        var masked = SystemAdminApi.MaskSecrets(json)!;
        foreach (var leaked in new[] { "11111111111", "22222222222", "3333333333", "cipher-x", "TR99" })
            Assert.DoesNotContain(leaked, masked, StringComparison.Ordinal);
        var root = JsonDocument.Parse(masked).RootElement;
        Assert.Contains("Ece", root.GetProperty("Ekler").GetString(), StringComparison.Ordinal);
        Assert.Equal("{ düz metin", root.GetProperty("Not").GetString());
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("bozuk{")]
    [InlineData("")]
    public void Non_object_or_invalid_json_returns_null(string json) => Assert.Null(SystemAdminApi.MaskSecrets(json));
}
