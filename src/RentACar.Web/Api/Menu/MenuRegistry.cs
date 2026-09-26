using RentACar.Application.Authorization;
using RentACar.Web.Identity;

namespace RentACar.Web.Api.Menu;

/// <summary>
/// Menü öğesi (F1.6, roadmap "İmzalar").
/// <list type="bullet">
/// <item><c>Rota</c>: bağlantı (Blazor ya da SPA rotası).</item>
/// <item><c>Grup</c>: menü grubu (<see cref="MenuRegistry.Root"/> = grupsuz tek başına öğe;
/// <see cref="MenuRegistry.Shortcuts"/> = hızlı bağlantılar).</item>
/// <item><c>Sira</c>: menünün TAMAMINDA görüntülenme sırası (gruplar ilk öğelerinin sırasıyla dizilir).</item>
/// <item><c>Sahip</c>: sayfayı kim çiziyor — <see cref="MenuRegistry.Blazor"/> ya da faz kesişinden sonra <see cref="MenuRegistry.Spa"/>
/// (F4.6: Panel, Kiralar, Yeni Kira; F5.4: Rezervasyon grubu, Teklifler, Filo Kiralama, iki kısa yol; F6.4: Araçlar
/// grubu + Tanımlar'daki Araç Tipleri ve Segmentler; F7.3: Cariler &amp; CRM grubunun F7 sayfaları; F10.3: Raporlar
/// grubu; F9.3: Servis &amp; Sigorta ve Fiyat &amp; Tarife grupları + Vade Panosu; F8.3: Finans grubu; F11.3: Tanımlar,
/// Web Sitesi ve Sistem grupları, Blog, Gelen Talepler ve kalan grupsuz öğeler). <c>spa</c> öğesinin <c>Rota</c>'sı SPA adresidir (<c>/app/…</c>); Blazor menüsü pilot
/// OLMAYAN firmada bunun Blazor karşılığını (<see cref="RentACar.Web.Spa.Cutover.BlazorEquivalent"/>) açar.</item>
/// <item><c>Izin</c>: öğeyi görmek için gereken etkin izin (null = oturum açmış herkes). Sayfanın kendi
/// yetkisinden türetilir (<c>MenuKaydiTests</c> kilitler).</item>
/// <item><c>Modul</c>: satın alınabilir modül bayrağı (ör. <see cref="ModulMetadata.Website"/>); kapalıysa öğe gizli.</item>
/// <item><c>RozetKodu</c>: sayaç rozeti (<see cref="MenuRegistry.BadgeUnreadNotification"/>, <see cref="MenuRegistry.BadgeNewRequest"/>).</item>
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
public static class MenuRegistry
{
    public const string Blazor = "blazor";
    /// <summary>F4.6: sayfa yeni arayüzde (Angular). Rota <c>/app/…</c>.</summary>
    public const string Spa = "spa";
    public const string Root = "";
    public const string Shortcuts = "Kısa Yollar";
    public const string BadgeUnreadNotification = "okunmamis-bildirim";
    public const string BadgeNewRequest = "yeni-talep";

    public static IReadOnlyList<MenuOgesi> Items { get; } = Create();

    private static List<MenuOgesi> Create()
    {
        const Permission OW = Permission.OperationsWrite;
        const Permission FW = Permission.FinanceWrite;
        const Permission VR = Permission.ViewReports;
        const Permission MU = Permission.ManageUsers;
        const string WS = ModulMetadata.Website;

        var l = new List<MenuOgesi>();
        void E(string group, string route, string label, Permission? permission, string? module = null, string? badge = null, bool quick = false)
            => l.Add(new MenuOgesi(route, label, group, (l.Count + 1) * 10,
                SpaHostingPath(route) ? Spa : Blazor, permission, module, badge, quick));

        // ---- Kısa yollar (MainLayout: Roles="Admin,Yonetici,Operator")
        E(Shortcuts, "/app/rezervasyonlar", "Yeni Rezervasyon", OW, quick: true); // F5.4: spa
        E(Shortcuts, "/app/kiralar/yeni", "Yeni Kira", OW, quick: true); // F4.6: spa
        E(Shortcuts, "/app/musaitlik", "Müsaitlik-Rez Açma", OW, quick: true); // F5.4: spa

        E(Root, "/app/panel", "Panel", null); // F4.6: spa

        // ---- Operasyon grupları (MainLayout: Roles="Admin,Yonetici,Operator" → OperationsWrite)
        const string Vehicle = "Araçlar";
        E(Vehicle, "/app/araclar", "Araç Listesi", OW); // F6.4: grubun tamamı spa
        E(Vehicle, "/app/araclar/detayli", "Detaylı Araç Listesi", VR);
        E(Vehicle, "/app/arac-durum", "Araç Durum", OW);
        E(Vehicle, "/app/arac-siparis", "Araç Sipariş", OW);
        E(Vehicle, "/app/filo-plan", "Filo Plan Hedefi", OW);
        E(Vehicle, "/app/arac-kredi", "Araç Kredisi", OW);
        E(Vehicle, "/app/musteri-taksit", "Müşteri Taksitleri", FW);
        E(Vehicle, "/app/baf", "BAF (Tahsis)", OW);
        E(Vehicle, "/app/hasar", "Hasar", OW);
        E(Vehicle, "/app/arac-tipleri", "Araç Tipleri", OW);
        E(Vehicle, "/app/arac-sahipleri", "Araç Sahipleri", OW);
        E(Vehicle, "/app/segmentler", "Segmentler", OW);

        const string Rental = "Kira";
        E(Rental, "/app/kiralar", "Kiralar", OW); // F4.6: spa
        E(Rental, "/app/teklifler", "Teklifler", OW); // F5.4: spa
        E(Rental, "/app/filo-kiralama", "Filo Kiralama", OW); // F5.4: spa

        const string Reservation = "Rezervasyon";
        E(Reservation, "/app/rezervasyonlar", "Rezervasyonlar", OW); // F5.4: grubun tamamı spa
        E(Reservation, "/app/takvim", "Takvim", OW);
        E(Reservation, "/app/musaitlik", "Müsaitlik", OW);
        E(Reservation, "/app/rez-sartlari", "Rez Şartları", OW);

        const string Account = "Cariler & CRM";
        E(Account, "/app/cariler", "Cariler", OW); // F7.3: F7 sayfaları spa; F11.3: Blog ve Gelen Talepler de spa
        E(Account, "/app/crm", "CRM Analiz", VR);
        E(Account, "/app/sikayetler", "Şikayetler", OW);
        E(Account, "/app/assistans", "Assistans Talepleri", OW);
        E(Account, "/app/hukuk", "Hukuk", OW);
        E(Account, "/app/anketler", "Anketler", OW);
        E(Account, "/app/blog-yonetim", "Blog", OW);
        E(Account, "/app/gelen-talepler", "Gelen Talepler", OW, badge: BadgeNewRequest);

        const string Web = "Web Sitesi"; // PR-12: yalnız modülü satın almış firmada. F11.3: grubun tamamı spa.
        E(Web, "/app/web-sitesi", "İlanlar", OW, WS);
        E(Web, "/app/site-icerik", "Site İçeriği", OW, WS);
        E(Web, "/app/gelen-talepler", "Gelen Talepler", OW, WS, BadgeNewRequest);
        E(Web, "/app/blog-yonetim", "Blog", OW, WS);

        const string Service = "Servis & Sigorta";
        E(Service, "/app/servisler", "Servis", OW); // F9.3: Servis & Sigorta ve Fiyat & Tarife gruplarının tamamı spa
        E(Service, "/app/servis-tanimlari", "Servis Tanımları", OW);
        E(Service, "/app/regulasyon", "Sigorta / MTV / Muayene", OW);

        const string Price = "Fiyat & Tarife";
        E(Price, "/app/tarifeler", "Tarifeler", OW);
        E(Price, "/app/tarife-matris", "Tarife Matrisi", OW);
        E(Price, "/app/tarife-gruplari", "Tarife Grupları", OW);
        E(Price, "/app/tarife-aktar", "Tarife İçe Aktar", MU); // MainLayout: Roles="Admin"
        E(Price, "/app/sigorta-urunleri", "Sigorta Ürünleri", OW);
        E(Price, "/app/kira-kurallari", "Kiralama Kuralları", OW);
        E(Price, "/app/broker-yasaklari", "Broker Yasakları", OW);
        E(Price, "/app/fiyat-hesapla", "Fiyat Hesapla", OW);
        E(Price, "/app/maliyet-hesapla", "Maliyet Hesapla", FW);
        E(Price, "/app/maliyet-teklifleri", "Maliyet Teklifleri", FW);
        E(Price, "/app/ek-hizmetler", "Ek Hizmetler", OW);

        const string Definition = "Tanımlar"; // F11.3: grubun tamamı spa (Araç Tipleri ve Segmentler F6.4'ten beri)
        E(Definition, "/app/markalar", "Markalar", OW);
        E(Definition, "/app/arac-gruplari", "Araç Grupları", OW);
        E(Definition, "/app/arac-tipleri", "Araç Tipleri", OW); // F6.4: spa (F6 sayfası)
        E(Definition, "/app/lokasyonlar", "Lokasyonlar", OW);
        E(Definition, "/app/yakit-turleri", "Yakıt Türleri", OW);
        E(Definition, "/app/vites-turleri", "Vites Türleri", OW);
        E(Definition, "/app/renkler", "Renkler", OW);
        E(Definition, "/app/segmentler", "Segmentler", OW); // F6.4: spa (F6 sayfası)
        E(Definition, "/app/musteri-gruplari", "Müşteri Grupları", OW);
        E(Definition, "/app/sigorta-sirketleri", "Sigorta Şirketleri", OW);
        E(Definition, "/app/bankalar", "Bankalar", OW);
        E(Definition, "/app/departmanlar", "Departmanlar", OW);
        E(Definition, "/app/odeme-tipleri", "Ödeme Tipleri", OW);
        E(Definition, "/app/ulkeler", "Ülkeler", OW);
        E(Definition, "/app/aksesuarlar", "Aksesuarlar", OW);
        E(Definition, "/app/iptal-sebepleri", "İptal Sebepleri", OW);
        E(Definition, "/app/rezervasyon-kaynaklari", "Rez. Kaynakları", OW);
        E(Definition, "/app/ceza-turleri", "Ceza Türleri", OW);
        E(Definition, "/app/kdv-oranlari", "KDV Oranları", OW);
        E(Definition, "/app/gider-turleri", "Gider Türleri", OW);
        E(Definition, "/app/hesaplar", "Hesaplar", OW);
        E(Definition, "/app/hesap-kodlari", "Hesap Kodları", OW);
        E(Definition, "/app/ozel-kodlar", "Özel Kodlar", OW);
        E(Definition, "/app/dovizler", "Dövizler", OW);
        E(Definition, "/app/drop-tanimlari", "Drop Matris", OW);
        E(Definition, "/app/doluluk-kurallari", "Doluluk Fiyat Kuralları", OW);

        // ---- Finans (MainLayout: Roles="Admin,Yonetici,Muhasebe" → FinanceWrite)
        const string Finance = "Finans";
        E(Finance, "/app/kasa", "Kasa / Banka", FW); // F8.3: grubun tamamı spa
        E(Finance, "/app/finans/nakit-islem", "Nakit İşlem", FW);
        E(Finance, "/app/finans/bakiye-duzeltme", "Bakiye Düzeltme", FW);
        E(Finance, "/app/kurlar", "Döviz Kurları", FW);
        E(Finance, "/app/faturalar", "Faturalar", FW);
        E(Finance, "/app/faturalar/detay-listesi", "Fatura Detay Listesi", FW);
        E(Finance, "/app/gelen-efatura", "Gelen e-Fatura", FW);
        E(Finance, "/app/giderler", "Giderler", FW);
        E(Finance, "/app/satislar", "Satışlar", FW);
        E(Finance, "/app/cari-virman", "Cari Virman", FW);
        E(Finance, "/app/depozito", "Depozito", FW);
        E(Finance, "/app/toplu-tahsilat", "Toplu Tahsilat", FW);
        E(Finance, "/app/tek-cari-toplu", "Tek Cari Toplu Kapatma", FW);
        E(Finance, "/app/otomatik-tahsilat", "Otomatik Tahsilat", FW);
        E(Finance, "/app/toplu-gider", "Toplu Gider", FW);
        E(Finance, "/app/cezalar", "Cezalar", FW);
        E(Finance, "/app/donem-kapanis", "Dönem Kapanışı", FW);

        // ---- Raporlar (MainLayout: Roles="Admin,Yonetici,Muhasebe" → ViewReports). F10.3: grubun tamamı spa.
        const string Report = "Raporlar";
        E(Report, "/app/raporlar/finans-analiz", "Finans Analiz", VR);
        E(Report, "/app/raporlar/gelir-gider", "Gelir-Gider", VR);
        E(Report, "/app/raporlar/karlilik", "Kârlılık", VR);
        E(Report, "/app/raporlar/filo-analiz", "Filo Analiz", VR);
        E(Report, "/app/raporlar/gunluk", "Günlük", VR);
        E(Report, "/app/raporlar/cari-bakiye", "Cari Bakiye", VR);
        E(Report, "/app/raporlar/extre-ozeti", "Extre Özeti", VR);
        E(Report, "/app/raporlar/filo", "Filo", VR);
        E(Report, "/app/raporlar/doluluk", "Doluluk", VR);
        E(Report, "/app/raporlar/tahsilat-fatura", "Tahsilat-Fatura", VR);
        E(Report, "/app/raporlar/kdv-listesi", "KDV Listesi", VR);
        E(Report, "/app/raporlar/ek-hizmet", "Ek Hizmet", VR);
        E(Report, "/app/raporlar/periyodik-servis", "Periyodik Servis", VR);
        E(Report, "/app/raporlar/km-detay", "KM Detay", VR);
        E(Report, "/app/raporlar/rezervasyon-kaynak", "Rezervasyon Kaynak", VR);
        E(Report, "/app/raporlar/karsilastirmali-analiz", "Karşılaştırmalı Analiz", VR);
        E(Report, "/app/raporlar/fatura-donem", "Fatura Dönem", VR);
        E(Report, "/app/raporlar/arac-durum-takip", "Araç Durum Takip", VR);
        E(Report, "/app/raporlar/arac-gunluk-durum", "Araç Günlük Durum", VR);
        E(Report, "/app/raporlar/personel-calisma", "Personel Çalışma (Vardiya)", VR);
        E(Report, "/app/raporlar/sigorta-muayene", "Sigorta / Muayene Envanteri", VR);
        E(Report, "/app/raporlar/kasa-banka", "Kasa-Banka Defteri", VR);
        E(Report, "/app/raporlar/virman-gecmisi", "Virman Geçmişi", VR);
        E(Report, "/app/raporlar/servis-ozet", "Servis Özet", VR);
        E(Report, "/app/raporlar/otomatik-servisler", "Otomatik Servisler", VR);

        // ---- Tüm roller (grupsuz). F9.3: Vade Panosu spa; F11.3: diğerleri spa.
        E(Root, "/app/vade", "Vade Panosu", null);
        E(Root, "/app/bildirimler", "Bildirimler", null, badge: BadgeUnreadNotification);
        E(Root, "/app/takvim-abonelik", "Takvim Aboneliği", null);
        E(Root, "/app/firma-belgeleri", "Firma Belgeleri", null);
        E(Root, "/app/dokumanlar", "Dokümanlar", null);

        // ---- Sistem (MainLayout: Roles="Admin" → ManageUsers). F11.3: grubun tamamı spa.
        const string System = "Sistem";
        E(System, "/app/subeler", "Şubeler", MU);
        E(System, "/app/kullanicilar", "Kullanıcılar", MU);
        E(System, "/app/personel", "Personel", MU);
        E(System, "/app/ayarlar", "Ayarlar", MU);
        E(System, "/app/mesaj-sablonlari", "Mesaj Şablonları", MU);
        E(System, "/app/belge-sablonlari", "Belge Şablonları", MU);
        E(System, "/app/yetki", "Ekran Yetkileri", MU);
        E(System, "/app/denetim", "Denetim", MU);
        E(System, "/app/ice-aktar", "Veri İçe Aktar", MU);
        return l;
    }

    /// <summary>Rota yeni arayüzün mü (<c>/app</c> ya da altı)? Sahip bundan türer — elle yazılmaz, kayamaz.</summary>
    private static bool SpaHostingPath(string route) => RentACar.Web.Spa.Cutover.IsSpaPath(route);
}
