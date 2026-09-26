using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Blog;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Blog SEO alanları (alt başlık, arama başlığı, meta açıklama, anahtar kelimeler, yazar,
/// kapak alt metni, aramaya-kapat). BAĞIMSIZ ORACLE: beklenen değerler elle kurulur.
///
/// <para><b>Neden ayrı alanlar:</b> sayfadaki h1 OKUYUCUYA yazılır, arama başlığı ise anahtar
/// kelime ve marka taşır; ikisini tek alana sıkıştırmak birini bozar. Aynı ayrım açıklama
/// tarafında da var: kart özeti (<c>Ozet</c>) ile arama açıklaması (<c>MetaAciklama</c>) farklı
/// yerlerde farklı işler yapar.</para>
/// </summary>
[Collection("postgres")]
public sealed class BlogSeoAlanlariTests(PostgresFixture fx)
{
    private static BlogInput Input(string title = "Uzun dönem kiralama") => new()
    {
        Baslik = title,
        Icerik = "Gövde metni.",
        Durum = BlogPostDurum.Yayinda,
    };

    private static BlogService Svc(TestHost host, Guid t, out IServiceScope scope)
    {
        scope = host.ScopeFor(t);
        return scope.ServiceProvider.GetRequiredService<BlogService>();
    }

    [Fact]
    public async Task SEO_alanlari_kaydedilir_ve_geri_okunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        var svc = Svc(host, t, out var s); using (s)
        {
            var input = Input();
            input.AltBaslik = "Aylık kiralamada dikkat edilmesi gerekenler";
            input.SeoBaslik = "Uzun Dönem Araç Kiralama Rehberi | Antalya";
            input.MetaAciklama = "Aylık araç kiralamada sözleşme, kilometre ve sigorta başlıklarını açıklıyoruz.";
            input.AnahtarKelimeler = "uzun dönem, filo kiralama, antalya";
            input.Yazar = "Ümit Yüce";
            input.KapakAlt = "Beyaz Fiat Egea önden görünüm";
            await svc.CreateAsync(input);

            var d = await svc.GetPublishedBySlugAsync("uzun-donem-kiralama");
            Assert.NotNull(d);
            Assert.Equal("Aylık kiralamada dikkat edilmesi gerekenler", d!.AltBaslik);
            Assert.Equal("Uzun Dönem Araç Kiralama Rehberi | Antalya", d.SeoBaslik);
            Assert.Equal("Ümit Yüce", d.Yazar);
            Assert.Equal("Beyaz Fiat Egea önden görünüm", d.KapakAlt);
            Assert.False(d.AramaDisi);   // varsayılan: aramaya AÇIK
            Assert.Equal(["uzun dönem", "filo kiralama", "antalya"], d.Kelimeler);
        }
    }

    /// <summary>
    /// Arama başlığı verilmemişse görünen başlığa DÜŞER. Karar tek yerde (<c>AramaBasligi</c>);
    /// sayfa ile JSON-LD ayrı ayrı karar verseydi biri boş başlık basabilirdi.
    /// </summary>
    [Fact]
    public async Task Arama_basligi_verilmemisse_goruinen_basliga_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        var svc = Svc(host, t, out var s); using (s)
        {
            await svc.CreateAsync(Input("Kış lastiği zorunluluğu"));
            var d = await svc.GetPublishedBySlugAsync("kis-lastigi-zorunlulugu");
            Assert.Null(d!.SeoBaslik);
            Assert.Equal("Kış lastiği zorunluluğu", d.AramaBasligi);
        }
    }

    /// <summary>
    /// Anahtar kelimeler normalize edilir: uçlar kırpılır, boşlar atılır, TEKRARLAR Türkçe-duyarlı
    /// ayıklanır ("Antalya" ile "antalya" AYNI kelimedir; ordinal karşılaştırma ikisini de tutardı).
    /// Bağımsız oracle: 5 girdiden 2 benzersiz kelime kalmalı.
    /// </summary>
    [Fact]
    public async Task Anahtar_kelimeler_tekrarsiz_ve_kirpilmis_saklanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        var svc = Svc(host, t, out var s); using (s)
        {
            var input = Input("Kelime testi");
            input.AnahtarKelimeler = "  Antalya , antalya,  , filo kiralama ,ANTALYA";
            await svc.CreateAsync(input);

            var d = await svc.GetPublishedBySlugAsync("kelime-testi");
            Assert.Equal(["Antalya", "filo kiralama"], d!.Kelimeler);
        }
    }

    /// <summary>Boş/whitespace SEO alanları null'a çevrilir — ekranda "boş etiket" basılmasın.</summary>
    [Fact]
    public async Task Bos_alanlar_null_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        var svc = Svc(host, t, out var s); using (s)
        {
            var input = Input("Boş alan testi");
            input.AltBaslik = "   ";
            input.MetaAciklama = "";
            input.Yazar = "  ";
            await svc.CreateAsync(input);

            var d = await svc.GetPublishedBySlugAsync("bos-alan-testi");
            Assert.Null(d!.AltBaslik);
            Assert.Null(d.MetaAciklama);
            Assert.Null(d.Yazar);
            Assert.Empty(d.Kelimeler);
        }
    }

    /// <summary>
    /// TAVSİYE SINIRI DAYATILMAZ: 160 karakteri aşan meta açıklama REDDEDİLMEZ ve KIRPILMAZ,
    /// olduğu gibi saklanır. Arama motoru kırpar, içerik kaybolmaz; yazarın metnini sessizce
    /// kesmek ya da kaydını reddetmek gerçek bir hatayı değil bir stil tercihini dayatmak olurdu.
    /// (Ekranda uyarı gösterilir — o ayrı bir yüzey.)
    /// </summary>
    [Fact]
    public async Task Uzun_meta_aciklama_reddedilmez_ve_kirpilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var longText = new string('a', 200);

        var svc = Svc(host, t, out var s); using (s)
        {
            var input = Input("Uzun açıklama");
            input.MetaAciklama = longText;
            await svc.CreateAsync(input);

            var d = await svc.GetPublishedBySlugAsync("uzun-aciklama");
            Assert.Equal(200, d!.MetaAciklama!.Length);
            Assert.True(longText.Length > BlogService.MaxMetaDescription);   // sınır gerçekten aşıldı
        }
    }

    /// <summary>Güncelleme SEO alanlarını da yazar (yalnız oluşturmada çalışmıyor).</summary>
    [Fact]
    public async Task Guncelleme_SEO_alanlarini_da_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();

        var svc = Svc(host, t, out var s); using (s)
        {
            var id = await svc.CreateAsync(Input("Güncelleme testi"));

            var newItem = Input("Güncelleme testi");
            newItem.SeoBaslik = "Sonradan Eklenen Arama Başlığı";
            newItem.AramaDisi = true;
            Assert.True(await svc.UpdateAsync(id, newItem));

            var d = await svc.GetPublishedBySlugAsync("guncelleme-testi");
            Assert.Equal("Sonradan Eklenen Arama Başlığı", d!.SeoBaslik);
            Assert.True(d.AramaDisi);
        }
    }
}
