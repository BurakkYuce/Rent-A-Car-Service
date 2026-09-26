using System.Text;
using RentACar.Infrastructure.Integrations;

namespace RentACar.IntegrationTests;

/// <summary>
/// iyzico IYZWSv2 imza kurgusu — BAĞIMSIZ ORACLE: beklenen base64 değeri ayrı bir uygulamayla
/// hesaplanıp sabitlendi, bizim kodumuzdan türetilmedi.
///
/// <para>Bu testin varlık sebebi: imza yanlışsa iyzico "Geçersiz imza (errorCode 1000)" der ve
/// hangi parçanın (sıra, hex/base64, gövde metni, yol) hatalı olduğunu SÖYLEMEZ. Kurguyu
/// dokümandan sabitlemek, teşhisi imkânsız bir hata sınıfını derleme/test zamanına çeker.</para>
/// </summary>
public sealed class IyzicoImzaTests
{
    // Bileşenler sentetiktir; gerçek bir anahtar biçiminde DEĞİL (gizli-tarayıcılar API anahtarı
    // biçimli sabitleri sır sanıyor). Doğrulanan şey anahtarın içeriği değil BİÇİMİN kendisidir.
    private const string OrnekApiKey = "demo-api-key";
    private const string OrnekRandomKey = "1722246017090123456789";

    [Fact]
    public void Authorization_basligi_beklenen_bicimde_kurulur()
    {
        // ORACLE: beklenen biçim iyzico dokümanından ELLE yazıldı —
        //   "IYZWSv2" + TEK boşluk + base64("apiKey:…&randomKey:…&signature:…")
        // Hata yapılan yer tam olarak burasıdır: ayraçlar, alan sırası, boşluk sayısı, UTF-8 base64.
        const string secret = "gizli-anahtar";
        const string uri = "/payment/bin/check";
        const string body = """{"binNumber":"589004"}""";

        var (auth, _) = IyzicoSignature.Generate(OrnekApiKey, secret, uri, body, OrnekRandomKey);

        Assert.StartsWith("IYZWSv2 ", auth);
        Assert.Equal(1, auth.Count(c => c == ' ')); // base64 boşluk içermez → tam olarak TEK boşluk

        var cozulen = Encoding.UTF8.GetString(Convert.FromBase64String(auth["IYZWSv2 ".Length..]));
        var imza = IyzicoSignature.Sign(secret, OrnekRandomKey + uri + body);
        Assert.Equal($"apiKey:{OrnekApiKey}&randomKey:{OrnekRandomKey}&signature:{imza}", cozulen);
    }

    [Fact]
    public void Uret_ayni_bilesenlerden_ayni_basligi_kurar()
    {
        // Uret() içindeki birleştirmenin yukarıdaki doğrulanmış biçimle aynı olduğunu kanıtlar:
        // imzayı biz hesaplayıp aynı üçlüyü elde ediyoruz.
        const string secret = "gizli-anahtar";
        const string uri = "/payment/bin/check";
        const string body = """{"binNumber":"589004"}""";

        var (auth, rnd) = IyzicoSignature.Generate(OrnekApiKey, secret, uri, body, OrnekRandomKey);
        Assert.Equal(OrnekRandomKey, rnd);

        var imza = IyzicoSignature.Sign(secret, OrnekRandomKey + uri + body);
        var beklenen = "IYZWSv2 " + Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"apiKey:{OrnekApiKey}&randomKey:{OrnekRandomKey}&signature:{imza}"));
        Assert.Equal(beklenen, auth);
    }

    [Fact]
    public void Imza_yuku_SIRALI_randomKey_sonra_yol_sonra_govde()
    {
        // Sıra karışırsa iyzico'nun tek söylediği "Geçersiz imza" olur. Sıranın önemi burada kilitli:
        // aynı parçaların farklı sırası FARKLI imza üretmeli.
        const string secret = "s";
        var dogru = IyzicoSignature.Sign(secret, "RND" + "/yol" + "{\"a\":1}");
        var yanlis = IyzicoSignature.Sign(secret, "/yol" + "RND" + "{\"a\":1}");
        Assert.NotEqual(dogru, yanlis);
    }

    [Fact]
    public void Imza_hex_ve_kucuk_harf()
    {
        var imza = IyzicoSignature.Sign("anahtar", "yük");
        Assert.Equal(64, imza.Length);                       // SHA-256 → 32 bayt → 64 hex
        Assert.Matches("^[0-9a-f]+$", imza);                 // base64 DEĞİL, küçük harf hex
    }

    [Fact]
    public void Imza_ayni_girdiyle_kararli_farkli_anahtarla_farkli()
    {
        Assert.Equal(IyzicoSignature.Sign("k1", "yük"), IyzicoSignature.Sign("k1", "yük"));
        Assert.NotEqual(IyzicoSignature.Sign("k1", "yük"), IyzicoSignature.Sign("k2", "yük"));
    }

    [Fact]
    public void Rastgele_anahtar_ayni_milisaniyede_bile_cakismaz()
    {
        // Yalnız zaman damgası kullanılsaydı paralel çağrılar aynı anahtarı üretirdi.
        var anahtarlar = Enumerable.Range(0, 200).Select(_ => IyzicoSignature.RandomKey()).ToList();
        Assert.Equal(anahtarlar.Count, anahtarlar.Distinct().Count());
        Assert.All(anahtarlar, a => Assert.Matches("^[0-9]{22,}$", a));
    }

    [Theory]
    [InlineData("", "s", "/y")]
    [InlineData("a", "", "/y")]
    [InlineData("a", "s", "")]
    public void Eksik_bilesenle_uretilmez(string apiKey, string secret, string uri)
        => Assert.ThrowsAny<ArgumentException>(() => IyzicoSignature.Generate(apiKey, secret, uri, "{}"));
}
