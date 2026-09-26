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
    private static BlogInput Girdi(string baslik = "Uzun dönem kiralama") => new()
    {
        Baslik = baslik,
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
            var girdi = Girdi();
            girdi.AltBaslik = "Aylık kiralamada dikkat edilmesi gerekenler";
            girdi.SeoBaslik = "Uzun Dönem Araç Kiralama Rehberi | Antalya";
            girdi.MetaAciklama = "Aylık araç kiralamada sözleşme, kilometre ve sigorta başlıklarını açıklıyoruz.";
            girdi.AnahtarKelimeler = "uzun dönem, filo kiralama, antalya";
            girdi.Yazar = "Ümit Yüce";
            girdi.KapakAlt = "Beyaz Fiat Egea önden görünüm";
            await svc.CreateAsync(girdi);

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
            await svc.CreateAsync(Girdi("Kış lastiği zorunluluğu"));
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
            var girdi = Girdi("Kelime testi");
            girdi.AnahtarKelimeler = "  Antalya , antalya,  , filo kiralama ,ANTALYA";
            await svc.CreateAsync(girdi);

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
            var girdi = Girdi("Boş alan testi");
            girdi.AltBaslik = "   ";
            girdi.MetaAciklama = "";
            girdi.Yazar = "  ";
            await svc.CreateAsync(girdi);

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
        var uzun = new string('a', 200);

        var svc = Svc(host, t, out var s); using (s)
        {
            var girdi = Girdi("Uzun açıklama");
            girdi.MetaAciklama = uzun;
            await svc.CreateAsync(girdi);

            var d = await svc.GetPublishedBySlugAsync("uzun-aciklama");
            Assert.Equal(200, d!.MetaAciklama!.Length);
            Assert.True(uzun.Length > BlogService.MaxMetaDescription);   // sınır gerçekten aşıldı
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
            var id = await svc.CreateAsync(Girdi("Güncelleme testi"));

            var yeni = Girdi("Güncelleme testi");
            yeni.SeoBaslik = "Sonradan Eklenen Arama Başlığı";
            yeni.AramaDisi = true;
            Assert.True(await svc.UpdateAsync(id, yeni));

            var d = await svc.GetPublishedBySlugAsync("guncelleme-testi");
            Assert.Equal("Sonradan Eklenen Arama Başlığı", d!.SeoBaslik);
            Assert.True(d.AramaDisi);
        }
    }
}
