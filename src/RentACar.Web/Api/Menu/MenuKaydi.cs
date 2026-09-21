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
/// <item><c>Sahip</c>: sayfayı kim çiziyor — bugün hepsi <see cref="MenuKaydi.Blazor"/>; faz kesişinde <c>spa</c> olur.</item>
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
/// okur; Blazor F4.6'ya dek kendi menüsünü çizer.
/// <para><b>Kayma çiti:</b> <c>MenuKaydiTests</c> MainLayout'taki her menü bağlantısının burada tam bir
/// karşılığı olduğunu (ve tersini) sayarak doğrular — MainLayout'a öğe ekleyen buraya da eklemek zorunda.</para>
/// <para><b>İzin kuralı</b> (bugünkü rol kapısından türetilmiş): sayfa <c>izin:X</c> politikası taşıyorsa X;
/// yalnız Admin rollü sayfa ManageUsers; aksi halde grubun rol kapısının izin karşılığı
/// (Operasyon grupları → OperationsWrite, Finans → FinanceWrite, Raporlar → ViewReports, Sistem → ManageUsers).
/// Rol → izin geçişi bilinçlidir: kullanıcı-bazlı istisnalar menüye yansır ve açılamayacak öğe gösterilmez
/// (Blazor menüsü bu davranışa F4.6'da geçer).</para>
/// </summary>
public static class MenuKaydi
{
    public const string Blazor = "blazor";
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
            => l.Add(new MenuOgesi(rota, etiket, grup, (l.Count + 1) * 10, Blazor, izin, modul, rozet, hizli));

        // ---- Kısa yollar (MainLayout: Roles="Admin,Yonetici,Operator")
        E(KisaYollar, "/rezervasyonlar", "Yeni Rezervasyon", OW, hizli: true);
        E(KisaYollar, "/kiralar/yeni", "Yeni Kira", OW, hizli: true);
        E(KisaYollar, "/musaitlik", "Müsaitlik-Rez Açma", OW, hizli: true);

        E(Kok, "/", "Panel", null);

        // ---- Operasyon grupları (MainLayout: Roles="Admin,Yonetici,Operator" → OperationsWrite)
        const string Arac = "Araçlar";
        E(Arac, "/vehicles", "Araç Listesi", OW);
        E(Arac, "/vehicles/detayli", "Detaylı Araç Listesi", VR);
        E(Arac, "/arac-durum", "Araç Durum", OW);
        E(Arac, "/arac-siparis", "Araç Sipariş", OW);
        E(Arac, "/filo-plan", "Filo Plan Hedefi", OW);
        E(Arac, "/arac-kredi", "Araç Kredisi", OW);
        E(Arac, "/musteri-taksit", "Müşteri Taksitleri", FW);
        E(Arac, "/baf", "BAF (Tahsis)", OW);
        E(Arac, "/hasar", "Hasar", OW);
        E(Arac, "/arac-tipleri", "Araç Tipleri", OW);
        E(Arac, "/arac-sahipleri", "Araç Sahipleri", OW);
        E(Arac, "/segmentler", "Segmentler", OW);

        const string Kira = "Kira";
        E(Kira, "/kiralar", "Kiralar", OW);
        E(Kira, "/teklifler", "Teklifler", OW);
        E(Kira, "/filo-kiralama", "Filo Kiralama", OW);

        const string Rez = "Rezervasyon";
        E(Rez, "/rezervasyonlar", "Rezervasyonlar", OW);
        E(Rez, "/takvim", "Takvim", OW);
        E(Rez, "/musaitlik", "Müsaitlik", OW);
        E(Rez, "/rez-sartlari", "Rez Şartları", OW);

        const string Cari = "Cariler & CRM";
        E(Cari, "/cariler", "Cariler", OW);
        E(Cari, "/crm", "CRM Analiz", VR);
        E(Cari, "/sikayetler", "Şikayetler", OW);
        E(Cari, "/assistans", "Assistans Talepleri", OW);
        E(Cari, "/hukuk", "Hukuk", OW);
        E(Cari, "/anketler", "Anketler", OW);
        E(Cari, "/blog-yonetim", "Blog", OW);
        E(Cari, "/gelen-talepler", "Gelen Talepler", OW, rozet: RozetYeniTalep);

        const string Web = "Web Sitesi"; // PR-12: yalnız modülü satın almış firmada
        E(Web, "/web-sitesi", "İlanlar", OW, WS);
        E(Web, "/site-icerik", "Site İçeriği", OW, WS);
        E(Web, "/gelen-talepler", "Gelen Talepler", OW, WS, RozetYeniTalep);
        E(Web, "/blog-yonetim", "Blog", OW, WS);

        const string Servis = "Servis & Sigorta";
        E(Servis, "/servisler", "Servis", OW);
        E(Servis, "/servis-tanimlari", "Servis Tanımları", OW);
        E(Servis, "/regulasyon", "Sigorta / MTV / Muayene", OW);

        const string Fiyat = "Fiyat & Tarife";
        E(Fiyat, "/tarifeler", "Tarifeler", OW);
        E(Fiyat, "/tarife-matris", "Tarife Matrisi", OW);
        E(Fiyat, "/tarife-gruplari", "Tarife Grupları", OW);
        E(Fiyat, "/tarife-aktar", "Tarife İçe Aktar", MU); // MainLayout: Roles="Admin"
        E(Fiyat, "/sigorta-urunleri", "Sigorta Ürünleri", OW);
        E(Fiyat, "/kira-kurallari", "Kiralama Kuralları", OW);
        E(Fiyat, "/broker-yasaklari", "Broker Yasakları", OW);
        E(Fiyat, "/fiyat-hesapla", "Fiyat Hesapla", OW);
        E(Fiyat, "/maliyet-hesapla", "Maliyet Hesapla", FW);
        E(Fiyat, "/maliyet-teklifleri", "Maliyet Teklifleri", FW);
        E(Fiyat, "/ek-hizmetler", "Ek Hizmetler", OW);

        const string Tanim = "Tanımlar";
        E(Tanim, "/markalar", "Markalar", OW);
        E(Tanim, "/arac-gruplari", "Araç Grupları", OW);
        E(Tanim, "/arac-tipleri", "Araç Tipleri", OW);
        E(Tanim, "/lokasyonlar", "Lokasyonlar", OW);
        E(Tanim, "/yakit-turleri", "Yakıt Türleri", OW);
        E(Tanim, "/vites-turleri", "Vites Türleri", OW);
        E(Tanim, "/renkler", "Renkler", OW);
        E(Tanim, "/segmentler", "Segmentler", OW);
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
        E(Finans, "/kasa", "Kasa / Banka", FW);
        E(Finans, "/finans/nakit-islem", "Nakit İşlem", FW);
        E(Finans, "/finans/bakiye-duzeltme", "Bakiye Düzeltme", FW);
        E(Finans, "/kurlar", "Döviz Kurları", FW);
        E(Finans, "/faturalar", "Faturalar", FW);
        E(Finans, "/faturalar/detay-listesi", "Fatura Detay Listesi", FW);
        E(Finans, "/gelen-efatura", "Gelen e-Fatura", FW);
        E(Finans, "/giderler", "Giderler", FW);
        E(Finans, "/satislar", "Satışlar", FW);
        E(Finans, "/cari-virman", "Cari Virman", FW);
        E(Finans, "/depozito", "Depozito", FW);
        E(Finans, "/toplu-tahsilat", "Toplu Tahsilat", FW);
        E(Finans, "/tek-cari-toplu", "Tek Cari Toplu Kapatma", FW);
        E(Finans, "/otomatik-tahsilat", "Otomatik Tahsilat", FW);
        E(Finans, "/toplu-gider", "Toplu Gider", FW);
        E(Finans, "/cezalar", "Cezalar", FW);
        E(Finans, "/donem-kapanis", "Dönem Kapanışı", FW);

        // ---- Raporlar (MainLayout: Roles="Admin,Yonetici,Muhasebe" → ViewReports)
        const string Rapor = "Raporlar";
        E(Rapor, "/raporlar/finans-analiz", "Finans Analiz", VR);
        E(Rapor, "/raporlar/gelir-gider", "Gelir-Gider", VR);
        E(Rapor, "/raporlar/karlilik", "Kârlılık", VR);
        E(Rapor, "/raporlar/filo-analiz", "Filo Analiz", VR);
        E(Rapor, "/raporlar/gunluk", "Günlük", VR);
        E(Rapor, "/raporlar/cari-bakiye", "Cari Bakiye", VR);
        E(Rapor, "/raporlar/extre-ozeti", "Extre Özeti", VR);
        E(Rapor, "/raporlar/filo", "Filo", VR);
        E(Rapor, "/raporlar/doluluk", "Doluluk", VR);
        E(Rapor, "/raporlar/tahsilat-fatura", "Tahsilat-Fatura", VR);
        E(Rapor, "/raporlar/kdv-listesi", "KDV Listesi", VR);
        E(Rapor, "/raporlar/ek-hizmet", "Ek Hizmet", VR);
        E(Rapor, "/raporlar/periyodik-servis", "Periyodik Servis", VR);
        E(Rapor, "/raporlar/km-detay", "KM Detay", VR);
        E(Rapor, "/raporlar/rezervasyon-kaynak", "Rezervasyon Kaynak", VR);
        E(Rapor, "/raporlar/karsilastirmali-analiz", "Karşılaştırmalı Analiz", VR);
        E(Rapor, "/raporlar/fatura-donem", "Fatura Dönem", VR);
        E(Rapor, "/raporlar/arac-durum-takip", "Araç Durum Takip", VR);
        E(Rapor, "/raporlar/arac-gunluk-durum", "Araç Günlük Durum", VR);
        E(Rapor, "/raporlar/personel-calisma", "Personel Çalışma (Vardiya)", VR);
        E(Rapor, "/raporlar/sigorta-muayene", "Sigorta / Muayene Envanteri", VR);
        E(Rapor, "/raporlar/kasa-banka", "Kasa-Banka Defteri", VR);
        E(Rapor, "/raporlar/virman-gecmisi", "Virman Geçmişi", VR);
        E(Rapor, "/raporlar/servis-ozet", "Servis Özet", VR);
        E(Rapor, "/raporlar/otomatik-servisler", "Otomatik Servisler", VR);

        // ---- Tüm roller (grupsuz)
        E(Kok, "/vade", "Vade Panosu", null);
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
}
