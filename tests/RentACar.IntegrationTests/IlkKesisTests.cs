using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Spa;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.6 ilk kesiş — saf kararlar (<see cref="Cutover"/>): yönlendirme haritası, önek paylaşan yolların DIŞARIDA
/// kalması, tek giriş (<c>/login</c> → <c>/app/giris</c>) ve giriş sonrası hedef.
/// BAĞIMSIZ ORACLE: beklenen adresler elle yazılmış sabitlerdir (sınıfın sabitleri/haritası kullanılmaz); harita
/// ayrıca F4 envanterine (docs/roadmap/F4.md) ve sayfaların gerçek <c>@page</c> satırlarına bağlanır.
/// </summary>
public sealed class IlkKesisKararTests
{
    private const string G = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    [Theory]
    [InlineData("/", "/app/panel")]
    [InlineData("/kiralar", "/app/kiralar")]
    [InlineData("/kiralar/", "/app/kiralar")]
    [InlineData("/Kiralar", "/app/kiralar")]                    // ASP.NET yönlendirmesi gibi harf duyarsız
    [InlineData("/kiralar/yeni", "/app/kiralar/yeni")]
    [InlineData("/KIRALAR/YENI", "/app/kiralar/yeni")]
    [InlineData("/kiralar/" + G, "/app/kiralar/" + G)]
    [InlineData("/kiralar/3F2504E0-4F89-11D3-9A0C-0305E82C3301", "/app/kiralar/" + G)] // kanonik (D, küçük harf)
    [InlineData("/kiralar/" + G + "/yazdir", "/app/kiralar/" + G + "/yazdir")]
    [InlineData("/kiralar/" + G + "/yazdir/", "/app/kiralar/" + G + "/yazdir")]
    // F5.4 rezervasyon kesişi: tek @page'li altı liste sayfası
    [InlineData("/rezervasyonlar", "/app/rezervasyonlar")]
    [InlineData("/Rezervasyonlar/", "/app/rezervasyonlar")]
    [InlineData("/teklifler", "/app/teklifler")]
    [InlineData("/takvim", "/app/takvim")]
    [InlineData("/musaitlik", "/app/musaitlik")]
    [InlineData("/MUSAITLIK/", "/app/musaitlik")]
    [InlineData("/rez-sartlari", "/app/rez-sartlari")]
    [InlineData("/filo-kiralama", "/app/filo-kiralama")]
    // F6.4 araç kesişi: dört şablonun SPA adı farklı, on sayfa aynı adla /app altına
    [InlineData("/vehicles", "/app/araclar")]
    [InlineData("/Vehicles/", "/app/araclar")]
    [InlineData("/vehicles/detayli", "/app/araclar/detayli")]
    [InlineData("/vehicles/" + G, "/app/araclar/" + G)]                         // araç kartı (düzenle)
    [InlineData("/vehicles/3F2504E0-4F89-11D3-9A0C-0305E82C3301", "/app/araclar/" + G)]
    [InlineData("/araclar/" + G, "/app/araclar/" + G + "/detay")]               // araç detayı
    [InlineData("/arac-durum", "/app/arac-durum")]
    [InlineData("/arac-sahipleri", "/app/arac-sahipleri")]
    [InlineData("/segmentler", "/app/segmentler")]
    [InlineData("/arac-tipleri", "/app/arac-tipleri")]
    [InlineData("/arac-kredi", "/app/arac-kredi")]
    [InlineData("/musteri-taksit", "/app/musteri-taksit")]
    [InlineData("/arac-siparis", "/app/arac-siparis")]
    [InlineData("/baf", "/app/baf")]
    [InlineData("/BAF/", "/app/baf")]
    [InlineData("/hasar", "/app/hasar")]
    [InlineData("/filo-plan", "/app/filo-plan")]
    // F7.3 cari/CRM kesişi: sekiz sayfa aynı adla /app altına
    [InlineData("/cariler", "/app/cariler")]
    [InlineData("/Cariler/", "/app/cariler")]
    [InlineData("/cariler/" + G, "/app/cariler/" + G)]                          // cari kartı
    [InlineData("/cariler/3F2504E0-4F89-11D3-9A0C-0305E82C3301", "/app/cariler/" + G)]
    [InlineData("/cariler/" + G + "/detay", "/app/cariler/" + G + "/detay")]    // cari 360°
    [InlineData("/anketler", "/app/anketler")]
    [InlineData("/sikayetler", "/app/sikayetler")]
    [InlineData("/assistans", "/app/assistans")]
    [InlineData("/hukuk", "/app/hukuk")]
    [InlineData("/HUKUK/", "/app/hukuk")]
    [InlineData("/crm", "/app/crm")]
    // F10.3 rapor kesişi: 26 sayfa aynı adla /app altına; araç karnesi kimlikli
    [InlineData("/raporlar/gelir-gider", "/app/raporlar/gelir-gider")]
    [InlineData("/Raporlar/Gelir-Gider/", "/app/raporlar/gelir-gider")]
    [InlineData("/raporlar/filo", "/app/raporlar/filo")]
    [InlineData("/raporlar/filo-analiz", "/app/raporlar/filo-analiz")]
    [InlineData("/raporlar/personel-calisma", "/app/raporlar/personel-calisma")]
    [InlineData("/raporlar/arac-durum-takip", "/app/raporlar/arac-durum-takip")]
    [InlineData("/raporlar/virman-gecmisi", "/app/raporlar/virman-gecmisi")]
    [InlineData("/raporlar/otomatik-servisler", "/app/raporlar/otomatik-servisler")]
    [InlineData("/raporlar/arac-karne/" + G, "/app/raporlar/arac-karne/" + G)]
    [InlineData("/raporlar/arac-karne/3F2504E0-4F89-11D3-9A0C-0305E82C3301", "/app/raporlar/arac-karne/" + G)]
    // F11.3 tanım/sistem/web kesişi: 47 sayfa aynı adla /app altına; üç şablon kimlikli
    [InlineData("/markalar", "/app/markalar")]
    [InlineData("/Markalar/", "/app/markalar")]
    [InlineData("/arac-gruplari", "/app/arac-gruplari")]
    [InlineData("/rezervasyon-kaynaklari", "/app/rezervasyon-kaynaklari")]
    [InlineData("/hesap-kodlari", "/app/hesap-kodlari")]
    [InlineData("/belge-sablonlari", "/app/belge-sablonlari")]
    [InlineData("/doluluk-kurallari", "/app/doluluk-kurallari")]
    [InlineData("/drop-tanimlari", "/app/drop-tanimlari")]
    [InlineData("/subeler", "/app/subeler")]
    [InlineData("/personel", "/app/personel")]
    [InlineData("/ice-aktar", "/app/ice-aktar")]
    [InlineData("/takvim-abonelik", "/app/takvim-abonelik")]
    [InlineData("/dokumanlar", "/app/dokumanlar")]
    [InlineData("/firma-belgeleri", "/app/firma-belgeleri")]
    [InlineData("/kullanicilar", "/app/kullanicilar")]
    [InlineData("/yetki", "/app/yetki")]
    [InlineData("/ayarlar", "/app/ayarlar")]
    [InlineData("/AYARLAR/", "/app/ayarlar")]
    [InlineData("/mesaj-sablonlari", "/app/mesaj-sablonlari")]
    [InlineData("/denetim", "/app/denetim")]
    [InlineData("/bildirimler", "/app/bildirimler")]
    [InlineData("/ara", "/app/ara")]
    [InlineData("/profil/sifre-degistir", "/app/profil/sifre-degistir")]
    [InlineData("/web-sitesi", "/app/web-sitesi")]
    [InlineData("/web-sitesi/arac-ekle", "/app/web-sitesi/arac-ekle")]
    [InlineData("/web-sitesi/ilan/" + G + "/fiyat", "/app/web-sitesi/ilan/" + G + "/fiyat")]
    [InlineData("/web-sitesi/ilan/3F2504E0-4F89-11D3-9A0C-0305E82C3301/ozellikler", "/app/web-sitesi/ilan/" + G + "/ozellikler")]
    [InlineData("/site-icerik", "/app/site-icerik")]
    [InlineData("/blog-yonetim", "/app/blog-yonetim")]
    [InlineData("/blog-yonetim/" + G + "/onizleme", "/app/blog-yonetim/" + G + "/onizleme")]
    [InlineData("/gelen-talepler", "/app/gelen-talepler")]
    // F8.3 finans kesişi: 19 sayfa aynı adla /app altına; cari ekstresi ve fatura yazdır kimlikli
    [InlineData("/kasa", "/app/kasa")]
    [InlineData("/KASA/", "/app/kasa")]
    [InlineData("/finans/nakit-islem", "/app/finans/nakit-islem")]
    [InlineData("/finans/bakiye-duzeltme", "/app/finans/bakiye-duzeltme")]
    [InlineData("/cari-virman", "/app/cari-virman")]
    [InlineData("/depozito", "/app/depozito")]
    [InlineData("/tek-cari-toplu", "/app/tek-cari-toplu")]
    [InlineData("/toplu-tahsilat", "/app/toplu-tahsilat")]
    [InlineData("/toplu-gider", "/app/toplu-gider")]
    [InlineData("/otomatik-tahsilat", "/app/otomatik-tahsilat")]
    [InlineData("/donem-kapanis", "/app/donem-kapanis")]
    [InlineData("/kurlar", "/app/kurlar")]
    [InlineData("/cariler/" + G + "/ekstre", "/app/cariler/" + G + "/ekstre")]
    [InlineData("/cariler/3F2504E0-4F89-11D3-9A0C-0305E82C3301/Ekstre/", "/app/cariler/" + G + "/ekstre")]
    [InlineData("/faturalar", "/app/faturalar")]
    [InlineData("/faturalar/detay-listesi", "/app/faturalar/detay-listesi")]
    [InlineData("/faturalar/" + G + "/yazdir", "/app/faturalar/" + G + "/yazdir")]
    [InlineData("/cezalar", "/app/cezalar")]
    [InlineData("/giderler", "/app/giderler")]
    [InlineData("/gelen-efatura", "/app/gelen-efatura")]
    [InlineData("/satislar", "/app/satislar")]
    // F9.3 servis/sigorta/vade + fiyat/tarife kesişi: 15 sayfa aynı adla /app altına
    [InlineData("/servisler", "/app/servisler")]
    [InlineData("/Servisler/", "/app/servisler")]
    [InlineData("/regulasyon", "/app/regulasyon")]
    [InlineData("/vade", "/app/vade")]
    [InlineData("/servis-tanimlari", "/app/servis-tanimlari")]
    [InlineData("/tarifeler", "/app/tarifeler")]
    [InlineData("/tarife-matris", "/app/tarife-matris")]
    [InlineData("/tarife-gruplari", "/app/tarife-gruplari")]
    [InlineData("/tarife-aktar", "/app/tarife-aktar")]
    [InlineData("/sigorta-urunleri", "/app/sigorta-urunleri")]
    [InlineData("/kira-kurallari", "/app/kira-kurallari")]
    [InlineData("/broker-yasaklari", "/app/broker-yasaklari")]
    [InlineData("/fiyat-hesapla", "/app/fiyat-hesapla")]
    [InlineData("/maliyet-hesapla", "/app/maliyet-hesapla")]
    [InlineData("/MALIYET-TEKLIFLERI/", "/app/maliyet-teklifleri")]
    [InlineData("/ek-hizmetler", "/app/ek-hizmetler")]
    // F12 platform konsolu: SPA adları farklı (tenants → kiracilar, login → giris)
    [InlineData("/platform/login", "/app/platform/giris")]
    [InlineData("/platform/tenants", "/app/platform/kiracilar")]
    [InlineData("/Platform/Tenants/", "/app/platform/kiracilar")]
    [InlineData("/platform/tenants/" + G, "/app/platform/kiracilar/" + G)]
    [InlineData("/platform/belgeler", "/app/platform/belgeler")]
    public void Haritadaki_sablon_SPA_yoluna_esler(string path, string expected)
        => Assert.Equal(expected, Cutover.SpaPath(path));

    /// <summary>Önek paylaşan GET uçları ve diğer her şey haritada YOK (segment segment birebir eşleşme).</summary>
    [Theory]
    [InlineData("/kiralar/" + G + "/pdf")]                // sözleşme PDF
    [InlineData("/kiralar/" + G + "/pdf?indir=1")]
    [InlineData("/kiralar/ornek-sozlesme/pdf")]
    [InlineData("/kiralar/hesapla")]                       // canlı hesap (JSON)
    [InlineData("/kiralar/donus-hesapla")]
    [InlineData("/kiralar/musait-arac")]
    [InlineData("/kiralar/" + G + "/yazdir/x")]
    [InlineData("/kiralar/5")]                             // Guid değil
    [InlineData("/kiralar/yeni/x")]
    [InlineData("/kiralarx")]
    [InlineData("/kiralar//yazdir")]
    [InlineData("//kiralar")]
    [InlineData("/listeler/export/kiralar")]
    [InlineData("/raporlar/export/kiralar")]
    [InlineData("/kasa/makbuz/" + G + "/pdf")]
    [InlineData("/faturalar/" + G + "/pdf")]
    // F7: POST uçlarının GET'i, Blazor karşılığı olmayan SPA rotası, benzer adlar, export (cari ekstresi F8 bloğunda,
    // Blog ve Gelen Talepler F11 bloğunda yönlenir)
    [InlineData("/cariler/yeni")]
    [InlineData("/cariler/5")]                             // Guid değil
    [InlineData("/cariler/create")]
    [InlineData("/cariler/update")]
    [InlineData("/cariler/delete")]
    [InlineData("/cariler/" + G + "/detay/x")]
    [InlineData("/carilerx")]
    [InlineData("/anketler/create")]
    [InlineData("/sikayetler/update")]
    [InlineData("/assistans/delete")]
    [InlineData("/hukuk/create")]
    [InlineData("/crm/secim/kira")]
    [InlineData("/listeler/export/cariler")]
    // F11: dosya/görsel GET'leri, POST uçlarının GET'i, kimliksiz/Guid'siz ilan ve blog yolları, benzer adlar,
    // takvim beslemesi, export, API ve SPA yolları
    [InlineData("/ayarlar/logo")]
    [InlineData("/ayarlar/kaydet")]
    [InlineData("/blog-yonetim/" + G + "/kapak")]
    [InlineData("/blog-yonetim/" + G)]
    [InlineData("/blog-yonetim/5/onizleme")]
    [InlineData("/blog-yonetim/create")]
    [InlineData("/web-sitesi/ilan/" + G)]
    [InlineData("/web-sitesi/ilan/5/fiyat")]
    [InlineData("/web-sitesi/ilan/" + G + "/foto")]
    [InlineData("/web-sitesi/ilan/" + G + "/fiyat/kaydet")]
    [InlineData("/web-sitesi/ilan/olustur")]
    [InlineData("/web-sitesi/arac-ekle/x")]
    [InlineData("/site-icerik/kaydet")]
    [InlineData("/subeler/birlestir")]
    [InlineData("/kullanicilar/sifre")]
    [InlineData("/profil")]
    [InlineData("/profil/sifre-degistir/kaydet")]
    [InlineData("/ice-aktar/arac")]
    [InlineData("/dokumanlar/sil")]
    [InlineData("/bildirim/oku")]
    [InlineData("/gelen-talepler/donustur")]
    [InlineData("/rezervasyon-kaynaklari/yansit")]
    [InlineData("/arac-gruplari/ata")]
    [InlineData("/aramak")]
    [InlineData("/markalarx")]
    [InlineData("/listeler/export-personel")]
    [InlineData("/app/markalar")]
    [InlineData("/api/ui/v1/markalar")]
    [InlineData("/api/ui/v1/ayarlar")]
    [InlineData("/app/cariler")]
    [InlineData("/api/ui/v1/cariler")]
    // F8: PDF/makbuz, ekstre ve fatura alt yolları, Blazor POST uçlarının GET'i, Guid'siz kimlik, benzer adlar, export, API
    [InlineData("/faturalar/" + G + "/pdf?indir=1")]
    [InlineData("/faturalar/" + G)]
    [InlineData("/faturalar/5/yazdir")]
    [InlineData("/faturalar/detay-listesi/x")]
    [InlineData("/cariler/" + G + "/ekstre/pdf")]
    [InlineData("/cariler/5/ekstre")]
    [InlineData("/kasa/x")]
    [InlineData("/kasax")]
    [InlineData("/finans")]
    [InlineData("/finans/tahsilat")]
    [InlineData("/finans/tahsilat/ters")]
    [InlineData("/finans/odeme")]
    [InlineData("/finans/virman")]
    [InlineData("/finans/cari-virman")]
    [InlineData("/finans/fatura-manuel")]
    [InlineData("/finans/otomatik-tahsilat/calistir")]
    [InlineData("/cezalar/create")]
    [InlineData("/cezalar/yansit")]
    [InlineData("/depozito/al")]
    [InlineData("/depozito/irat")]
    [InlineData("/donem-kapanis/kilitle")]
    [InlineData("/gelen-efatura/sync")]
    [InlineData("/giderler/create")]
    [InlineData("/kurlar/yenile")]
    [InlineData("/kurlar/sabit/kaydet")]
    [InlineData("/satislar/create")]
    [InlineData("/listeler/export/faturalar")]
    [InlineData("/app/kasa")]
    [InlineData("/api/ui/v1/finans/kasa")]
    // F6: fotoğraf GET'leri, POST uçlarının GET'i, Blazor karşılığı olmayan SPA alt rotaları, benzer adlı sayfalar, export
    [InlineData("/vehicles/" + G + "/photos/" + G)]
    [InlineData("/vehicles/" + G + "/photos/" + G + "/thumb")]
    [InlineData("/vehicles/create")]
    [InlineData("/vehicles/km-log")]
    [InlineData("/vehicles/5")]                            // Guid değil
    [InlineData("/vehicles/detayli/x")]
    [InlineData("/araclar")]                               // Blazor'da liste /vehicles; /araclar yalnız {id}
    [InlineData("/araclar/yeni")]
    [InlineData("/araclar/detayli")]
    [InlineData("/araclar/" + G + "/detay")]
    [InlineData("/arac-kredi/" + G)]
    [InlineData("/arac-kredi/taksit-ode")]
    [InlineData("/arac-siparis/yeni")]
    [InlineData("/musteri-taksit/plan")]
    [InlineData("/baf/create")]
    [InlineData("/hasar/onayla")]
    [InlineData("/filo-plan/delta")]
    [InlineData("/listeler/export/araclar")]
    [InlineData("/listeler/export/arac-kredileri")]
    [InlineData("/app/araclar")]
    [InlineData("/api/ui/v1/araclar")]
    // F10: rapor export'ları, personel çalışma POST uçlarının GET'i, kimliksiz/Guid'siz karne, benzer adlar, API
    [InlineData("/raporlar/export/gelir-gider")]
    [InlineData("/raporlar/export/arac-karne")]
    [InlineData("/raporlar/export/personel-calisma")]
    [InlineData("/raporlar/personel-calisma/create")]
    [InlineData("/raporlar/personel-calisma/delete")]
    [InlineData("/raporlar/arac-karne")]
    [InlineData("/raporlar/arac-karne/5")]
    [InlineData("/raporlar/arac-karne/" + G + "/x")]
    [InlineData("/raporlar")]
    [InlineData("/raporlar/gelir-gider/x")]
    [InlineData("/raporlar/filo-analizx")]
    [InlineData("/raporlar/bilinmeyen")]
    [InlineData("/app/raporlar/gunluk")]
    [InlineData("/api/ui/v1/raporlar/gunluk")]
    [InlineData("/api/ui/v1/vardiyalar")]
    // F9: Blazor POST uçlarının GET'i (regülasyon kayıt/ödeme, servis, yansıtma, tanımlar, tarife aktar, maliyet teklifi),
    // Blazor karşılığı olmayan SPA alt rotaları (MTV/muayene listeleri, kayıt sayfaları), benzer adlı sayfalar, export, API
    [InlineData("/regulasyon/mtv")]
    [InlineData("/regulasyon/muayene")]
    [InlineData("/regulasyon/sigorta")]
    [InlineData("/regulasyon/zeyil")]
    [InlineData("/regulasyon/zeyil/sil")]
    [InlineData("/regulasyon/sigortalar/" + G)]
    [InlineData("/regulasyon/mtv/" + G)]
    [InlineData("/regulasyon-odeme/mtv")]
    [InlineData("/servisler/" + G)]
    [InlineData("/servisler/create")]
    [InlineData("/servisler/kalem")]
    [InlineData("/servis-yansitma/yansit")]
    [InlineData("/servis-tanimlari/oneri-kabul")]
    [InlineData("/tarifeler/update")]
    [InlineData("/tarife-matris/delete")]
    [InlineData("/tarife-aktar/yukle")]
    [InlineData("/tarife-aktar/kanal-sil")]
    [InlineData("/fiyat-hesapla/hesapla")]
    [InlineData("/maliyet-teklifi/kaydet")]
    [InlineData("/maliyet-teklifleri/" + G)]
    [InlineData("/kira-kurallari/create")]
    [InlineData("/vade/x")]
    [InlineData("/vadex")]
    [InlineData("/listeler/export/vade")]
    [InlineData("/app/vade")]
    [InlineData("/api/ui/v1/vade")]
    [InlineData("/api/ui/v1/servisler")]
    // F5: Blazor'da karşılığı olmayan SPA alt rotaları, POST uçlarının GET'i, önek/benzer adlı sayfalar, export, takvim beslemesi
    [InlineData("/rezervasyonlar/yeni")]
    [InlineData("/rezervasyonlar/" + G)]
    [InlineData("/rezervasyonlar/create")]
    [InlineData("/rezervasyonlar/cancel")]
    [InlineData("/teklifler/yeni")]
    [InlineData("/teklifler/" + G)]
    [InlineData("/teklifler/kabul")]
    [InlineData("/takvim/yenile")]
    [InlineData("/rezervasyonlarx")]
    [InlineData("/musaitlik/x")]
    [InlineData("/rez-sartlari/delete")]
    [InlineData("/filo-kiralama/yeni")]
    [InlineData("/filo-kiralama/" + G)]
    [InlineData("/filo-kiralama/iptal")]
    [InlineData("/listeler/export/rezervasyonlar")]
    [InlineData("/listeler/export/filo-kiralama")]
    [InlineData("/feed/calendar/x.ics")]
    [InlineData("/app/rezervasyonlar")]
    [InlineData("/api/ui/v1/rezervasyonlar")]
    [InlineData("/api/ui/v1/musaitlik")]
    [InlineData("/login")]
    [InlineData("/app/kiralar")]
    [InlineData("/api/ui/v1/kiralar")]
    // F12: platform POST uçlarının GET'i, dosya GET'leri, Guid'siz detay, benzer adlar, SPA ve API yolları
    [InlineData("/platform")]
    [InlineData("/platform/tenants/5")]
    [InlineData("/platform/tenants/create")]
    [InlineData("/platform/tenants/toggle")]
    [InlineData("/platform/tenants/" + G + "/x")]
    [InlineData("/platform/auth/login")]
    [InlineData("/platform/belgeler/yukle")]
    [InlineData("/platform/belgeler/" + G + "/indir")]
    [InlineData("/platformx/tenants")]
    [InlineData("/app/platform/kiracilar")]
    [InlineData("/api/ui/v1/platform/kiracilar")]
    [InlineData("kiralar")]
    [InlineData("")]
    [InlineData(null)]
    public void Harita_disi_yol_yonlenmez(string? path)
        => Assert.Null(Cutover.SpaPath(path));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public void Yalniz_GET_HEAD_yonlenir(string method)
    {
        Assert.Null(Cutover.SpaTarget(method, "/kiralar", QueryString.Empty));
        Assert.Equal("/app/kiralar", Cutover.SpaTarget("GET", "/kiralar", QueryString.Empty));
        Assert.Equal("/app/kiralar", Cutover.SpaTarget("HEAD", "/kiralar", QueryString.Empty));
    }

    [Fact]
    public void Sorgu_dizesi_AYNEN_tasinir()
    {
        // Müsaitlik/araç durumu "Kirala" bağlantısı (kira sorgu sözleşmesi): varac, vfrom, vto, vgrup, musteriId.
        const string query = "?varac=" + G + "&vfrom=2026-10-01T10%3A00&vto=2026-10-04T10%3A00&vgrup=%C3%96zel+Grup&musteriId=" + G;
        Assert.Equal("/app/kiralar/yeni" + query, Cutover.SpaTarget("GET", "/kiralar/yeni", new QueryString(query)));
        Assert.Equal("/app/kiralar?q=a&q=b&bilgi=x", Cutover.SpaTarget("GET", "/kiralar", new QueryString("?q=a&q=b&bilgi=x")));
        // Ham (kodlanmamış) ASCII dışı sorgu Location'a yazılamaz → yönlendirme yok (Blazor sayfası açılır, 500 değil).
        Assert.Null(Cutover.SpaTarget("GET", "/kiralar", new QueryString("?q=Yılmaz")));
        Assert.Null(Cutover.SpaTarget("GET", "/kiralar", new QueryString("?q=a b")));
    }

    [Theory]
    [InlineData("/login", true)]
    [InlineData("/login/", true)]
    [InlineData("/LOGIN", true)]
    [InlineData("/platform/login", false)]                 // platform girişi ETKİLENMEZ
    [InlineData("/loginx", false)]
    [InlineData("/login/x", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void Blazor_giris_yolu_segment_esitligiyle(string path, bool expected)
        => Assert.Equal(expected, Cutover.IsBlazorEntry(path.Length == 0 ? PathString.Empty : new PathString(path)));

    [Theory]
    [InlineData("", "/app/giris")]
    [InlineData("?ReturnUrl=%2Fkiralar%3Fvarac%3Dx", "/app/giris?returnUrl=%2Fkiralar%3Fvarac%3Dx")]
    [InlineData("?ReturnUrl=%2Fkiralar%2F" + G + "%3Fsekme%3Dodeme", "/app/giris?returnUrl=%2Fkiralar%2F" + G + "%3Fsekme%3Dodeme")]
    [InlineData("?returnurl=%2Fvehicles", "/app/giris?returnUrl=%2Fvehicles")]            // anahtar harf duyarsız
    [InlineData("?ReturnUrl=%2F", "/app/giris")]                                         // Panel = varsayılan, taşınmaz
    [InlineData("?ReturnUrl=%2F%2Fevil.com", "/app/giris")]                              // açık yönlendirme
    [InlineData("?ReturnUrl=https%3A%2F%2Fevil.com", "/app/giris")]
    [InlineData("?ReturnUrl=%2Flogin", "/app/giris")]                                    // döngü
    [InlineData("?ReturnUrl=%2Fplatform%2Ftenants", "/app/giris")]                       // alan geçişi
    [InlineData("?ReturnUrl=%2Flisteler%2Fexport%2Fcariler", "/app/giris")]              // indirme dönüş olamaz
    [InlineData("?hata=kapali", "/app/giris?neden=kiraci_kapali")]
    [InlineData("?hata=1&ReturnUrl=%2Fvehicles", "/app/giris?returnUrl=%2Fvehicles")]   // diğer hata kodları taşınmaz
    [InlineData("?bilgi=x&foo=bar", "/app/giris")]
    public void Oturumsuz_login_SPA_girisine(string query, string expected)
        => Assert.Equal(expected, Cutover.LoginRedirect(new QueryString(query.Length == 0 ? null : query)));

    [Theory]
    // F13.1b: pilot ayrımı yok — /app dönüşü aynen, haritadaki eski Blazor adresi SPA karşılığına, varsayılan Panel
    [InlineData(null, "/app/panel")]
    [InlineData("/", "/app/panel")]
    [InlineData("/kiralar?varac=x&vfrom=y", "/app/kiralar?varac=x&vfrom=y")]
    [InlineData("/kiralar/yeni?varac=x", "/app/kiralar/yeni?varac=x")]
    [InlineData("/kiralar/" + G + "#sekme=odeme", "/app/kiralar/" + G + "#sekme=odeme")]
    [InlineData("/app/kiralar?q=a", "/app/kiralar?q=a")]
    [InlineData("/app/giris?returnUrl=%2Fapp", "/app/panel")]                     // döngü yok
    [InlineData("/APP/GIRIS", "/app/panel")]
    [InlineData("/rezervasyonlar?durum=Rezerv", "/app/rezervasyonlar?durum=Rezerv")] // F5.4
    [InlineData("/musaitlik?from=2026-10-01&to=2026-10-04", "/app/musaitlik?from=2026-10-01&to=2026-10-04")]
    [InlineData("/takvim-abonelik", "/app/takvim-abonelik")]                      // F11.3 (F5 dışı benzer ad)
    [InlineData("/gelen-talepler?durum=0", "/app/gelen-talepler?durum=0")]        // F11.3
    [InlineData("/web-sitesi/ilan/" + G + "/fiyat", "/app/web-sitesi/ilan/" + G + "/fiyat")]
    [InlineData("/bildirimler?x=1", "/app/bildirimler?x=1")]                      // F11.3
    [InlineData("/yetkisiz?x=1", "/yetkisiz?x=1")]                                // kabuk adresi: GET'i ShellTarget ile Panel'e
    [InlineData("/cariler?x=1", "/app/cariler?x=1")]                              // F7.3
    [InlineData("/cariler/" + G + "/detay#sekme=ekstre", "/app/cariler/" + G + "/detay#sekme=ekstre")]
    [InlineData("/cariler/" + G + "/ekstre", "/app/cariler/" + G + "/ekstre")]    // F8.3
    [InlineData("/kasa?x=1", "/app/kasa?x=1")]
    [InlineData("/faturalar?durum=Acik#liste", "/app/faturalar?durum=Acik#liste")]
    [InlineData("/faturalar/" + G + "/pdf", "/faturalar/" + G + "/pdf")]          // fatura PDF yönlenmez
    [InlineData("/vehicles?x=1", "/app/araclar?x=1")]                             // F6.4
    [InlineData("/araclar/" + G + "#km", "/app/araclar/" + G + "/detay#km")]
    [InlineData("/raporlar/gelir-gider?bas=2026-09-01", "/app/raporlar/gelir-gider?bas=2026-09-01")] // F10.3
    [InlineData("/raporlar/arac-karne/" + G, "/app/raporlar/arac-karne/" + G)]
    [InlineData("/vade?plaka=34ABC12", "/app/vade?plaka=34ABC12")]                 // F9.3
    [InlineData("/servisler?durum=Serviste", "/app/servisler?durum=Serviste")]
    [InlineData("/regulasyon#mtv", "/app/regulasyon#mtv")]
    [InlineData("/listeler/export/vade", "/app/panel")]                            // indirme dönüş olamaz
    [InlineData("/kiralar/" + G + "/pdf", "/kiralar/" + G + "/pdf")]              // PDF yönlenmez
    [InlineData("//evil.com", "/app/panel")]
    [InlineData("/login", "/app/panel")]
    [InlineData("/listeler/export/cariler", "/app/panel")]
    [InlineData("/platform/tenants", "/app/panel")]                                    // alan geçişi reddedilir
    public void Oturumlu_giris_sonrasi_hedef(string? returnInfo, string expected)
        => Assert.Equal(expected, Cutover.AfterLogin(returnInfo));

    [Theory]
    [InlineData("/app/panel", "/")]
    [InlineData("/app/kiralar", "/kiralar")]
    [InlineData("/app/kiralar/yeni", "/kiralar/yeni")]
    [InlineData("/app/kiralar/{id}", null)]                // parametreli rota menüde yok
    [InlineData("/app/rezervasyonlar", "/rezervasyonlar")] // F5.4 menü öğeleri
    [InlineData("/app/teklifler", "/teklifler")]
    [InlineData("/app/takvim", "/takvim")]
    [InlineData("/app/musaitlik", "/musaitlik")]
    [InlineData("/app/rez-sartlari", "/rez-sartlari")]
    [InlineData("/app/filo-kiralama", "/filo-kiralama")]
    [InlineData("/app/rezervasyonlar/yeni", null)]         // Blazor'da ayrı "yeni" sayfası yoktu
    [InlineData("/app/araclar", "/vehicles")]              // F6.4 menü öğeleri (Araçlar grubu + iki tanım)
    [InlineData("/app/araclar/detayli", "/vehicles/detayli")]
    [InlineData("/app/arac-durum", "/arac-durum")]
    [InlineData("/app/arac-siparis", "/arac-siparis")]
    [InlineData("/app/filo-plan", "/filo-plan")]
    [InlineData("/app/arac-kredi", "/arac-kredi")]
    [InlineData("/app/musteri-taksit", "/musteri-taksit")]
    [InlineData("/app/baf", "/baf")]
    [InlineData("/app/hasar", "/hasar")]
    [InlineData("/app/arac-tipleri", "/arac-tipleri")]
    [InlineData("/app/arac-sahipleri", "/arac-sahipleri")]
    [InlineData("/app/segmentler", "/segmentler")]
    [InlineData("/app/araclar/yeni", null)]                // Blazor'da ayrı "yeni araç" sayfası yoktu
    [InlineData("/app/cariler", "/cariler")]               // F7.3 menü öğeleri (Cariler & CRM grubunun F7 sayfaları)
    [InlineData("/app/crm", "/crm")]
    [InlineData("/app/sikayetler", "/sikayetler")]
    [InlineData("/app/assistans", "/assistans")]
    [InlineData("/app/hukuk", "/hukuk")]
    [InlineData("/app/anketler", "/anketler")]
    [InlineData("/app/cariler/yeni", null)]                // Blazor'da yeni cari liste içi formdu
    [InlineData("/app/raporlar/gelir-gider", "/raporlar/gelir-gider")] // F10.3 menü öğeleri (Raporlar grubu)
    [InlineData("/app/raporlar/personel-calisma", "/raporlar/personel-calisma")]
    [InlineData("/app/raporlar/filo", "/raporlar/filo")]
    [InlineData("/app/raporlar/virman-gecmisi", "/raporlar/virman-gecmisi")]
    [InlineData("/app/raporlar/arac-karne/{id}", null)]    // kimlikli rota menüde yok
    [InlineData("/app/markalar", "/markalar")]             // F11.3 menü öğeleri (Tanımlar, Web Sitesi, Sistem, grupsuz)
    [InlineData("/app/doluluk-kurallari", "/doluluk-kurallari")]
    [InlineData("/app/web-sitesi", "/web-sitesi")]
    [InlineData("/app/gelen-talepler", "/gelen-talepler")]
    [InlineData("/app/blog-yonetim", "/blog-yonetim")]
    [InlineData("/app/bildirimler", "/bildirimler")]
    [InlineData("/app/dokumanlar", "/dokumanlar")]
    [InlineData("/app/subeler", "/subeler")]
    [InlineData("/app/ice-aktar", "/ice-aktar")]
    [InlineData("/app/web-sitesi/ilan/{id}/fiyat", null)]  // kimlikli rota menüde yok
    [InlineData("/app/kasa", "/kasa")]                     // F8.3 menü öğeleri (Finans grubu)
    [InlineData("/app/finans/nakit-islem", "/finans/nakit-islem")]
    [InlineData("/app/faturalar", "/faturalar")]
    [InlineData("/app/faturalar/detay-listesi", "/faturalar/detay-listesi")]
    [InlineData("/app/donem-kapanis", "/donem-kapanis")]
    [InlineData("/app/cariler/{id}/ekstre", null)]         // kimlikli rota menüde yok
    [InlineData("/app/faturalar/{id}/yazdir", null)]
    [InlineData("/app/servisler", "/servisler")]           // F9.3 menü öğeleri (Servis & Sigorta, Fiyat & Tarife, Vade)
    [InlineData("/app/servis-tanimlari", "/servis-tanimlari")]
    [InlineData("/app/regulasyon", "/regulasyon")]
    [InlineData("/app/vade", "/vade")]
    [InlineData("/app/tarifeler", "/tarifeler")]
    [InlineData("/app/tarife-matris", "/tarife-matris")]
    [InlineData("/app/tarife-gruplari", "/tarife-gruplari")]
    [InlineData("/app/tarife-aktar", "/tarife-aktar")]
    [InlineData("/app/sigorta-urunleri", "/sigorta-urunleri")]
    [InlineData("/app/kira-kurallari", "/kira-kurallari")]
    [InlineData("/app/broker-yasaklari", "/broker-yasaklari")]
    [InlineData("/app/fiyat-hesapla", "/fiyat-hesapla")]
    [InlineData("/app/maliyet-hesapla", "/maliyet-hesapla")]
    [InlineData("/app/maliyet-teklifleri", "/maliyet-teklifleri")]
    [InlineData("/app/ek-hizmetler", "/ek-hizmetler")]
    [InlineData("/app/regulasyon/mtv", null)]              // Blazor'da ayrı MTV sayfası yoktu (tek /regulasyon)
    [InlineData("/app/regulasyon/muayene", null)]
    [InlineData("/vehicles", null)]
    public void Menu_icin_Blazor_karsiligi(string spa, string? expected)
        => Assert.Equal(expected, Cutover.BlazorEquivalent(spa));

    /// <summary>
    /// Bir fazın envanter tablosundaki (<c>docs/roadmap/F?.md</c>) sayfa rotaları. F13.1a'dan beri tablodaki her Blazor
    /// sayfa dosyasının SİLİNMİŞ olduğu da doğrulanır (önceden gerçek <c>@page</c> satırlarıyla karşılaştırılıyordu).
    /// </summary>
    private static HashSet<string> InventoryRoutes(string faz, int expectedPage)
    {
        var root = RepoRoot();
        var md = File.ReadAllText(Path.Combine(root, $"docs/roadmap/{faz}.md"));
        var rows = Regex.Matches(md, @"^\| `(?<dosya>[^`]+\.razor)`[^|]*\| (?<rotalar>[^|]+) \|", RegexOptions.Multiline);
        Assert.Equal(expectedPage, rows.Count);

        static string Normal(string r) => r.Trim().Replace("{Id:guid}", "{id:guid}", StringComparison.Ordinal)
            .Replace("{VehicleId:guid}", "{id:guid}", StringComparison.Ordinal); // F10 araç karnesi
        var inventory = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match s in rows)
        {
            // Kabuk (layout) satırı rota değil: F12 PlatformLayout gibi (@page yok, "(kabuk)" yazılı).
            if (s.Groups["rotalar"].Value.Trim() == "`(kabuk)`") continue;
            foreach (var r in s.Groups["rotalar"].Value.Split("<br>")) inventory.Add(Normal(r.Trim('`', ' ')));
            // F13.1a: fazın Blazor sayfası SİLİNDİ (Exit "@page = 0"); rota yalnız yönlendirme haritasında yaşar.
            var file = Path.Combine(root, "src/RentACar.Web/Components/Pages", s.Groups["dosya"].Value);
            Assert.False(File.Exists(file), $"F13.1a sonrası Blazor sayfası hâlâ var: {s.Groups["dosya"].Value}");
        }
        return inventory;
    }

    /// <summary>F11 envanterinin 47 <c>@page</c> şablonu (elle yazılmış oracle; F11.md tablosu ve sayfaların gerçek satırları).</summary>
    private static readonly string[] F11Sources =
    [
        "/aksesuarlar", "/arac-gruplari", "/bankalar", "/belge-sablonlari", "/ceza-turleri", "/departmanlar",
        "/doluluk-kurallari", "/dovizler", "/drop-tanimlari", "/gider-turleri", "/hesap-kodlari", "/hesaplar",
        "/iptal-sebepleri", "/kdv-oranlari", "/lokasyonlar", "/markalar", "/musteri-gruplari", "/odeme-tipleri",
        "/ozel-kodlar", "/personel", "/renkler", "/rezervasyon-kaynaklari", "/sigorta-sirketleri", "/subeler", "/ulkeler",
        "/vites-turleri", "/yakit-turleri", "/dokumanlar", "/firma-belgeleri", "/takvim-abonelik", "/ice-aktar",
        "/kullanicilar", "/yetki", "/ayarlar", "/mesaj-sablonlari", "/denetim", "/bildirimler", "/ara",
        "/profil/sifre-degistir", "/web-sitesi", "/web-sitesi/arac-ekle", "/web-sitesi/ilan/{id:guid}/fiyat",
        "/web-sitesi/ilan/{id:guid}/ozellikler", "/site-icerik", "/blog-yonetim", "/blog-yonetim/{id:guid}/onizleme",
        "/gelen-talepler",
    ];

    /// <summary>
    /// Harita = kesişi yapılmış fazların (F4, F5) envanterindeki silinecek <c>@page</c> şablonları (<c>/login</c>
    /// hariç — o herkes için tek giriş). Fazlar ayrık; hedefler /app altında ve benzersiz.
    /// </summary>
    [Fact]
    public void Harita_kesisi_yapilmis_fazlarin_page_sablonlarindan_turetilmis()
    {
        var f4 = InventoryRoutes("F4", 5);
        Assert.Contains("/login", f4);
        f4.Remove("/login");
        var f5 = InventoryRoutes("F5", 6);
        Assert.Equal(new[] { "/filo-kiralama", "/musaitlik", "/rez-sartlari", "/rezervasyonlar", "/takvim", "/teklifler" },
            f5.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f4.Intersect(f5));
        var f6 = InventoryRoutes("F6", 14);
        Assert.Equal(new[]
            {
                "/arac-durum", "/arac-kredi", "/arac-sahipleri", "/arac-siparis", "/arac-tipleri", "/araclar/{id:guid}",
                "/baf", "/filo-plan", "/hasar", "/musteri-taksit", "/segmentler", "/vehicles", "/vehicles/detayli",
                "/vehicles/{id:guid}",
            },
            f6.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f6.Intersect(f4.Concat(f5)));
        var f7 = InventoryRoutes("F7", 8);
        Assert.Equal(new[]
            {
                "/anketler", "/assistans", "/cariler", "/cariler/{id:guid}", "/cariler/{id:guid}/detay", "/crm", "/hukuk",
                "/sikayetler",
            },
            f7.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f7.Intersect(f4.Concat(f5).Concat(f6)));
        var f10 = InventoryRoutes("F10", 26);
        Assert.Equal(new[]
            {
                "/raporlar/arac-durum-takip", "/raporlar/arac-gunluk-durum", "/raporlar/arac-karne/{id:guid}",
                "/raporlar/cari-bakiye", "/raporlar/doluluk", "/raporlar/ek-hizmet", "/raporlar/extre-ozeti",
                "/raporlar/fatura-donem", "/raporlar/filo", "/raporlar/filo-analiz", "/raporlar/finans-analiz",
                "/raporlar/gelir-gider", "/raporlar/gunluk", "/raporlar/karlilik", "/raporlar/karsilastirmali-analiz",
                "/raporlar/kasa-banka", "/raporlar/kdv-listesi", "/raporlar/km-detay", "/raporlar/otomatik-servisler",
                "/raporlar/periyodik-servis", "/raporlar/personel-calisma", "/raporlar/rezervasyon-kaynak",
                "/raporlar/servis-ozet", "/raporlar/sigorta-muayene", "/raporlar/tahsilat-fatura", "/raporlar/virman-gecmisi",
            },
            f10.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f10.Intersect(f4.Concat(f5).Concat(f6).Concat(f7)));
        var f9 = InventoryRoutes("F9", 15);
        Assert.Equal(new[]
            {
                "/broker-yasaklari", "/ek-hizmetler", "/fiyat-hesapla", "/kira-kurallari", "/maliyet-hesapla",
                "/maliyet-teklifleri", "/regulasyon", "/servis-tanimlari", "/servisler", "/sigorta-urunleri", "/tarife-aktar",
                "/tarife-gruplari", "/tarife-matris", "/tarifeler", "/vade",
            },
            f9.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f9.Intersect(f4.Concat(f5).Concat(f6).Concat(f7).Concat(f10)));
        var f8 = InventoryRoutes("F8", 19);
        Assert.Equal(new[]
            {
                "/cari-virman", "/cariler/{id:guid}/ekstre", "/cezalar", "/depozito", "/donem-kapanis", "/faturalar",
                "/faturalar/detay-listesi", "/faturalar/{id:guid}/yazdir", "/finans/bakiye-duzeltme", "/finans/nakit-islem",
                "/gelen-efatura", "/giderler", "/kasa", "/kurlar", "/otomatik-tahsilat", "/satislar", "/tek-cari-toplu",
                "/toplu-gider", "/toplu-tahsilat",
            },
            f8.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f8.Intersect(f4.Concat(f5).Concat(f6).Concat(f7).Concat(f10).Concat(f9)));
        var f11 = InventoryRoutes("F11", 47);
        Assert.Equal(F11Sources.OrderBy(x => x, StringComparer.Ordinal), f11.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f11.Intersect(f4.Concat(f5).Concat(f6).Concat(f7).Concat(f10).Concat(f9).Concat(f8)));
        var f12 = InventoryRoutes("F12", 5); // PlatformLayout kabuk satırı sayılır ama rota üretmez
        Assert.Equal(new[] { "/platform/belgeler", "/platform/login", "/platform/tenants", "/platform/tenants/{id:guid}" },
            f12.OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(f12.Intersect(f4.Concat(f5).Concat(f6).Concat(f7).Concat(f10).Concat(f9).Concat(f8).Concat(f11)));

        Assert.Equal(f4.Concat(f5).Concat(f6).Concat(f7).Concat(f10).Concat(f9).Concat(f8).Concat(f11).Concat(f12).OrderBy(x => x), Cutover.Map.Select(e => e.Kaynak).OrderBy(x => x));
        Assert.All(Cutover.Map, e => Assert.StartsWith("/app/", e.Hedef));
        Assert.Equal(Cutover.Map.Count, Cutover.Map.Select(e => e.Hedef).Distinct().Count());
    }

    /// <summary>
    /// F5 haritasının hedefleri SPA'da GERÇEK rota (bağımsız kaynak: Angular rota dosyaları metin olarak okunur —
    /// yoksa pilot kullanıcı 302 sonrası SPA'nın "sayfa yok" ekranına düşerdi).
    /// </summary>
    [Fact]
    public void F5_hedefleri_SPA_rota_dosyalarinda_tanimli()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        var text = File.ReadAllText(Path.Combine(app, "sayfalar.ts"))
            + File.ReadAllText(Path.Combine(app, "features/rezervasyonlar/rezervasyonlar.routes.ts"));
        foreach (var path in new[] { "rezervasyonlar", "teklifler", "takvim", "musaitlik", "rez-sartlari", "filo-kiralama" })
            Assert.Contains($"path: '{path}',", text);
    }

    /// <summary>
    /// F6 haritasının hedefleri SPA'da GERÇEK rota (Angular rota dosyaları metin olarak). Hedefler elle yazılmıştır;
    /// üç tanım sayfası rota dosyasında tek eşlemden üretildiği için adlarıyla aranır.
    /// </summary>
    [Fact]
    public void F6_hedefleri_SPA_rota_dosyalarinda_tanimli()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        var text = File.ReadAllText(Path.Combine(app, "sayfalar.ts"))
            + File.ReadAllText(Path.Combine(app, "features/vehicles/vehicles.routes.ts"))
            + File.ReadAllText(Path.Combine(app, "features/vehicle-finance/vehicle-finance.routes.ts"));
        Assert.Contains("VEHICLE_ROUTES", File.ReadAllText(Path.Combine(app, "sayfalar.ts")));
        Assert.Contains("VEHICLE_FINANCE_ROUTES", File.ReadAllText(Path.Combine(app, "sayfalar.ts")));
        foreach (var path in new[]
                 {
                     "araclar", "araclar/detayli", "araclar/:id", "araclar/:id/detay", "arac-durum", "arac-kredi",
                     "musteri-taksit", "arac-siparis", "baf", "hasar", "filo-plan",
                 })
            Assert.Contains($"path: '{path}',", text);
        foreach (var path in new[] { "'arac-sahipleri'", "'segmentler'", "'arac-tipleri'" })
            Assert.Contains(path, text);

        // Harita hedefleri (SPA'da /app öneki olmadan, {id} → :id) bu listeyle birebir.
        var f6Targets = Cutover.Map.Skip(11).Take(14).Select(e => e.Hedef["/app/".Length..].Replace("{id}", ":id")).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[]
            {
                "arac-durum", "arac-kredi", "arac-sahipleri", "arac-siparis", "arac-tipleri", "araclar", "araclar/:id",
                "araclar/:id/detay", "araclar/detayli", "baf", "filo-plan", "hasar", "musteri-taksit", "segmentler",
            },
            f6Targets);
    }

    /// <summary>
    /// F7 haritasının hedefleri SPA'da GERÇEK rota (Angular rota dosyaları metin olarak). Hedefler elle yazılmıştır.
    /// </summary>
    [Fact]
    public void F7_targets_are_defined_in_SPA_route_files()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        var pages = File.ReadAllText(Path.Combine(app, "sayfalar.ts"));
        Assert.Contains("CUSTOMER_ROUTES", pages);
        Assert.Contains("CRM_ROUTES", pages);
        var text = pages
            + File.ReadAllText(Path.Combine(app, "features/customers/customers.routes.ts"))
            + File.ReadAllText(Path.Combine(app, "features/crm/crm.routes.ts"));
        foreach (var path in new[] { "cariler", "cariler/:id", "cariler/:id/detay", "anketler", "sikayetler", "assistans", "hukuk", "crm" })
            Assert.Contains($"path: '{path}',", text);

        // Harita hedefleri (SPA'da /app öneki olmadan, {id} → :id) bu listeyle birebir.
        // Konumla değil kaynakla seçilir: başka fazın bloğu araya girse de (paralel kesiş PR'ları) test kaymaz.
        string[] f7Sources =
        [
            "/cariler", "/cariler/{id:guid}", "/cariler/{id:guid}/detay", "/anketler", "/sikayetler", "/assistans", "/hukuk",
            "/crm",
        ];
        var f7Targets = Cutover.Map.Where(e => f7Sources.Contains(e.Kaynak)).Select(e => e.Hedef["/app/".Length..].Replace("{id}", ":id")).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[] { "anketler", "assistans", "cariler", "cariler/:id", "cariler/:id/detay", "crm", "hukuk", "sikayetler" },
            f7Targets);
    }

    /// <summary>
    /// F10 haritasının hedefleri SPA'da GERÇEK rota. Rapor rotaları tek tablodan üretilir
    /// (<c>reports.routes.ts</c> <c>REPORT_ROUTE_TABLE</c>: <c>['kod', …]</c> → <c>raporlar/kod</c>, karne
    /// <c>raporlar/arac-karne/:id</c>); tablo satırları ve karne şablonu metin olarak aranır.
    /// </summary>
    [Fact]
    public void F10_hedefleri_SPA_rota_dosyalarinda_tanimli()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        Assert.Contains("REPORT_ROUTES", File.ReadAllText(Path.Combine(app, "sayfalar.ts")));
        var text = File.ReadAllText(Path.Combine(app, "features/reports/reports.routes.ts"));
        Assert.Contains("`raporlar/${code}`", text);
        Assert.Contains("'raporlar/arac-karne/:id'", text);

        // F7.3 birleşmesi: F10 bloğu F7'den sonra geldiği için konumla (Skip) değil kaynak önekiyle seçilir.
        var f10 = Cutover.Map.Where(e => e.Kaynak.StartsWith("/raporlar/", StringComparison.Ordinal)).ToList();
        var f10Targets = f10.Select(e => e.Hedef["/app/raporlar/".Length..]).ToList();
        Assert.Equal(26, f10Targets.Count);
        Assert.All(f10, e => Assert.StartsWith("/app/raporlar/", e.Hedef));
        Assert.Equal(26, Cutover.Map.Count(e => e.Hedef.StartsWith("/app/raporlar/", StringComparison.Ordinal)));
        foreach (var code in f10Targets.Select(h => h.Replace("/{id}", "", StringComparison.Ordinal)))
            Assert.Contains($"['{code}',", text);
    }

    /// <summary>
    /// F11 haritasının hedefleri SPA'da GERÇEK rota. Genel tanım ekranları tek eşlemden üretilir
    /// (<c>definition-paths.ts</c> <c>DEFINITION_PATHS</c>: <c>tur: 'yol',</c>); diğerleri rota dosyalarında
    /// <c>path: 'yol',</c>. Hedefler kaynakla seçilir (konumla değil): paralel kesiş blokları testi kaydırmaz.
    /// </summary>
    [Fact]
    public void F11_targets_are_defined_in_SPA_route_files()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        var pages = File.ReadAllText(Path.Combine(app, "sayfalar.ts"));
        Assert.Contains("DEFINITION_ROUTES", pages);
        Assert.Contains("SYSTEM_ROUTES", pages);
        var routeFiles = File.ReadAllText(Path.Combine(app, "features/definitions/definitions.routes.ts"))
            + File.ReadAllText(Path.Combine(app, "features/system/system.routes.ts"));
        var definitionPaths = File.ReadAllText(Path.Combine(app, "features/definitions/definition-paths.ts"));

        var f11 = Cutover.Map.Where(e => F11Sources.Contains(e.Kaynak)).ToList();
        Assert.Equal(47, f11.Count);
        Assert.All(f11, e => Assert.Equal("/app" + e.Kaynak.Replace("{id:guid}", "{id}", StringComparison.Ordinal), e.Hedef));
        var missing = new List<string>();
        foreach (var target in f11.Select(e => e.Hedef["/app/".Length..].Replace("{id}", ":id", StringComparison.Ordinal)))
        {
            if (routeFiles.Contains($"path: '{target}',", StringComparison.Ordinal)) continue;
            if (Regex.IsMatch(definitionPaths, $@"^\s*\w+: '{Regex.Escape(target)}',", RegexOptions.Multiline)) continue;
            missing.Add(target);
        }
        Assert.True(missing.Count == 0, "SPA'da rotası olmayan F11 hedefi: " + string.Join(", ", missing));
    }

    /// <summary>
    /// F8 haritasının hedefleri SPA'da GERÇEK rota (<c>finance.routes.ts</c> + <c>finance-documents.routes.ts</c> metin
    /// olarak). Hedefler elle yazılmıştır; konumla değil kaynakla seçilir (paralel kesiş blokları testi kaydırmaz).
    /// </summary>
    [Fact]
    public void F8_targets_are_defined_in_SPA_route_files()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        var pages = File.ReadAllText(Path.Combine(app, "sayfalar.ts"));
        Assert.Contains("FINANCE_ROUTES", pages);
        Assert.Contains("FINANCE_DOCUMENT_ROUTES", pages);
        var text = File.ReadAllText(Path.Combine(app, "features/finance/finance.routes.ts"))
            + File.ReadAllText(Path.Combine(app, "features/finance-documents/finance-documents.routes.ts"));
        string[] expected =
        [
            "cari-virman", "cariler/:id/ekstre", "cezalar", "depozito", "donem-kapanis", "faturalar", "faturalar/:id/yazdir",
            "faturalar/detay-listesi", "finans/bakiye-duzeltme", "finans/nakit-islem", "gelen-efatura", "giderler", "kasa",
            "kurlar", "otomatik-tahsilat", "satislar", "tek-cari-toplu", "toplu-gider", "toplu-tahsilat",
        ];
        foreach (var path in expected)
            Assert.Contains($"path: '{path}',", text);

        string[] f8Sources =
        [
            "/kasa", "/finans/nakit-islem", "/finans/bakiye-duzeltme", "/cari-virman", "/depozito", "/tek-cari-toplu",
            "/toplu-tahsilat", "/toplu-gider", "/otomatik-tahsilat", "/donem-kapanis", "/kurlar", "/cariler/{id:guid}/ekstre",
            "/faturalar", "/faturalar/detay-listesi", "/faturalar/{id:guid}/yazdir", "/cezalar", "/giderler", "/gelen-efatura",
            "/satislar",
        ];
        var f8Targets = Cutover.Map.Where(e => f8Sources.Contains(e.Kaynak))
            .Select(e => e.Hedef["/app/".Length..].Replace("{id}", ":id")).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(expected, f8Targets);

        // Cari ekstresi rotası FinanceWrite ∨ ViewReports ile kapılı (#295 KVKK M1): Blazor sayfası yalnız [Authorize]
        // idi; yönlenen operatör SPA'da izin ekranına düşer, firma geneli cari defterini görmez.
        var statement = Regex.Match(text, @"path: 'cariler/:id/ekstre',[\s\S]*?canMatch: \[(?<g>[^\]]+)\]");
        Assert.True(statement.Success);
        Assert.Contains("anyPermissionGuard('FinanceWrite', 'ViewReports')", statement.Groups["g"].Value);
    }

    /// <summary>
    /// F9 haritasının hedefleri SPA'da GERÇEK rota. Tanım ekranları tek yardımcıdan (<c>catalogRoute('…')</c>) üretilir;
    /// hem <c>path: '…'</c> hem yardımcı çağrısı metin olarak aranır. Hedefler elle yazılmıştır ve kaynakla seçilir
    /// (konumla değil — paralel kesiş PR'larının blokları araya girebilir).
    /// </summary>
    [Fact]
    public void F9_targets_are_defined_in_SPA_route_files()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        var pages = File.ReadAllText(Path.Combine(app, "sayfalar.ts"));
        Assert.Contains("SERVICE_INSURANCE_ROUTES", pages);
        Assert.Contains("PRICING_ROUTES", pages);
        var serviceRoutes = File.ReadAllText(Path.Combine(app, "features/service-insurance/service-insurance.routes.ts"));
        var pricingRoutes = File.ReadAllText(Path.Combine(app, "features/pricing/pricing.routes.ts"));
        foreach (var path in new[] { "servisler", "regulasyon", "vade" })
            Assert.Contains($"path: '{path}',", serviceRoutes);
        foreach (var path in new[] { "fiyat-hesapla", "maliyet-hesapla", "maliyet-teklifleri", "tarife-aktar" })
            Assert.Contains($"path: '{path}',", pricingRoutes);
        foreach (var path in new[]
                 {
                     "tarifeler", "tarife-gruplari", "sigorta-urunleri", "ek-hizmetler", "tarife-matris", "kira-kurallari",
                     "broker-yasaklari", "servis-tanimlari",
                 })
            Assert.Matches($@"catalogRoute\(\s*'{path}',", pricingRoutes);

        string[] f9Sources =
        [
            "/servisler", "/regulasyon", "/vade", "/servis-tanimlari", "/tarifeler", "/tarife-matris", "/tarife-gruplari",
            "/tarife-aktar", "/sigorta-urunleri", "/kira-kurallari", "/broker-yasaklari", "/fiyat-hesapla", "/maliyet-hesapla",
            "/maliyet-teklifleri", "/ek-hizmetler",
        ];
        var f9Targets = Cutover.Map.Where(e => f9Sources.Contains(e.Kaynak)).Select(e => e.Hedef["/app/".Length..])
            .OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[]
            {
                "broker-yasaklari", "ek-hizmetler", "fiyat-hesapla", "kira-kurallari", "maliyet-hesapla", "maliyet-teklifleri",
                "regulasyon", "servis-tanimlari", "servisler", "sigorta-urunleri", "tarife-aktar", "tarife-gruplari",
                "tarife-matris", "tarifeler", "vade",
            },
            f9Targets);
    }

    /// <summary>
    /// F12 haritasının hedefleri SPA'da GERÇEK rota (<c>app.routes.ts</c> <c>platform</c> + <c>platform.routes.ts</c>
    /// metin olarak). Hedefler elle yazılmıştır ve kaynakla seçilir (konumla değil).
    /// </summary>
    [Fact]
    public void F12_targets_are_defined_in_SPA_route_files()
    {
        var app = Path.Combine(RepoRoot(), "src/RentACar.Frontend/src/app");
        Assert.Contains("path: 'platform',", File.ReadAllText(Path.Combine(app, "app.routes.ts")));
        var text = File.ReadAllText(Path.Combine(app, "features/platform/platform.routes.ts"));
        foreach (var path in new[] { "giris", "kiracilar", "kiracilar/:id", "belgeler" })
            Assert.Contains($"path: '{path}',", text);

        string[] f12Sources = ["/platform/login", "/platform/tenants", "/platform/tenants/{id:guid}", "/platform/belgeler"];
        var f12Targets = Cutover.Map.Where(e => f12Sources.Contains(e.Kaynak))
            .Select(e => e.Hedef.Replace("{id}", ":id", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[]
            {
                "/app/platform/belgeler", "/app/platform/giris", "/app/platform/kiracilar", "/app/platform/kiracilar/:id",
            },
            f12Targets);
        // Platform bloğu dışında hiçbir şablon platform alanında değil (pilotsuz yönlenme yalnız bu dört sayfa).
        Assert.Equal(f12Sources.OrderBy(x => x, StringComparer.Ordinal),
            Cutover.Map.Where(e => Cutover.IsPlatformConsolePath(e.Kaynak.Replace("{id:guid}", "x", StringComparison.Ordinal)))
                .Select(e => e.Kaynak).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("/platform", true)]
    [InlineData("/platform/tenants", true)]
    [InlineData("/PLATFORM/login", true)]
    [InlineData("/platformx", false)]          // segment eşleşmesi, önek değil
    [InlineData("/app/platform/kiracilar", false)]
    [InlineData("/api/ui/v1/platform/kiracilar", false)]
    [InlineData("/", false)]
    public void Platform_console_path_is_segment_match(string path, bool expected)
        => Assert.Equal(expected, Cutover.IsPlatformConsolePath(new PathString(path)));
}

/// <summary>
/// F4.6 ilk kesiş — GERÇEK Web boru hattı (<c>IlkKesisMiddleware</c> + cookie challenge + TenantActive +
/// PlatformIsolation): pilot yönlendirmeleri, negatif liste (önek paylaşan GET uçları, POST, pilot olmayan firma),
/// tek giriş, döngü yokluğu ve platform pilot anahtarı. Beklenen Location değerleri elle yazılmış sabitlerdir.
/// </summary>
[Collection("web")]
public sealed class IlkKesisHostTests(WebFixture fx)
{
    private const string G = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    /// <summary>Yeni arayüzün giriş ucuyla oturum açar (tek giriş) — cookie istemcide kalır.</summary>
    private async Task<HttpClient> SessionAsync(TestKimlik k)
    {
        var c = fx.Web.Client();
        var x = await c.GetAsync("/api/ui/v1/oturum/xsrf");
        var xsrf = CookieValue(x, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF yok");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/v1/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre }),
        };
        request.Headers.Add("X-XSRF-TOKEN", xsrf);
        var r = await c.SendAsync(request);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return c;
    }

    private async Task<HttpClient> PlatformSessionAsync()
    {
        // F13.1a: Blazor platform giriş formu kalktı; yeni arayüzün platform oturum ucu (aynı çerez).
        var c = fx.Web.Client();
        var x = await c.GetAsync("/api/ui/v1/oturum/xsrf");
        var xsrf = CookieValue(x, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF yok");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ui/v1/platform/oturum/giris")
        {
            Content = JsonContent.Create(new { kullanici = fx.Platform.Kullanici, sifre = fx.Platform.Sifre }),
        };
        request.Headers.Add("X-XSRF-TOKEN", xsrf);
        var r = await c.SendAsync(request);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return c;
    }

    private static async Task<string?> LocationAsync(HttpClient c, string url, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        var r = await c.SendAsync(request);
        return r.Headers.Location?.OriginalString;
    }

    private static async Task RedirectsAsync(HttpClient c, string url, string expected, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        var r = await c.SendAsync(request);
        Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"{url}: beklenen 302, gelen {(int)r.StatusCode}");
        Assert.Equal(expected, r.Headers.Location?.OriginalString);
    }

    /// <summary>F13.1b: eski Blazor sayfa adresi → SPA, KALICI (301).</summary>
    private static async Task MovedAsync(HttpClient c, string url, string expected, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        var r = await c.SendAsync(request);
        Assert.True(r.StatusCode == HttpStatusCode.MovedPermanently, $"{url}: beklenen 301, gelen {(int)r.StatusCode}");
        Assert.Equal(expected, r.Headers.Location?.OriginalString);
    }

    /// <summary>302 zincirini elle izler (en çok 8 adım): (adres, durum) listesi. Döngü = aynı adres iki kez.</summary>
    private static async Task<List<(string Adres, HttpStatusCode Durum)>> ChainAsync(HttpClient c, string url)
    {
        var chain = new List<(string, HttpStatusCode)>();
        for (var i = 0; i < 8; i++)
        {
            var r = await c.GetAsync(url);
            chain.Add((url, r.StatusCode));
            if ((int)r.StatusCode is < 300 or >= 400) return chain;
            var next = r.Headers.Location?.OriginalString ?? throw new Xunit.Sdk.XunitException($"{url}: Location yok");
            Assert.True(chain.All(z => z.Item1 != next), "Yönlendirme DÖNGÜSÜ: " + string.Join(" → ", chain.Select(z => z.Item1)) + " → " + next);
            url = next;
        }
        throw new Xunit.Sdk.XunitException("8 adımda bitmeyen yönlendirme zinciri: " + string.Join(" → ", chain.Select(z => z.Item1)));
    }

    // ------------------------------------------------------------ pilot yönlendirme haritası

    [Fact]
    public async Task Old_F4_sayfalari_SPA_ya_301_sorgu_AYNEN()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        const string rent = "?varac=" + G + "&vfrom=2026-10-01T10%3A00&vto=2026-10-04T10%3A00&vgrup=Ekonomi&musteriId=" + G;

        await MovedAsync(c, "/", "/app/panel");
        await MovedAsync(c, "/?df=bugun", "/app/panel?df=bugun");
        await MovedAsync(c, "/kiralar", "/app/kiralar");
        await MovedAsync(c, "/kiralar?q=Y%C4%B1lmaz&bilgi=Kaydedildi", "/app/kiralar?q=Y%C4%B1lmaz&bilgi=Kaydedildi");
        await MovedAsync(c, "/kiralar/yeni" + rent, "/app/kiralar/yeni" + rent);
        await MovedAsync(c, "/kiralar/" + G, "/app/kiralar/" + G);
        await MovedAsync(c, "/kiralar/" + G + "?hata=x", "/app/kiralar/" + G + "?hata=x");
        await MovedAsync(c, "/kiralar/" + G + "/yazdir", "/app/kiralar/" + G + "/yazdir");
        await MovedAsync(c, "/kiralar", "/app/kiralar", HttpMethod.Head);

        // Operatör de (izin kapısı SPA'da/API'de; Blazor sayfaları yalnız [Authorize]).
        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/kiralar/yeni" + rent, "/app/kiralar/yeni" + rent);
    }

    /// <summary>F5.4: rezervasyon modülünün altı Blazor sayfası pilot firmada SPA'ya; Blazor sorgusu AYNEN taşınır.</summary>
    [Fact]
    public async Task Old_F5_sayfalari_SPA_ya_301_sorgu_AYNEN()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        await MovedAsync(c, "/rezervasyonlar", "/app/rezervasyonlar");
        await MovedAsync(c, "/rezervasyonlar?durum=Rezerv&ara=Y%C4%B1lmaz", "/app/rezervasyonlar?durum=Rezerv&ara=Y%C4%B1lmaz");
        await MovedAsync(c, "/rezervasyonlar?vurgu=" + G, "/app/rezervasyonlar?vurgu=" + G); // gelen talep dönüşümü
        await MovedAsync(c, "/teklifler", "/app/teklifler");
        await MovedAsync(c, "/takvim?ay=2026-10", "/app/takvim?ay=2026-10");
        await MovedAsync(c, "/musaitlik?from=2026-10-01&to=2026-10-04", "/app/musaitlik?from=2026-10-01&to=2026-10-04");
        await MovedAsync(c, "/rez-sartlari?musteriId=" + G, "/app/rez-sartlari?musteriId=" + G);
        await MovedAsync(c, "/filo-kiralama/", "/app/filo-kiralama");
        await MovedAsync(c, "/musaitlik", "/app/musaitlik", HttpMethod.Head);

        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/rezervasyonlar", "/app/rezervasyonlar");
        await MovedAsync(op, "/musaitlik", "/app/musaitlik");
    }

    /// <summary>F6.4: araç modülünün 14 Blazor sayfası pilot firmada SPA'ya; Blazor sorgusu AYNEN taşınır.</summary>
    [Fact]
    public async Task Old_F6_sayfalari_SPA_ya_301_sorgu_AYNEN()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        await MovedAsync(c, "/vehicles", "/app/araclar");
        await MovedAsync(c, "/vehicles?q=34&gorunum=grup", "/app/araclar?q=34&gorunum=grup");
        await MovedAsync(c, "/vehicles/detayli?sube=Merkez", "/app/araclar/detayli?sube=Merkez");
        await MovedAsync(c, "/vehicles/" + G, "/app/araclar/" + G);
        await MovedAsync(c, "/vehicles/" + G + "?bilgi=Kaydedildi", "/app/araclar/" + G + "?bilgi=Kaydedildi");
        await MovedAsync(c, "/araclar/" + G, "/app/araclar/" + G + "/detay");
        await MovedAsync(c, "/arac-durum?durum=Kirada", "/app/arac-durum?durum=Kirada");
        await MovedAsync(c, "/arac-sahipleri", "/app/arac-sahipleri");
        await MovedAsync(c, "/segmentler", "/app/segmentler");
        await MovedAsync(c, "/arac-tipleri", "/app/arac-tipleri");
        await MovedAsync(c, "/arac-kredi?durum=Aktif", "/app/arac-kredi?durum=Aktif");
        await MovedAsync(c, "/musteri-taksit", "/app/musteri-taksit");
        await MovedAsync(c, "/arac-siparis", "/app/arac-siparis");
        await MovedAsync(c, "/baf", "/app/baf");
        await MovedAsync(c, "/hasar", "/app/hasar");
        await MovedAsync(c, "/filo-plan/", "/app/filo-plan");
        await MovedAsync(c, "/vehicles", "/app/araclar", HttpMethod.Head);

        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/vehicles", "/app/araclar");
        await MovedAsync(op, "/arac-durum", "/app/arac-durum");
    }

    /// <summary>F7.3: cari/CRM modülünün 8 Blazor sayfası pilot firmada SPA'ya; Blazor sorgusu AYNEN taşınır.</summary>
    [Fact]
    public async Task Old_F7_pages_redirect_301_to_SPA_query_kept()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        await MovedAsync(c, "/cariler", "/app/cariler");
        await MovedAsync(c, "/cariler?ara=Y%C4%B1lmaz&bilgi=Kaydedildi", "/app/cariler?ara=Y%C4%B1lmaz&bilgi=Kaydedildi");
        await MovedAsync(c, "/cariler/" + G, "/app/cariler/" + G);
        await MovedAsync(c, "/cariler/" + G + "/detay", "/app/cariler/" + G + "/detay");
        await MovedAsync(c, "/anketler", "/app/anketler");
        await MovedAsync(c, "/sikayetler?durum=Acik", "/app/sikayetler?durum=Acik");
        await MovedAsync(c, "/assistans", "/app/assistans");
        await MovedAsync(c, "/hukuk/", "/app/hukuk");
        await MovedAsync(c, "/crm", "/app/crm");
        await MovedAsync(c, "/cariler", "/app/cariler", HttpMethod.Head);

        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/cariler", "/app/cariler");
        await MovedAsync(op, "/sikayetler", "/app/sikayetler");
    }

    /// <summary>F10.3: 26 rapor sayfası pilot firmada SPA'ya (aynı ad); Blazor sorgusu AYNEN taşınır.</summary>
    [Fact]
    public async Task Old_F10_sayfalari_SPA_ya_301_sorgu_AYNEN()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        foreach (var code in new[]
                 {
                     "arac-durum-takip", "arac-gunluk-durum", "cari-bakiye", "doluluk", "ek-hizmet", "extre-ozeti",
                     "fatura-donem", "filo-analiz", "filo", "finans-analiz", "gelir-gider", "gunluk", "karlilik",
                     "karsilastirmali-analiz", "kasa-banka", "kdv-listesi", "km-detay", "otomatik-servisler",
                     "periyodik-servis", "personel-calisma", "rezervasyon-kaynak", "servis-ozet", "sigorta-muayene",
                     "tahsilat-fatura", "virman-gecmisi",
                 })
            await MovedAsync(c, "/raporlar/" + code, "/app/raporlar/" + code);
        await MovedAsync(c, "/raporlar/arac-karne/" + G, "/app/raporlar/arac-karne/" + G);
        await MovedAsync(c, "/raporlar/gelir-gider?from=2026-09-01&to=2026-09-30", "/app/raporlar/gelir-gider?from=2026-09-01&to=2026-09-30");
        await MovedAsync(c, "/raporlar/personel-calisma?bas=2026-09-21&bit=2026-09-27&personelFiltre=" + G,
            "/app/raporlar/personel-calisma?bas=2026-09-21&bit=2026-09-27&personelFiltre=" + G);
        await MovedAsync(c, "/raporlar/kasa-banka/", "/app/raporlar/kasa-banka");
        await MovedAsync(c, "/raporlar/gunluk", "/app/raporlar/gunluk", HttpMethod.Head);

        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/raporlar/personel-calisma", "/app/raporlar/personel-calisma");
        await MovedAsync(op, "/raporlar/km-detay", "/app/raporlar/km-detay");
    }

    /// <summary>F9.3: servis/sigorta/vade ve fiyat/tarife modülünün 15 Blazor sayfası pilot firmada SPA'ya (aynı ad).</summary>
    [Fact]
    public async Task Old_F9_pages_redirect_301_to_SPA_query_kept()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        foreach (var path in new[]
                 {
                     "/servisler", "/regulasyon", "/vade", "/servis-tanimlari", "/tarifeler", "/tarife-matris",
                     "/tarife-gruplari", "/tarife-aktar", "/sigorta-urunleri", "/kira-kurallari", "/broker-yasaklari",
                     "/fiyat-hesapla", "/maliyet-hesapla", "/maliyet-teklifleri", "/ek-hizmetler",
                 })
            await MovedAsync(c, path, "/app" + path);
        await MovedAsync(c, "/servisler?durum=Serviste&hata=x", "/app/servisler?durum=Serviste&hata=x");
        await MovedAsync(c, "/vade?plaka=34%20ABC%2012", "/app/vade?plaka=34%20ABC%2012");
        await MovedAsync(c, "/kira-kurallari?q=Yaz&durum=Aktif", "/app/kira-kurallari?q=Yaz&durum=Aktif");
        await MovedAsync(c, "/regulasyon/", "/app/regulasyon");
        await MovedAsync(c, "/vade", "/app/vade", HttpMethod.Head);

        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/servisler", "/app/servisler");
        await MovedAsync(op, "/vade", "/app/vade");
    }

    /// <summary>
    /// F8.3: finans modülünün 19 Blazor sayfası pilot firmada SPA'ya (aynı ad); Blazor sorgusu AYNEN taşınır. Operatör
    /// de yönlenir (Blazor cari ekstresi yalnız [Authorize] idi); izin kapısı SPA rotasında ve API'de.
    /// </summary>
    [Fact]
    public async Task Old_F8_pages_redirect_301_to_SPA_query_kept()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        foreach (var path in new[]
                 {
                     "/kasa", "/finans/nakit-islem", "/finans/bakiye-duzeltme", "/cari-virman", "/depozito", "/tek-cari-toplu",
                     "/toplu-tahsilat", "/toplu-gider", "/otomatik-tahsilat", "/donem-kapanis", "/kurlar", "/faturalar",
                     "/faturalar/detay-listesi", "/cezalar", "/giderler", "/gelen-efatura", "/satislar",
                 })
            await MovedAsync(c, path, "/app" + path);
        await MovedAsync(c, "/cariler/" + G + "/ekstre", "/app/cariler/" + G + "/ekstre");
        await MovedAsync(c, "/faturalar/" + G + "/yazdir", "/app/faturalar/" + G + "/yazdir");
        await MovedAsync(c, "/kasa?hesap=" + G + "&bilgi=Kaydedildi", "/app/kasa?hesap=" + G + "&bilgi=Kaydedildi");
        await MovedAsync(c, "/faturalar?q=Y%C4%B1lmaz&durum=Acik", "/app/faturalar?q=Y%C4%B1lmaz&durum=Acik");
        await MovedAsync(c, "/cariler/" + G + "/ekstre?from=2026-09-01", "/app/cariler/" + G + "/ekstre?from=2026-09-01");
        await MovedAsync(c, "/donem-kapanis/", "/app/donem-kapanis");
        await MovedAsync(c, "/kasa", "/app/kasa", HttpMethod.Head);

        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/cezalar", "/app/cezalar");
        await MovedAsync(op, "/cariler/" + G + "/ekstre", "/app/cariler/" + G + "/ekstre");
    }

    /// <summary>F11.3: 47 tanım/sistem/web sitesi sayfası pilot firmada SPA'ya (aynı ad); Blazor sorgusu AYNEN taşınır.</summary>
    [Fact]
    public async Task Old_F11_pages_redirect_301_to_SPA_query_kept()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        foreach (var path in new[]
                 {
                     "aksesuarlar", "arac-gruplari", "bankalar", "belge-sablonlari", "ceza-turleri", "departmanlar",
                     "doluluk-kurallari", "dovizler", "drop-tanimlari", "gider-turleri", "hesap-kodlari", "hesaplar",
                     "iptal-sebepleri", "kdv-oranlari", "lokasyonlar", "markalar", "musteri-gruplari", "odeme-tipleri",
                     "ozel-kodlar", "personel", "renkler", "rezervasyon-kaynaklari", "sigorta-sirketleri", "subeler",
                     "ulkeler", "vites-turleri", "yakit-turleri", "dokumanlar", "firma-belgeleri", "takvim-abonelik",
                     "ice-aktar", "kullanicilar", "yetki", "ayarlar", "mesaj-sablonlari", "denetim", "bildirimler", "ara",
                     "profil/sifre-degistir", "web-sitesi", "web-sitesi/arac-ekle", "site-icerik", "blog-yonetim",
                     "gelen-talepler",
                 })
            await MovedAsync(c, "/" + path, "/app/" + path);
        await MovedAsync(c, "/web-sitesi/ilan/" + G + "/fiyat", "/app/web-sitesi/ilan/" + G + "/fiyat");
        await MovedAsync(c, "/web-sitesi/ilan/" + G + "/ozellikler?bos=1", "/app/web-sitesi/ilan/" + G + "/ozellikler?bos=1");
        await MovedAsync(c, "/blog-yonetim/" + G + "/onizleme", "/app/blog-yonetim/" + G + "/onizleme");
        await MovedAsync(c, "/gelen-talepler?durum=0", "/app/gelen-talepler?durum=0");
        await MovedAsync(c, "/ara?q=Y%C4%B1lmaz", "/app/ara?q=Y%C4%B1lmaz");
        await MovedAsync(c, "/denetim?entity=Rental&page=2", "/app/denetim?entity=Rental&page=2");
        await MovedAsync(c, "/subeler/", "/app/subeler");
        await MovedAsync(c, "/ayarlar", "/app/ayarlar", HttpMethod.Head);

        // Operatör de (izin kapısı SPA'da/API'de; Blazor sayfaları yalnız [Authorize] ya da kendi politikası).
        var op = await SessionAsync(fx.PilotOperator);
        await MovedAsync(op, "/markalar", "/app/markalar");
        await MovedAsync(op, "/bildirimler", "/app/bildirimler");
        await MovedAsync(op, "/profil/sifre-degistir", "/app/profil/sifre-degistir");
    }

    /// <summary>
    /// F13.1b: pilot kapısı kalktı — eski Blazor adresleri HER oturumda (eski pilot olmayan firma, operatör, oturumsuz)
    /// SPA karşılığına KALICI (301) yönlenir. Oturum kapısı SPA'da ve /api/ui'de (oturumsuz SPA girişe düşer).
    /// </summary>
    [Fact]
    public async Task Pilot_kapisi_yok_eski_adresler_herkes_icin_301()
    {
        // Elle yazılmış oracle: aynı adla /app altına; farklı adlı üç şablon açıkça.
        static string Expected(string url) => url switch
        {
            "/" => "/app/panel",
            "/vehicles" => "/app/araclar",
            "/vehicles/detayli" => "/app/araclar/detayli",
            _ => "/app" + url,
        };
        var sessions = new[] { await SessionAsync(fx.OtherAdmin), await SessionAsync(fx.PilotOperator), fx.Web.Client() };
        foreach (var c in sessions)
            foreach (var url in new[]
                     {
                     "/", "/kiralar", "/kiralar/yeni",
                     "/rezervasyonlar", "/teklifler", "/takvim", "/musaitlik", "/rez-sartlari", "/filo-kiralama",
                     "/vehicles", "/vehicles/detayli", "/arac-durum", "/arac-sahipleri", "/segmentler", "/arac-tipleri",
                     "/arac-kredi", "/musteri-taksit", "/arac-siparis", "/baf", "/hasar", "/filo-plan",
                     "/cariler", "/anketler", "/sikayetler", "/assistans", "/hukuk", "/crm",
                     "/raporlar/gelir-gider", "/raporlar/gunluk", "/raporlar/personel-calisma", "/raporlar/filo",
                     "/markalar", "/arac-gruplari", "/subeler", "/kullanicilar", "/ayarlar", "/bildirimler", "/ara",
                     "/profil/sifre-degistir", "/gelen-talepler", "/blog-yonetim", "/takvim-abonelik", "/dokumanlar",
                     "/servisler", "/regulasyon", "/vade", "/tarifeler", "/tarife-aktar", "/fiyat-hesapla", "/maliyet-teklifleri",
                     "/kasa", "/faturalar", "/giderler", "/cezalar", "/kurlar", "/donem-kapanis", "/cari-virman",
                 })
                await MovedAsync(c, url, Expected(url));
    }

    /// <summary>
    /// Önek paylaşan GET uçlarının TAMAMI (uç tablosundan: <c>/kiralar</c> altındaki her minimal-API GET) + PDF,
    /// hesap, export, makbuz: pilot oturumunda /app'e YÖNLENMEZ. Liste elle değil uç tablosundan — yeni bir
    /// <c>/kiralar/…</c> GET ucu eklenirse kendiliğinden kapsanır.
    /// </summary>
    [Fact]
    public async Task Pilot_onek_paylasan_GET_uclari_yonlenmez()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        var rentalGets = MinimalGetEndpoints()
            .Where(u => u.StartsWith("/kiralar/", StringComparison.OrdinalIgnoreCase)).ToList();
        // Canlıda bilinen: PDF, örnek sözleşme PDF. F13.1a: yalnız Blazor kira formunun kullandığı hesapla, müsait-araç ve
        // dönüş-hesapla GET'leri silindi (yeni arayüz /api/ui/v1/kiralar/… uçlarını kullanır).
        Assert.Contains("/kiralar/{id:guid}/pdf", rentalGets);
        Assert.Contains("/kiralar/ornek-sozlesme/pdf", rentalGets);
        Assert.DoesNotContain("/kiralar/hesapla", rentalGets);
        Assert.DoesNotContain("/kiralar/donus-hesapla", rentalGets);
        Assert.DoesNotContain("/kiralar/musait-arac", rentalGets);

        // F5.4: haritadaki HER kaynak sayfanın altındaki minimal-API GET'leri de (bugün yok; eklenirse kapsanır).
        var prefixes = Cutover.Map.Select(e => e.Kaynak).Where(k => k != "/" && !k.Contains('{')).ToList();
        var subGets = MinimalGetEndpoints()
            .Where(u => prefixes.Any(o => u.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase))).ToList();

        var addresses = rentalGets.Concat(subGets).Distinct().Select(Sample).Concat(
        [
            "/kiralar/" + G + "/pdf?indir=1",
            "/listeler/export/kiralar?format=excel",
            "/listeler/export/kiralar?format=pdf&q=x",
            "/raporlar/export/gelir-gider",
            "/kasa/makbuz/" + G + "/pdf",
            "/faturalar/" + G + "/pdf",
            "/yetkisiz",
            // F8: fatura PDF (indir), makbuz, POST uçlarının GET'i, export
            "/faturalar/" + G + "/pdf?indir=1",
            "/finans/tahsilat",
            "/kurlar/yenile",
            "/listeler/export/faturalar?format=excel",
            "/listeler/export/giderler?format=csv",
            // F7: export
            "/listeler/export/cariler?format=excel",
            // F6: fotoğraf (tam boy + küçük resim), export
            "/vehicles/" + G + "/photos/" + G,
            "/vehicles/" + G + "/photos/" + G + "/thumb",
            "/listeler/export/araclar?format=excel",
            "/listeler/export/arac-kredileri?format=csv",
            // F11: logo, blog kapağı, ilan (sayfanın alt GET'leri yukarıda uç tablosundan)
            "/ayarlar/logo",
            "/blog-yonetim/" + G + "/kapak",
            "/web-sitesi/ilan/" + G,
            // F10: rapor export'ları (Excel/CSV/PDF; karne dahil) YÖNLENMEZ
            "/raporlar/export/gelir-gider?format=excel&from=2026-09-01",
            "/raporlar/export/kasa-banka?format=csv",
            "/raporlar/export/arac-karne?format=pdf&vehicleId=" + G,
            "/raporlar/export/personel-calisma?format=excel",
            // F9: vade export'u (Excel/CSV/PDF) YÖNLENMEZ
            "/listeler/export/vade",
            "/listeler/export/vade?format=csv",
            "/listeler/export/vade?format=pdf",
            // F5: export (liste ekranlarının Excel/CSV/PDF'i), takvim beslemesi, benzer adlı Blazor sayfaları
            "/listeler/export/rezervasyonlar?format=excel",
            "/listeler/export/rezervasyonlar?format=pdf&ara=x",
            "/listeler/export/filo-kiralama?format=csv",
            "/listeler/export/teklifler",
            "/feed/calendar/x.ics",
        ]).ToList();
        var violation = new List<string>();
        foreach (var url in addresses)
        {
            var location = await LocationAsync(c, url);
            // F13.1b: kaydı bulunamayan dosya ucunun gövdesiz 404'ü SPA Panel'ine bulunamadı bandıyla gider (hata hedefi,
            // harita yönlendirmesi değil) — ihlal sayılmaz; harita hedefine (sayfa rotasına) giden yönlendirme ihlaldir.
            if (location is not null && location.StartsWith("/app", StringComparison.OrdinalIgnoreCase)
                && !location.StartsWith("/app/panel?hata=", StringComparison.Ordinal))
                violation.Add($"{url} → {location}");
        }
        Assert.True(violation.Count == 0, "Harita dışı GET ucu SPA'ya yönlendi:\n  " + string.Join("\n  ", violation));
    }

    [Fact]
    public async Task Pilot_POST_yonlenmez()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        foreach (var url in new[]
                 {
                     "/kiralar/create", "/kiralar/update", "/kiralar/yeni", "/kiralar", "/",
                     // F5 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/rezervasyonlar/create", "/rezervasyonlar/update", "/rezervasyonlar/confirm", "/rezervasyonlar/cancel",
                     "/rezervasyonlar/convert", "/teklifler/create", "/teklifler/gonder", "/teklifler/kabul", "/teklifler/reddet",
                     "/filo-kiralama/create", "/filo-kiralama/guncelle", "/filo-kiralama/iptal", "/filo-kiralama/tamamla",
                     "/rez-sartlari/create", "/rez-sartlari/update", "/rez-sartlari/karsilandi", "/rez-sartlari/geri-al",
                     "/rez-sartlari/delete", "/rezervasyonlar", "/musaitlik", "/takvim",
                     // F6 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/vehicles/create", "/vehicles/update", "/vehicles/delete", "/vehicles/km-log",
                     "/vehicles/" + G + "/photos", "/vehicles/" + G + "/photos/" + G + "/sil",
                     "/arac-kredi/create", "/arac-kredi/taksit-ode", "/musteri-taksit/odeme", "/arac-siparis/onayla",
                     "/baf/create", "/hasar/onayla", "/filo-plan/delta", "/segmentler/create", "/arac-tipleri/update",
                     "/arac-sahipleri/delete", "/vehicles", "/vehicles/" + G, "/arac-durum", "/baf",
                     // F7 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/cariler/create", "/cariler/update", "/cariler/delete", "/anketler/create", "/anketler/update",
                     "/anketler/delete", "/sikayetler/create", "/sikayetler/update", "/sikayetler/delete",
                     "/assistans/create", "/assistans/update", "/assistans/delete", "/hukuk/create", "/hukuk/update",
                     "/hukuk/delete", "/cariler", "/cariler/" + G, "/crm", "/hukuk",
                     // F10 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/raporlar/personel-calisma/create", "/raporlar/personel-calisma/update",
                     "/raporlar/personel-calisma/delete", "/raporlar/personel-calisma", "/raporlar/gelir-gider",
                     // F11 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/markalar/create", "/markalar/update", "/markalar/delete", "/arac-gruplari/ata", "/ayarlar/kaydet",
                     "/ayarlar/logo", "/subeler/birlestir", "/kullanicilar/create", "/kullanicilar/sifre",
                     "/personel/update", "/ice-aktar/arac", "/ice-aktar/cari", "/yetki/set", "/bildirim/hepsini-oku",
                     "/takvim/yenile", "/profil/sifre-degistir/kaydet", "/gelen-talepler/donustur",
                     "/web-sitesi/ilan/olustur", "/web-sitesi/ilan/" + G + "/fiyat/kaydet", "/site-icerik/kaydet",
                     "/blog-yonetim/create", "/mesaj-sablonlari/kaydet", "/rezervasyon-kaynaklari/yansit",
                     "/markalar", "/ayarlar", "/kullanicilar", "/web-sitesi", "/ara", "/profil/sifre-degistir",
                     // F8 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST. Dış çağrı yapan
                     // (/kurlar/yenile, /gelen-efatura/sync) ve dönem kilitleyen uç boş formla bile denenmez.
                     "/finans/tahsilat", "/finans/odeme", "/finans/virman", "/finans/cari-virman", "/finans/bakiye-duzeltme",
                     "/finans/fatura-manuel", "/finans/fatura-iade", "/finans/toplu-tahsilat", "/cezalar/create",
                     "/depozito/al", "/giderler/create", "/satislar/create", "/kasa", "/faturalar",
                     "/cariler/" + G + "/ekstre", "/depozito", "/kurlar",
                     // F9 Blazor POST uçları (hedefsiz; bu PR'da silinmedi) + sayfa yollarına POST
                     "/servisler/create", "/servisler/kalem", "/servisler/tamamla", "/servis-yansitma/yansit",
                     "/regulasyon/mtv", "/regulasyon/muayene", "/regulasyon/sigorta", "/regulasyon/zeyil",
                     "/regulasyon-odeme/mtv", "/regulasyon-odeme/sigorta", "/servis-tanimlari/oneri-kabul",
                     "/tarifeler/create", "/tarife-matris/update", "/tarife-gruplari/delete", "/tarife-aktar/yukle",
                     "/tarife-aktar/kanal-sil", "/sigorta-urunleri/create", "/kira-kurallari/update", "/broker-yasaklari/delete",
                     "/ek-hizmetler/create", "/fiyat-hesapla/hesapla", "/maliyet-teklifi/kaydet", "/maliyet-teklifi/delete",
                     "/servisler", "/regulasyon", "/vade", "/fiyat-hesapla", "/maliyet-hesapla",
                 })
        {
            var r = await c.PostAsync(url, new FormUrlEncodedContent([]));
            var location = r.Headers.Location?.OriginalString;
            Assert.False(location?.StartsWith("/app", StringComparison.OrdinalIgnoreCase) == true, $"POST {url} → {location}");
        }
    }

    /// <summary>Uygulamadaki HER minimal-API GET ucunun örnek adresi haritada yok (Razor sayfası olmayan hiçbir GET yönlenemez).</summary>
    [Fact]
    public void Hicbir_minimal_API_GET_ucu_haritada_degil()
    {
        var endpoints = MinimalGetEndpoints();
        Assert.True(endpoints.Count > 40, $"Uç tablosu şüpheli: {endpoints.Count}");
        var violation = endpoints.Where(u => Cutover.SpaPath(Sample(u).Split('?')[0]) is not null).ToList();
        Assert.True(violation.Count == 0, "Haritaya düşen minimal-API GET ucu: " + string.Join(", ", violation));
    }

    private List<string> MinimalGetEndpoints()
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<System.Reflection.MethodInfo>() is not null) // Razor sayfası değil
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>() is not { } m || m.HttpMethods.Contains("GET"))
            .Select(e => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'))
            .Distinct().ToList();

    /// <summary>Rota şablonundan örnek adres: <c>{x:guid}</c> → Guid, <c>{**x}</c> → iki segment, diğer parametre → "x".</summary>
    private static string Sample(string template)
        => Regex.Replace(template, @"\{(?<ad>[^}]+)\}", m =>
        {
            var name = m.Groups["ad"].Value;
            if (name.Contains(":guid", StringComparison.OrdinalIgnoreCase)) return G;
            if (name.StartsWith('*')) return "x/y";
            if (name.Contains(":int", StringComparison.OrdinalIgnoreCase)) return "1";
            return "x";
        });

    // ------------------------------------------------------------ tek giriş + döngü yokluğu

    [Fact]
    public async Task Oturumsuz_login_SPA_girisine_platform_girisi_etkilenmez()
    {
        var c = fx.Web.Client();
        await RedirectsAsync(c, "/login", "/app/giris");
        await RedirectsAsync(c, "/login", "/app/giris", HttpMethod.Head);
        await RedirectsAsync(c, "/login?ReturnUrl=%2Fkiralar%3Fvarac%3Dx", "/app/giris?returnUrl=%2Fkiralar%3Fvarac%3Dx");
        await RedirectsAsync(c, "/login?ReturnUrl=%2F%2Fevil.com", "/app/giris");
        await RedirectsAsync(c, "/login?hata=kapali", "/app/giris?neden=kiraci_kapali");

        // Platform girişi de yeni arayüzde; kalıcı (301), oturum koşulu yok.
        await MovedAsync(c, "/platform/login", "/app/platform/giris");
        await MovedAsync(c, "/platform/login?hata=1", "/app/platform/giris?hata=1");
        await MovedAsync(c, "/platform/login", "/app/platform/giris", HttpMethod.Head);
        var chain = await ChainAsync(c, "/platform/tenants");
        Assert.Equal(new[] { "/platform/tenants", "/app/platform/kiracilar" }, chain.Select(z => z.Adres));

        // F13.1b: cookie challenge'ın hedefi doğrudan SPA girişi (LoginPath = /app/giris); korumalı dosya GET'inde dönüş
        // adresi indirme olduğu için taşınmaz.
        await RedirectsAsync(c, "/listeler/export/kiralar", "/app/giris");
        await RedirectsAsync(c, "/kiralar/" + G + "/pdf", "/app/giris?returnUrl=%2Fkiralar%2F" + G + "%2Fpdf");
    }

    [Fact]
    public async Task Oturumlu_login_hedefi_SPA_platform_konsol()
    {
        // F13.1b: pilot ayrımı yok — her firma kullanıcısı aynı hedefi alır.
        foreach (var who in new[] { fx.PilotAdmin, fx.OtherAdmin })
        {
            var c = await SessionAsync(who);
            await RedirectsAsync(c, "/login", "/app/panel");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fkiralar%3Fvarac%3Dx", "/app/kiralar?varac=x");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fapp%2Fkiralar%3Fq%3Da", "/app/kiralar?q=a");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fvehicles%3Fx%3D1", "/app/araclar?x=1");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fraporlar%2Fgunluk%3Fsube%3DMerkez", "/app/raporlar/gunluk?sube=Merkez");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fbildirimler", "/app/bildirimler");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fkasa", "/app/kasa");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fcariler", "/app/cariler");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fvade%3Fplaka%3Dx", "/app/vade?plaka=x");
            await RedirectsAsync(c, "/login?ReturnUrl=%2F%2Fevil.com", "/app/panel");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fapp%2Fgiris", "/app/panel");
            await RedirectsAsync(c, "/login?ReturnUrl=%2Fplatform%2Ftenants", "/app/panel");
        }

        var platform = await PlatformSessionAsync();
        await RedirectsAsync(platform, "/login", "/app/platform/kiracilar");
    }

    /// <summary>
    /// F12: dört platform konsolu sayfası HER oturumda SPA platform rotasına (F13.1b: 301); sorgu AYNEN. Platform
    /// operatörünün firma sayfası isteği tek adımda SPA konsoluna (PlatformIsolation, 302). Firma kullanıcısı konsola
    /// yönlenir ama platform verisini okuyamaz (kapı /api/ui/v1/platform'da).
    /// </summary>
    [Fact]
    public async Task F12_platform_pages_redirect_to_SPA_for_every_session()
    {
        var platform = await PlatformSessionAsync();
        await MovedAsync(platform, "/platform/tenants", "/app/platform/kiracilar");
        await MovedAsync(platform, "/platform/tenants?ok=1", "/app/platform/kiracilar?ok=1");
        await MovedAsync(platform, "/platform/tenants/" + G, "/app/platform/kiracilar/" + G);
        await MovedAsync(platform, "/platform/belgeler", "/app/platform/belgeler");
        await MovedAsync(platform, "/platform/login", "/app/platform/giris");
        await RedirectsAsync(platform, "/kiralar", "/app/platform/kiracilar"); // PlatformIsolation: tek adım
        await RedirectsAsync(platform, "/", "/app/platform/kiracilar");
        Assert.Null(await LocationAsync(platform, "/app/platform/kiracilar"));

        var other = await SessionAsync(fx.OtherAdmin);
        var chain = await ChainAsync(other, "/platform/tenants");
        Assert.Equal(new[] { "/platform/tenants", "/app/platform/kiracilar" }, chain.Select(z => z.Adres));
        Assert.NotEqual(HttpStatusCode.OK, (await other.GetAsync("/api/ui/v1/platform/kiracilar")).StatusCode);
    }

    /// <summary>
    /// DÖNGÜ YOK (F13.1b): eski adresler tek adımda /app'e (301; /app anonim, challenge almaz — SPA bu host'ta kurulu
    /// değil → 404, önemli olan yönlendirme olmaması). Korumalı dosya GET'leri challenge'la doğrudan /app/giris'e.
    /// Hiçbir zincir 3 adımı aşmaz ve aynı adrese dönmez.
    /// </summary>
    [Fact]
    public async Task Oturumsuz_zincir_app_te_biter_dongu_yok()
    {
        var c = fx.Web.Client();
        foreach (var start in new[]
                 {
                     "/", "/kiralar", "/kiralar/yeni?varac=" + G, "/kiralar/" + G + "/yazdir", "/kiralar/" + G + "/pdf",
                     "/vehicles", "/login", "/login?ReturnUrl=%2Flogin", "/app/giris", "/app/giris?returnUrl=%2Fkiralar",
                     "/rezervasyonlar", "/musaitlik?from=2026-10-01", "/filo-kiralama",
                     "/vehicles/" + G, "/araclar/" + G, "/arac-kredi",
                     "/cariler", "/cariler/" + G + "/detay", "/crm",
                     "/raporlar/gelir-gider", "/raporlar/arac-karne/" + G,
                     "/markalar", "/ayarlar", "/profil/sifre-degistir", "/web-sitesi/ilan/" + G + "/fiyat",
                     "/gelen-talepler?durum=0", "/servisler", "/vade", "/tarifeler",
                     "/kasa", "/faturalar/" + G + "/yazdir", "/cariler/" + G + "/ekstre",
                     "/platform/tenants", "/hata?mesaj=x", "/yetkisiz", "/Error", "/not-found", "/boyle-bir-sayfa-yok",
                 })
        {
            var chain = await ChainAsync(c, start);
            var last = chain[^1];
            Assert.True(last.Adres.StartsWith("/app/", StringComparison.Ordinal),
                $"{start}: zincir /app'te bitmedi: " + string.Join(" → ", chain.Select(z => $"{z.Adres} ({(int)z.Durum})")));
            Assert.True(chain.Count <= 3, $"{start}: {chain.Count} adım");
        }
    }

    [Theory]
    [InlineData("/app/giris")]
    [InlineData("/app/giris?returnUrl=%2Fkiralar%3Fvarac%3Dx")]
    [InlineData("/app/giris?neden=kiraci_kapali")]
    [InlineData("/app/")]
    [InlineData("/app/panel")]
    [InlineData("/app/kiralar/yeni?varac=x")]
    public async Task App_yollari_oturumsuz_challenge_almaz(string url)
    {
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var request = new HttpRequestMessage(method, url);
            var r = await fx.Web.Client().SendAsync(request);
            Assert.Null(r.Headers.Location);
            Assert.NotEqual(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    [Fact]
    public async Task Oturumlu_zincir_dongu_yok()
    {
        foreach (var who in new[] { fx.PilotAdmin, fx.OtherAdmin })
        {
            var c = await SessionAsync(who);
            Assert.Equal(new[] { "/login", "/app/panel" }, (await ChainAsync(c, "/login")).Select(z => z.Adres));
            Assert.Equal(new[] { "/", "/app/panel" }, (await ChainAsync(c, "/")).Select(z => z.Adres));
            Assert.Equal(new[] { "/login?ReturnUrl=%2Fapp%2Fkiralar", "/app/kiralar" },
                (await ChainAsync(c, "/login?ReturnUrl=%2Fapp%2Fkiralar")).Select(z => z.Adres));
        }
    }

    [Fact]
    public async Task Kapatilan_firmanin_oturumu_SPA_girisine_mesajla_duser_dongu_yok()
    {
        var k = await fx.CompanyAndUserAsync("Kapanacak Firma");
        var id = await fx.TenantIdAsync(k.Firma);
        var c = await SessionAsync(k);
        await MovedAsync(c, "/kiralar", "/app/kiralar");

        await using (var conn = new NpgsqlConnection(fx.Pg.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("UPDATE \"Tenants\" SET \"IsActive\" = false WHERE \"Id\" = @i", conn);
            cmd.Parameters.AddWithValue("i", id);
            await cmd.ExecuteNonQueryAsync();
        }
        using (var scope = fx.Web.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<TenantStatusCache>().Invalidate(id);

        var chain = await ChainAsync(c, "/kiralar");
        Assert.Equal(new[] { "/kiralar", "/login?hata=kapali", "/app/giris?neden=kiraci_kapali" }, chain.Select(z => z.Adres));
    }

    // ------------------------------------------------------------ F13.1b: kabuk sayfaları, 404 ve 403 SPA'da

    // Elle yazılmış oracle: hata bandı KOD taşır (SPA kodu sabit çeviri metnine çevirir; serbest metin taşınmaz).
    private const string NoPermission = "/app/panel?hata=yetki_yok";
    private const string NotFound = "/app/panel?hata=bulunamadi";

    /// <summary>
    /// Eski kabuk sayfaları ve gövdesiz 404 yeni arayüzün Panel'ine hata KODUYLA (302). Hedef yol ve kod SABİT; kullanıcı
    /// girdisi (<c>?mesaj=</c>) TAŞINMAZ — açık yönlendirme ve içerik sahteciliği (kimlik avı) yok. Uzantılı dosya isteği,
    /// /api/ui ve GET dışı istek ham durum kodunu alır (tarayıcının arka plan isteği SPA'ya yönlenmez).
    /// </summary>
    [Fact]
    public async Task Shell_pages_and_404_go_to_SPA_panel_with_error_code()
    {
        var c = fx.Web.Client();
        // Serbest metin yok sayılır (L2): saldırganın metni bizim bandımıza taşınmaz.
        await RedirectsAsync(c, "/hata?mesaj=Hesab%C4%B1n%C4%B1z%20ask%C4%B1ya%20al%C4%B1nd%C4%B1%2C%20aray%C4%B1n", "/app/panel?hata=dogrulama");
        await RedirectsAsync(c, "/hata", "/app/panel?hata=dogrulama");
        await RedirectsAsync(c, "/yetkisiz?x=1", NoPermission);
        await RedirectsAsync(c, "/Error", "/app/panel?hata=beklenmeyen");
        await RedirectsAsync(c, "/not-found", NotFound);
        await RedirectsAsync(c, "/boyle-bir-sayfa-yok?q=1", NotFound);

        // Açık yönlendirme denemeleri: hedef daima /app/panel (yol sabit), girdi taşınmaz.
        // ("//evil.com" HttpClient'ta şema-göreli URL olarak başka host'a gider; Kestrel'e aynı yol "/%2F%2Fevil.com" ile.)
        foreach (var evil in new[] { "/hata?mesaj=https://evil.com", "/hata?mesaj=%2F%2Fevil.com", "/%2F%2Fevil.com", "/%5Cevil.com" })
        {
            // Ya yönlendirme yok (".com" uzantılı yol dosya isteği sayılır → ham 404) ya da sabit Panel yolu.
            var location = await LocationAsync(c, evil);
            Assert.True(location is null || location.StartsWith("/app/panel?hata=", StringComparison.Ordinal), $"{evil} → {location}");
            Assert.DoesNotContain("evil", location ?? "", StringComparison.OrdinalIgnoreCase);
        }

        // Ham durum kodu: dosya isteği, API, GET dışı.
        var favicon = await c.GetAsync("/favicon.ico");
        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        Assert.Null(favicon.Headers.Location);
        var api = await c.GetAsync("/api/ui/v1/boyle-bir-uc-yok");
        Assert.Null(api.Headers.Location);
        Assert.Null(await LocationAsync(c, "/boyle-bir-sayfa-yok", HttpMethod.Post));
    }

    /// <summary>F13.1b: 403 (girişli ama yetkisiz dosya GET'i) → SPA Panel + yetki bandı; eski /yetkisiz sayfası yok.</summary>
    [Fact]
    public async Task Forbidden_download_goes_to_SPA_panel_with_permission_band()
    {
        var op = await SessionAsync(fx.PilotOperator); // Operatör: ViewReports yok → rapor export'u 403
        await RedirectsAsync(op, "/raporlar/export/gelir-gider", NoPermission);
    }

    /// <summary>
    /// L1 (F13 sonrası güvenlik): eski form çıkış ucu üretimde CSRF korumasızdı (minimal API form bağlamayan uca
    /// antiforgery doğrulaması eklemez) — başka bir siteden gelen başlıksız POST oturumu kapatabiliyordu. Uç KALDIRILDI.
    /// Başlıksız çapraz POST'lar (eski uç ve yeni oturum ucu) oturumu KAPATMAZ; yeni uç X-XSRF-TOKEN ister.
    /// </summary>
    [Fact]
    public async Task Cross_site_logout_without_xsrf_header_does_not_sign_out()
    {
        var c = await SessionAsync(fx.PilotAdmin);
        foreach (var url in new[] { "/auth/logout", "/api/ui/v1/oturum/cikis" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent([]) };
            request.Headers.Add("Origin", "https://evil.example");
            var r = await c.SendAsync(request);
            Assert.False(r.IsSuccessStatusCode, $"{url}: {(int)r.StatusCode}");
            Assert.False(r.Headers.TryGetValues("Set-Cookie", out var cookies)
                && cookies.Any(v => v.StartsWith("racar.session=;", StringComparison.Ordinal)), $"{url}: çerez silindi");
        }
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/ui/v1/oturum/ben")).StatusCode); // oturum yerinde
    }

    /// <summary>
    /// Müşteri bağlantısı (<c>/sozlesme/{token}</c>): geçersiz ya da iptal edilmiş token personel girişine/Panel'e
    /// YÖNLENMEZ — sade metinli 404 (müşteri personel ekranını görmez). iCal beslemesi de aynı.
    /// </summary>
    [Theory]
    [InlineData("/sozlesme/gecersiz-token")]
    [InlineData("/feed/calendar/gecersiz-token.ics")]
    public async Task Invalid_public_link_is_plain_404_not_staff_ui(string url)
    {
        var r = await fx.Web.Client().GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Null(r.Headers.Location);
        if (!url.EndsWith(".ics", StringComparison.Ordinal))
            Assert.Equal(Cutover.PublicLinkNotFoundText, await r.Content.ReadAsStringAsync());
    }
}
