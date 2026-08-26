using RentACar.Web.Common;

namespace RentACar.IntegrationTests;

/// <summary>
/// <c>Common/Sonuc.cs</c> URL kurma kurallarını kilitler (saf birim — DB yok).
///
/// <para><b>Neden var:</b> bu mantık bugüne kadar dört modülde ayrı ayrı elle yazılmıştı ve iki
/// gerçek hata üretmişti: (1) dönüş yolu zaten querystring içerdiğinde ikinci bir <c>?</c> eklenip
/// mesaj önceki parametreye yutuluyordu (pano "/?df=gec" akışı), (2) kira formunda
/// <c>$"?ok=1{sekme}"</c> ile fragment query'nin ortasına giriyordu.</para>
///
/// <para>Beklenen dizeler ELLE yazıldı — <c>Sonuc</c>'un kendi çıktısından türetilmedi.</para>
/// </summary>
public sealed class SonucUrlTests
{
    [Fact]
    public void Query_yoksa_soru_isareti_kullanir()
        => Assert.Equal("/kiralar?bilgi=Kaydedildi.", Sonuc.Url("/kiralar", "bilgi", "Kaydedildi.", null));

    [Fact]
    public void Query_varsa_ve_ile_ekler()
        => Assert.Equal("/?df=gec&bilgi=Tahsilat%20kaydedildi.",
                        Sonuc.Url("/?df=gec", "bilgi", "Tahsilat kaydedildi.", null));

    [Fact]
    public void Parca_daima_en_sona_gider()
        => Assert.Equal("/kiralar/5?bilgi=Kira%20kaydedildi.#sekme=donus",
                        Sonuc.Url("/kiralar/5", "bilgi", "Kira kaydedildi.", "#sekme=donus"));

    [Fact]
    public void Parca_diyezsiz_verilirse_eklenir()
        => Assert.Equal("/kiralar/5?bilgi=Tamam#sekme=kira",
                        Sonuc.Url("/kiralar/5", "bilgi", "Tamam", "sekme=kira"));

    [Fact]
    public void Yolun_icindeki_fragment_query_nin_ARKASINA_tasinir()
    {
        // Elle birleştirmede "/kiralar/5#sekme=donus" + "?bilgi=..." → fragment'in İÇİNE yazılıyordu
        // ve parametre hiç okunmuyordu.
        Assert.Equal("/kiralar/5?bilgi=Tamam#sekme=donus",
                     Sonuc.Url("/kiralar/5#sekme=donus", "bilgi", "Tamam", null));
    }

    [Fact]
    public void Mesaj_kacislanir()
    {
        // '&' kaçışlanmazsa mesaj kesilir ve gerisi ayrı bir query parametresi sanılır.
        Assert.Equal("/faturalar?hata=Ara%C3%A7%20m%C3%BCsait%20de%C4%9Fil%20%26%20iptal",
                     Sonuc.Url("/faturalar", "hata", "Araç müsait değil & iptal", null));
    }

    [Fact]
    public void Tamam_ok_degil_bilgi_anahtarini_kullanir()
    {
        // KRİTİK: 27 sayfa hâlâ `?ok=1` okuyup kendi yerel mesajını basıyor. Anahtar `ok` olsaydı
        // global şerit + yerel mesaj ÇİFT görünürdü. Farklı anahtar, çakışmayı yapısal olarak keser.
        var r = Sonuc.Tamam("/cariler", "Cari kaydedildi.");
        var url = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult>(r).Url;
        Assert.Equal("/cariler?bilgi=Cari%20kaydedildi.", url);
        Assert.DoesNotContain("ok=", url, StringComparison.Ordinal);
    }
}
