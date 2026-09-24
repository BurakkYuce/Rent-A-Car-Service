using RentACar.Application.Authorization;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Menu;

/// <summary>
/// Menü öğesi (F1.6, roadmap "İmzalar").
/// <list type="bullet">
/// <item><c>Rota</c>: bağlantı (Blazor ya da SPA rotası).</item>
/// <item><c>Grup</c>: menü grubu (<see cref="MenuKaydi.Kok"/> = grupsuz tek başına öğe;
/// <see cref="MenuKaydi.KisaYollar"/> = hızlı bağlantılar).</item>
/// <item><c>Sira</c>: menünün TAMAMINDA görüntülenme sırası (gruplar ilk öğelerinin sırasıyla dizilir).</item>
/// <item><c>Sahip</c>: sayfayı kim çiziyor — <see cref="MenuKaydi.Blazor"/> ya da faz kesişinden sonra <see cref="MenuKaydi.Spa"/>
/// (F4.6: Panel, Kiralar, Yeni Kira; F5.4: Rezervasyon grubu, Teklifler, Filo Kiralama, iki kısa yol; F6.4: Araçlar
/// grubu + Tanımlar'daki Araç Tipleri ve Segmentler; F7.3: Cariler &amp; CRM grubunun F7 sayfaları; F10.3: Raporlar
/// grubu; F9.3: Servis &amp; Sigorta ve Fiyat &amp; Tarife grupları + Vade Panosu; F8.3: Finans grubu). <c>spa</c> öğesinin <c>Rota</c>'sı SPA adresidir (<c>/app/…</c>); Blazor menüsü pilot
/// OLMAYAN firmada bunun Blazor karşılığını (<see cref="RentACar.Web.Spa.IlkKesis.BlazorKarsiligi"/>) açar.</item>
/// <item><c>Izin</c>: öğeyi görmek için gereken etkin izin (null = oturum açmış herkes). Sayfanın kendi
/// yetkisinden türetilir (<c>MenuKaydiTests</c> kilitler).</item>
/// <item><c>Modul</c>: satın alınabilir modül bayrağı (ör. <see cref="ModulMetadata.WebSitesi"/>); kapalıysa öğe gizli.</item>
/// <item><c>RozetKodu</c>: sayaç rozeti (<see cref="MenuKaydi.RozetOkunmamisBildirim"/>, <see cref="MenuKaydi.RozetYeniTalep"/>).</item>
/// </list>
/// </summary>
public sealed record MenuOgesi(
    string Rota, string Etiket, string Grup, int Sira, string Sahip,
    Permission? Izin, string? Modul, string? RozetKodu, bool HizliBaglanti);

/// <summary>
/// Blazor <c>MainLayout</c> menüsünün TAMAMININ kaydı (F1.6) — gruplar, sıra, hızlı bağlantılar, rozetler,
/// modül bayrakları ve her öğenin izni. Yeni arayüz kabuğu (F3.2) menüyü buradan (<c>GET /api/ui/v1/menu</c>)
/// okur; Blazor <c>MainLayout</c> da F4.6'dan beri menüyü BURADAN çizer (tek kaynak, iki arayüz).
/// <para><b>Kayma çiti:</b> <c>MenuKaydiTests</c> kaydı F4.6 öncesi MainLayout menüsünün dondurulmuş kopyasıyla
/// (<c>tests/…/Oracle/menu-f46-oncesi.tsv</c>) karşılaştırır — öğe ekleyen iki yeri AYNI PR'da değiştirir.</para>
/// <para><b>İzin kuralı</b> (bugünkü rol kapısından türetilmiş): sayfa <c>izin:X</c> politikası taşıyorsa X;
/// yalnız Admin rollü sayfa ManageUsers; aksi halde grubun rol kapısının izin karşılığı
/// (Operasyon grupları → OperationsWrite, Finans → FinanceWrite, Raporlar → ViewReports, Sistem → ManageUsers).
/// Rol → izin geçişi bilinçlidir: kullanıcı-bazlı istisnalar menüye yansır ve açılamayacak öğe gösterilmez
/// (Blazor menüsü bu davranışa F4.6'da geçti — önce/sonra tablosu F4.6 PR'ında).</para>
/// </summary>
public static class MenuKaydi
{
    public const string Blazor = "blazor";
    /// <summary>F4.6: sayfa yeni arayüzde (Angular). Rota <c>/app/…</c>.</summary>
    public const string Spa = "spa";
    public const string Kok = "";
    public const string KisaYollar = "Kısa Yollar";
    public const string RozetOkunmamisBildirim = "okunmamis-bildirim";
    public const string RozetYeniTalep = "yeni-talep";

    public static IReadOnlyList<MenuOgesi> Ogeler { get; } = Olustur();

    private static List<MenuOgesi> Olustur()
    {
        const Permission OW = Permission.OperationsWrite;
        const Permission FW = Permission.FinanceWrite;
        const Permission VR = Permission.ViewReports;
        const Permission MU = Permission.ManageUsers;
        const string WS = ModulMetadata.WebSitesi;

        var l = new List<MenuOgesi>();
        void E(string grup, string rota, string etiket, Permission? izin, string? modul = null, string? rozet = null, bool hizli = false)
            => l.Add(new MenuOgesi(rota, etiket, grup, (l.Count + 1) * 10,
                SpaBarindirmaYolu(rota) ? Spa : Blazor, izin, modul, rozet, hizli));

        // ---- Kısa yollar (MainLayout: Roles="Admin,Yonetici,Operator")
        E(KisaYollar, "/app/rezervasyonlar", "Yeni Rezervasyon", OW, hizli: true); // F5.4: spa
        E(KisaYollar, "/app/kiralar/yeni", "Yeni Kira", OW, hizli: true); // F4.6: spa
        E(KisaYollar, "/app/musaitlik", "Müsaitlik-Rez Açma", OW, hizli: true); // F5.4: spa

        E(Kok, "/app/panel", "Panel", null); // F4.6: spa

        // ---- Operasyon grupları (MainLayout: Roles="Admin,Yonetici,Operator" → OperationsWrite)
        const string Arac = "Araçlar";
        E(Arac, "/app/araclar", "Araç Listesi", OW); // F6.4: grubun tamamı spa
        E(Arac, "/app/araclar/detayli", "Detaylı Araç Listesi", VR);
        E(Arac, "/app/arac-durum", "Araç Durum", OW);
        E(Arac, "/app/arac-siparis", "Araç Sipariş", OW);
        E(Arac, "/app/filo-plan", "Filo Plan Hedefi", OW);
        E(Arac, "/app/arac-kredi", "Araç Kredisi", OW);
        E(Arac, "/app/musteri-taksit", "Müşteri Taksitleri", FW);
        E(Arac, "/app/baf", "BAF (Tahsis)", OW);
        E(Arac, "/app/hasar", "Hasar", OW);
        E(Arac, "/app/arac-tipleri", "Araç Tipleri", OW);
        E(Arac, "/app/arac-sahipleri", "Araç Sahipleri", OW);
        E(Arac, "/app/segmentler", "Segmentler", OW);

        const string Kira = "Kira";
        E(Kira, "/app/kiralar", "Kiralar", OW); // F4.6: spa
        E(Kira, "/app/teklifler", "Teklifler", OW); // F5.4: spa
        E(Kira, "/app/filo-kiralama", "Filo Kiralama", OW); // F5.4: spa

        const string Rez = "Rezervasyon";
        E(Rez, "/app/rezervasyonlar", "Rezervasyonlar", OW); // F5.4: grubun tamamı spa
        E(Rez, "/app/takvim", "Takvim", OW);
        E(Rez, "/app/musaitlik", "Müsaitlik", OW);
        E(Rez, "/app/rez-sartlari", "Rez Şartları", OW);

        const string Cari = "Cariler & CRM";
        E(Cari, "/app/cariler", "Cariler", OW); // F7.3: F7 sayfaları spa (Blog, Gelen Talepler F11/F12'nin)
        E(Cari, "/app/crm", "CRM Analiz", VR);
        E(Cari, "/app/sikayetler", "Şikayetler", OW);
        E(Cari, "/app/assistans", "Assistans Talepleri", OW);
        E(Cari, "/app/hukuk", "Hukuk", OW);
        E(Cari, "/app/anketler", "Anketler", OW);
        E(Cari, "/blog-yonetim", "Blog", OW);
        E(Cari, "/gelen-talepler", "Gelen Talepler", OW, rozet: RozetYeniTalep);

        const string Web = "Web Sitesi"; // PR-12: yalnız modülü satın almış firmada
        E(Web, "/web-sitesi", "İlanlar", OW, WS);
        E(Web, "/site-icerik", "Site İçeriği", OW, WS);
        E(Web, "/gelen-talepler", "Gelen Talepler", OW, WS, RozetYeniTalep);
        E(Web, "/blog-yonetim", "Blog", OW, WS);

        const string Servis = "Servis & Sigorta";
        E(Servis, "/app/servisler", "Servis", OW); // F9.3: Servis & Sigorta ve Fiyat & Tarife gruplarının tamamı spa
        E(Servis, "/app/servis-tanimlari", "Servis Tanımları", OW);
        E(Servis, "/app/regulasyon", "Sigorta / MTV / Muayene", OW);

        const string Fiyat = "Fiyat & Tarife";
        E(Fiyat, "/app/tarifeler", "Tarifeler", OW);
        E(Fiyat, "/app/tarife-matris", "Tarife Matrisi", OW);
        E(Fiyat, "/app/tarife-gruplari", "Tarife Grupları", OW);
        E(Fiyat, "/app/tarife-aktar", "Tarife İçe Aktar", MU); // MainLayout: Roles="Admin"
        E(Fiyat, "/app/sigorta-urunleri", "Sigorta Ürünleri", OW);
        E(Fiyat, "/app/kira-kurallari", "Kiralama Kuralları", OW);
        E(Fiyat, "/app/broker-yasaklari", "Broker Yasakları", OW);
        E(Fiyat, "/app/fiyat-hesapla", "Fiyat Hesapla", OW);
        E(Fiyat, "/app/maliyet-hesapla", "Maliyet Hesapla", FW);
        E(Fiyat, "/app/maliyet-teklifleri", "Maliyet Teklifleri", FW);
        E(Fiyat, "/app/ek-hizmetler", "Ek Hizmetler", OW);

        const string Tanim = "Tanımlar";
        E(Tanim, "/markalar", "Markalar", OW);
        E(Tanim, "/arac-gruplari", "Araç Grupları", OW);
        E(Tanim, "/app/arac-tipleri", "Araç Tipleri", OW); // F6.4: spa (F6 sayfası)
        E(Tanim, "/lokasyonlar", "Lokasyonlar", OW);
        E(Tanim, "/yakit-turleri", "Yakıt Türleri", OW);
        E(Tanim, "/vites-turleri", "Vites Türleri", OW);
        E(Tanim, "/renkler", "Renkler", OW);
        E(Tanim, "/app/segmentler", "Segmentler", OW); // F6.4: spa (F6 sayfası)
        E(Tanim, "/musteri-gruplari", "Müşteri Grupları", OW);
        E(Tanim, "/sigorta-sirketleri", "Sigorta Şirketleri", OW);
        E(Tanim, "/bankalar", "Bankalar", OW);
        E(Tanim, "/departmanlar", "Departmanlar", OW);
        E(Tanim, "/odeme-tipleri", "Ödeme Tipleri", OW);
        E(Tanim, "/ulkeler", "Ülkeler", OW);
        E(Tanim, "/aksesuarlar", "Aksesuarlar", OW);
        E(Tanim, "/iptal-sebepleri", "İptal Sebepleri", OW);
        E(Tanim, "/rezervasyon-kaynaklari", "Rez. Kaynakları", OW);
        E(Tanim, "/ceza-turleri", "Ceza Türleri", OW);
        E(Tanim, "/kdv-oranlari", "KDV Oranları", OW);
        E(Tanim, "/gider-turleri", "Gider Türleri", OW);
        E(Tanim, "/hesaplar", "Hesaplar", OW);
        E(Tanim, "/hesap-kodlari", "Hesap Kodları", OW);
        E(Tanim, "/ozel-kodlar", "Özel Kodlar", OW);
        E(Tanim, "/dovizler", "Dövizler", OW);
        E(Tanim, "/drop-tanimlari", "Drop Matris", OW);
        E(Tanim, "/doluluk-kurallari", "Doluluk Fiyat Kuralları", OW);

        // ---- Finans (MainLayout: Roles="Admin,Yonetici,Muhasebe" → FinanceWrite)
        const string Finans = "Finans";
        E(Finans, "/app/kasa", "Kasa / Banka", FW); // F8.3: grubun tamamı spa
        E(Finans, "/app/finans/nakit-islem", "Nakit İşlem", FW);
        E(Finans, "/app/finans/bakiye-duzeltme", "Bakiye Düzeltme", FW);
        E(Finans, "/app/kurlar", "Döviz Kurları", FW);
        E(Finans, "/app/faturalar", "Faturalar", FW);
        E(Finans, "/app/faturalar/detay-listesi", "Fatura Detay Listesi", FW);
        E(Finans, "/app/gelen-efatura", "Gelen e-Fatura", FW);
        E(Finans, "/app/giderler", "Giderler", FW);
        E(Finans, "/app/satislar", "Satışlar", FW);
        E(Finans, "/app/cari-virman", "Cari Virman", FW);
        E(Finans, "/app/depozito", "Depozito", FW);
        E(Finans, "/app/toplu-tahsilat", "Toplu Tahsilat", FW);
        E(Finans, "/app/tek-cari-toplu", "Tek Cari Toplu Kapatma", FW);
        E(Finans, "/app/otomatik-tahsilat", "Otomatik Tahsilat", FW);
        E(Finans, "/app/toplu-gider", "Toplu Gider", FW);
        E(Finans, "/app/cezalar", "Cezalar", FW);
        E(Finans, "/app/donem-kapanis", "Dönem Kapanışı", FW);

        // ---- Raporlar (MainLayout: Roles="Admin,Yonetici,Muhasebe" → ViewReports). F10.3: grubun tamamı spa.
        const string Rapor = "Raporlar";
        E(Rapor, "/app/raporlar/finans-analiz", "Finans Analiz", VR);
        E(Rapor, "/app/raporlar/gelir-gider", "Gelir-Gider", VR);
        E(Rapor, "/app/raporlar/karlilik", "Kârlılık", VR);
        E(Rapor, "/app/raporlar/filo-analiz", "Filo Analiz", VR);
        E(Rapor, "/app/raporlar/gunluk", "Günlük", VR);
        E(Rapor, "/app/raporlar/cari-bakiye", "Cari Bakiye", VR);
        E(Rapor, "/app/raporlar/extre-ozeti", "Extre Özeti", VR);
        E(Rapor, "/app/raporlar/filo", "Filo", VR);
        E(Rapor, "/app/raporlar/doluluk", "Doluluk", VR);
        E(Rapor, "/app/raporlar/tahsilat-fatura", "Tahsilat-Fatura", VR);
        E(Rapor, "/app/raporlar/kdv-listesi", "KDV Listesi", VR);
        E(Rapor, "/app/raporlar/ek-hizmet", "Ek Hizmet", VR);
        E(Rapor, "/app/raporlar/periyodik-servis", "Periyodik Servis", VR);
        E(Rapor, "/app/raporlar/km-detay", "KM Detay", VR);
        E(Rapor, "/app/raporlar/rezervasyon-kaynak", "Rezervasyon Kaynak", VR);
        E(Rapor, "/app/raporlar/karsilastirmali-analiz", "Karşılaştırmalı Analiz", VR);
        E(Rapor, "/app/raporlar/fatura-donem", "Fatura Dönem", VR);
        E(Rapor, "/app/raporlar/arac-durum-takip", "Araç Durum Takip", VR);
        E(Rapor, "/app/raporlar/arac-gunluk-durum", "Araç Günlük Durum", VR);
        E(Rapor, "/app/raporlar/personel-calisma", "Personel Çalışma (Vardiya)", VR);
        E(Rapor, "/app/raporlar/sigorta-muayene", "Sigorta / Muayene Envanteri", VR);
        E(Rapor, "/app/raporlar/kasa-banka", "Kasa-Banka Defteri", VR);
        E(Rapor, "/app/raporlar/virman-gecmisi", "Virman Geçmişi", VR);
        E(Rapor, "/app/raporlar/servis-ozet", "Servis Özet", VR);
        E(Rapor, "/app/raporlar/otomatik-servisler", "Otomatik Servisler", VR);

        // ---- Tüm roller (grupsuz)
        E(Kok, "/app/vade", "Vade Panosu", null); // F9.3: spa
        E(Kok, "/bildirimler", "Bildirimler", null, rozet: RozetOkunmamisBildirim);
        E(Kok, "/takvim-abonelik", "Takvim Aboneliği", null);
        E(Kok, "/firma-belgeleri", "Firma Belgeleri", null);
        E(Kok, "/dokumanlar", "Dokümanlar", null);

        // ---- Sistem (MainLayout: Roles="Admin" → ManageUsers)
        const string Sistem = "Sistem";
        E(Sistem, "/subeler", "Şubeler", MU);
        E(Sistem, "/kullanicilar", "Kullanıcılar", MU);
        E(Sistem, "/personel", "Personel", MU);
        E(Sistem, "/ayarlar", "Ayarlar", MU);
        E(Sistem, "/mesaj-sablonlari", "Mesaj Şablonları", MU);
        E(Sistem, "/belge-sablonlari", "Belge Şablonları", MU);
        E(Sistem, "/yetki", "Ekran Yetkileri", MU);
        E(Sistem, "/denetim", "Denetim", MU);
        E(Sistem, "/ice-aktar", "Veri İçe Aktar", MU);
        return l;
    }

    /// <summary>Rota yeni arayüzün mü (<c>/app</c> ya da altı)? Sahip bundan türer — elle yazılmaz, kayamaz.</summary>
    private static bool SpaBarindirmaYolu(string rota) => RentACar.Web.Spa.IlkKesis.SpaYoluMu(rota);
}
