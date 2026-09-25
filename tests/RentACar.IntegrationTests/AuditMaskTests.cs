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

    [Fact]
    public void Vkn_and_tckn_keys_are_masked_but_bare_tc_prefix_is_not()
    {
        // 4. tur: GelenEFatura.GonderenVkn şahıs firmasında TCKN taşır. "Tc" tek başına maskelenmez (çok geniş).
        const string json = """{"GonderenVkn":"12345678901","AliciTckn":"10987654321","Tcsayac":5}""";
        var masked = SystemAdminApi.MaskSecrets(json)!;
        Assert.DoesNotContain("12345678901", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("10987654321", masked, StringComparison.Ordinal);
        Assert.Equal(5, JsonDocument.Parse(masked).RootElement.GetProperty("Tcsayac").GetInt32());
    }

    [Fact]
    public void Identity_document_numbers_are_masked_but_similar_non_pii_keys_are_not()
    {
        // #319 incelemesi L1: sürücü belgesi ve nüfus cüzdanı alanları (Personel, Customer) denetime düz yazılıyordu.
        const string json = """
            {"SurucuBelgeNo":"SB998877","SeriNo":"S1234","CiltNo":"C55","AileSira":"A7","AileSiraNo":"A8","SiraNo":"N9",
             "TaksitSiraNo":3,"BelgeNo":"RUHSAT-1","Seri":"RNT"}
            """;
        var masked = SystemAdminApi.MaskSecrets(json)!;
        foreach (var secret in new[] { "SB998877", "S1234", "C55", "A7", "A8", "N9" })
            Assert.DoesNotContain(secret, masked, StringComparison.Ordinal);

        // Tam anahtar eşleşmesi: benzer adlı kişisel olmayan alanlar görünür kalır.
        var root = JsonDocument.Parse(masked).RootElement;
        Assert.Equal(3, root.GetProperty("TaksitSiraNo").GetInt32());
        Assert.Equal("RUHSAT-1", root.GetProperty("BelgeNo").GetString());
        Assert.Equal("RNT", root.GetProperty("Seri").GetString());
    }

    [Fact]
    public void Company_iban_and_vkn_show_last_four_only_for_allowlisted_table()
    {
        // #319 L2 kullanıcı kararı: firmanın kendi IBAN'ı (Hesaplar.Iban) ve VKN'si (Ayarlar.FirmaVergiNo) son 4 görünür.
        // Sentetik değerler çalışma anında kurulur (gerçek IBAN yok). Boşluklu yazım da aynı sonucu verir.
        var iban = "TR00 " + string.Join(" ", Enumerable.Repeat("0000", 5)) + " 1234"; // TR00 + 20 sıfır + 1234 = 26 hane
        var json = "{\"Iban\":\"" + iban + "\",\"Ad\":\"Ana Banka\"}";
        var root = JsonDocument.Parse(SystemAdminApi.MaskSecrets(json, "Hesaplar")!).RootElement;
        Assert.Equal("********1234", root.GetProperty("Iban").GetString());
        Assert.Equal("Ana Banka", root.GetProperty("Ad").GetString());

        var vkn = JsonDocument.Parse(SystemAdminApi.MaskSecrets("""{"FirmaVergiNo":"0000009876"}""", "ayarlar")!).RootElement;
        Assert.Equal("********9876", vkn.GetProperty("FirmaVergiNo").GetString());

        // Tablo verilmezse ya da başka tabloysa TAM maske (anahtar adı tek başına yetmez).
        Assert.Equal("***", JsonDocument.Parse(SystemAdminApi.MaskSecrets(json)!).RootElement.GetProperty("Iban").GetString());
        Assert.Equal("***", JsonDocument.Parse(SystemAdminApi.MaskSecrets(json, "Customers")!).RootElement.GetProperty("Iban").GetString());
    }

    [Fact]
    public void Partial_mask_is_idempotent_and_short_values_stay_fully_masked()
    {
        // Yazma yolunun ürettiği kısmi maske okuma yüzeyinde yeniden maskelenince AYNI kalır.
        var again = JsonDocument.Parse(SystemAdminApi.MaskSecrets("""{"Iban":"********1234"}""", "Hesaplar")!).RootElement;
        Assert.Equal("********1234", again.GetProperty("Iban").GetString());
        // Boşluksuz 8 karakterden kısa değer TAM maske; eski "***" kayıtları da "***" kalır.
        var shortRoot = JsonDocument.Parse(SystemAdminApi.MaskSecrets("""{"Iban":"TR 12 345"}""", "Hesaplar")!).RootElement;
        Assert.Equal("***", shortRoot.GetProperty("Iban").GetString());
        Assert.Equal("***", JsonDocument.Parse(SystemAdminApi.MaskSecrets("""{"Iban":"***"}""", "Hesaplar")!).RootElement.GetProperty("Iban").GetString());
    }

    [Fact]
    public void Customer_personnel_pii_and_secrets_stay_fully_masked_even_on_allowlisted_tables()
    {
        // Kısmi maske yalnız iki (tablo, anahtar) çiftine; müşteri/personel PII ve sırlar hiçbir tabloda gevşemez.
        const string customer = """
            {"TcKimlik":"10000000146","VergiNo":"0000001234","BankaIban":"TR000000000000000000005555","EhliyetNo":"EH12345678",
             "PasaportNo":"PP12345678","SeriNo":"A12B345678","MaasEnc":"CfDJ8-maas-cipher"}
            """;
        var c = JsonDocument.Parse(SystemAdminApi.MaskSecrets(customer, "Customers")!).RootElement;
        foreach (var k in new[] { "TcKimlik", "VergiNo", "BankaIban", "EhliyetNo", "PasaportNo", "SeriNo", "MaasEnc" })
            Assert.Equal("***", c.GetProperty(k).GetString());

        // Sır değerleri çalışma anında üretilir (GitGuardian: sabit "sır benzeri" dize yok).
        var smtp = new string('s', 24);
        var sms = new string('m', 24);
        var settings = $$$"""
            {"SmtpSifreEnc":"{{{smtp}}}","SmsApiKeyEnc":"{{{sms}}}","VergiNo":"0000001234",
             "Alt":{"Iban":"TR000000000000000000007777","FirmaVergiNo":"0000004321"}}
            """;
        var s = JsonDocument.Parse(SystemAdminApi.MaskSecrets(settings, "Ayarlar")!).RootElement;
        Assert.Equal("***", s.GetProperty("SmtpSifreEnc").GetString());
        Assert.Equal("***", s.GetProperty("SmsApiKeyEnc").GetString());
        Assert.Equal("***", s.GetProperty("VergiNo").GetString());
        // İç içe nesnede izin listesi uygulanmaz.
        Assert.Equal("***", s.GetProperty("Alt").GetProperty("Iban").GetString());
        Assert.Equal("***", s.GetProperty("Alt").GetProperty("FirmaVergiNo").GetString());
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("bozuk{")]
    [InlineData("")]
    public void Non_object_or_invalid_json_returns_null(string json) => Assert.Null(SystemAdminApi.MaskSecrets(json));
}
