using Microsoft.AspNetCore.WebUtilities;
using RentACar.Web.Identity;

namespace RentACar.Web.Spa;

/// <summary>
/// F4.6 ilk kesiş (+ F5.4 rezervasyon, F6.4 araç, F7.3 cari/CRM, F10.3 rapor, F9.3 servis/sigorta/fiyat, F8.3 finans, F11.3 tanım/sistem/web kesişi) — Blazor ↔ yeni arayüz (<c>/app</c>) geçiş kararları. SAF fonksiyonlar (Program.cs ve
/// middleware test edilemez; karar burada, birim testiyle kilitli). Uygulayan: <see cref="CutoverMiddleware"/>.
///
/// <para><b>Yönlendirme haritası</b> (<see cref="Map"/>): kesişi yapılmış fazların (F4, F5, F6, F7, F10, F9, F8, F11) envanterinde
/// silinecek Blazor <c>@page</c> ŞABLONLARINDAN türetilmiş AÇIK liste — önek eşleşmesi YOK. Bu yüzden aynı öneki paylaşan GET uçları
/// (<c>/kiralar/{id}/pdf</c>, <c>/kiralar/hesapla</c>, <c>/kiralar/donus-hesapla</c>, <c>/kiralar/musait-arac</c>,
/// <c>/kiralar/ornek-sozlesme/pdf</c>, export, makbuz…) YÖNLENMEZ: bir şablonla segment segment birebir
/// eşleşmeyen yol haritada yoktur. Yalnız GET/HEAD, yalnız PİLOT kiracının oturumu (middleware karar verir).
/// Sorgu dizesi AYNEN taşınır (<c>?varac=…&amp;vfrom=…</c>); fragment tarayıcıda kalır (Location'a eklenmez —
/// tarayıcı fragment'sız Location'da özgün fragment'ı korur). 302 (geçici; F13'te kalıcı olur).</para>
///
/// <para><b>Tek giriş</b>: <c>GET /login</c> artık form çizmez. Oturumsuz → <c>/app/giris</c> (dönüş adresi
/// <see cref="PermissionRedirect.SafeReturn"/>'ten geçerek taşınır); oturumlu → <see cref="AfterLogin"/>.
/// SPA girişi Blazor dönüş adresini kendisi çözmez, <c>/login?ReturnUrl=</c>'e geri verir: açık yönlendirme
/// çiti TEK yerde (sunucu) kalır. <b>Döngü yok</b>: <c>/app</c> anonim (cookie challenge almaz), <c>/login</c>
/// oturumsuzken yalnız <c>/app/giris</c>'e, oturumluyken hiçbir zaman <c>/login</c>'e ya da <c>/app/giris</c>'e
/// yönlendirmez (GuvenliDonus <c>/login</c>'i reddeder; <c>/app/giris</c> burada Panel'e çevrilir).</para>
/// </summary>
public static class Cutover
{
    /// <summary>Blazor giriş sayfası (cookie şemasının <c>LoginPath</c>'i).</summary>
    public const string BlazorLogin = "/login";

    /// <summary>Yeni arayüz giriş sayfası (anonim).</summary>
    public const string SpaLogin = SpaHosting.Prefix + "/giris";

    /// <summary>Pilot kiracının varsayılan inişi.</summary>
    public const string SpaPanel = SpaHosting.Prefix + "/panel";

    /// <summary>SPA'nın dönüş adresi parametresi (Angular <c>queryParamMap</c> büyük/küçük harf duyarlı).</summary>
    public const string SpaReturnParameter = "returnUrl";

    /// <summary>SPA giriş sayfasının bilgi mesajı parametresi (<c>kiraci_kapali</c>, <c>cikis</c>).</summary>
    public const string SpaReasonParameter = "neden";

    /// <summary>Kaynak (<c>@page</c> şablonu) → SPA şablonu. Tek parametre biçimi <c>{id:guid}</c> → <c>{id}</c>.</summary>
    public sealed record Eslem(string Kaynak, string Hedef);

    /// <summary>
    /// Kesişi yapılmış fazların envanteri — her faz kendi bloğu; <c>IlkKesisTests</c> listeyi fazların envanter
    /// tablolarıyla (<c>docs/roadmap/F*.md</c>) ve sayfaların gerçek <c>@page</c> satırlarıyla karşılaştırır.
    /// <list type="bullet">
    /// <item><b>F4</b> (F4.6): <c>Home.razor</c> <c>/</c>, <c>RentalList.razor</c> <c>/kiralar</c>, <c>KiraForm.razor</c>
    /// <c>/kiralar/yeni</c> + <c>/kiralar/{Id:guid}</c>, <c>RentalPrint.razor</c> <c>/kiralar/{Id:guid}/yazdir</c>.
    /// <c>Login.razor</c> (<c>/login</c>) haritada DEĞİL — pilot olsun olmasın herkes için
    /// <see cref="LoginRedirect"/>/<see cref="AfterLogin"/>.</item>
    /// <item><b>F5</b> (F5.4): rezervasyonlar, teklifler, takvim, müsaitlik, rez şartları, filo kiralama — her biri
    /// TEK <c>@page</c> (liste; Blazor'da kayıt formu listenin içindeydi). SPA'nın <c>/yeni</c> ve <c>/:id</c>
    /// rotalarının Blazor karşılığı yok, haritaya girmez. <c>/takvim-abonelik</c>, <c>/rezervasyon-kaynaklari</c>
    /// ayrı sayfalardır (segment eşitliği; önek eşleşmesi yok).</item>
    /// <item><b>F6</b> (F6.4): 14 araç sayfası. SPA adı farklı olan dört şablon: <c>/vehicles</c> →
    /// <c>/app/araclar</c>, <c>/vehicles/detayli</c> → <c>/app/araclar/detayli</c>, <c>/vehicles/{id}</c> (kart) →
    /// <c>/app/araclar/{id}</c>, <c>/araclar/{id}</c> (detay) → <c>/app/araclar/{id}/detay</c>; diğer on sayfa aynı adla
    /// <c>/app</c> altına. Fotoğraf GET'leri (<c>/vehicles/{id}/photos/{p}[/thumb]</c>), export ve Blazor POST'ları
    /// segment sayısı ya da yöntem farkıyla dışarıda; <c>/arac-gruplari</c> F11'in sayfasıdır.</item>
    /// <item><b>F7</b> (F7.3): 8 cari/CRM sayfası, hepsi aynı adla <c>/app</c> altına (<c>/cariler</c>,
    /// <c>/cariler/{id}</c> kart, <c>/cariler/{id}/detay</c>, <c>/anketler</c>, <c>/sikayetler</c>, <c>/assistans</c>,
    /// <c>/hukuk</c>, <c>/crm</c>). <c>/cariler/{id}/ekstre</c> F8'in sayfasıdır (F8 bloğunda); SPA'nın
    /// <c>/cariler/yeni</c> rotasının Blazor karşılığı yok (Guid kısıtı <c>yeni</c>'ye uymaz).</item>
    /// <item><b>F10</b> (F10.3): 26 rapor sayfası, hepsi AYNI adla <c>/app</c> altına (tek ortak rapor ekranı; araç
    /// karnesi <c>/raporlar/arac-karne/{id}</c>). Rapor export'ları (<c>/raporlar/export/{rapor}</c>) segment farkıyla
    /// dışarıda; personel çalışma Blazor POST'ları (<c>/raporlar/personel-calisma/create|update|delete</c>) yöntem
    /// farkıyla dışarıda.</item>
    /// <item><b>F9</b> (F9.3): 15 servis / sigorta / vade / fiyat-tarife sayfası, hepsi AYNI adla <c>/app</c> altına
    /// (tek <c>@page</c>'li liste sayfaları). Blazor <c>/regulasyon</c> tek sayfasının MTV ve muayene bölümleri SPA'da
    /// ayrı rotadır (<c>/app/regulasyon/mtv</c>, <c>/app/regulasyon/muayene</c>); Blazor karşılıkları yok, haritaya
    /// girmez — Blazor <c>POST /regulasyon/mtv|muayene|sigorta|zeyil</c> yöntem farkıyla zaten dışarıda. SPA'nın kayıt
    /// rotaları (<c>/servisler/{id}</c>, <c>/maliyet-teklifleri/{id}</c>, <c>/regulasyon/sigortalar/{id}</c> …) Blazor'da
    /// yoktu. Vade export'u (<c>/listeler/export/vade</c>) ve Blazor POST'ları segment/yöntem farkıyla dışarıda.</item>
    /// <item><b>F8</b> (F8.3): 18 finans sayfası, hepsi AYNI adla <c>/app</c> altına (kasa, nakit işlem, bakiye düzeltme,
    /// cari virman, depozito, toplu işlemler, otomatik tahsilat, dönem kapanışı, kurlar, faturalar + detay listesi +
    /// yazdır, cezalar, giderler, gelen e-fatura, satışlar) ve cari ekstresi <c>/cariler/{id}/ekstre</c> (SPA'da
    /// FinanceWrite ∨ ViewReports kapılı). PDF/makbuz (<c>/faturalar/{id}/pdf</c>, <c>/kasa/makbuz/{id}/pdf</c>),
    /// export ve Blazor POST'ları (<c>/finans/…</c>, <c>/kurlar/…</c>, <c>/cezalar/…</c> …) segment ya da yöntem
    /// farkıyla dışarıda.</item>
    /// <item><b>F11</b> (F11.3): 47 tanım/sistem/web sitesi sayfası, hepsi AYNI adla <c>/app</c> altına (üç kimlikli
    /// şablon: ilan fiyatı, ilan özellikleri, blog önizlemesi). Dosya/görsel GET'leri (logo, ilan fotoğrafı, blog kapağı,
    /// doküman indirme), takvim beslemesi ve Blazor POST'ları segment sayısı ya da yöntem farkıyla dışarıda.
    /// <c>/tarife-aktar</c> F9'un sayfasıdır (F9 bloğunda).</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<Eslem> Map { get; } =
    [
        // F4
        new("/", SpaPanel),
        new("/kiralar", SpaHosting.Prefix + "/kiralar"),
        new("/kiralar/yeni", SpaHosting.Prefix + "/kiralar/yeni"),
        new("/kiralar/{id:guid}", SpaHosting.Prefix + "/kiralar/{id}"),
        new("/kiralar/{id:guid}/yazdir", SpaHosting.Prefix + "/kiralar/{id}/yazdir"),
        // F5
        new("/rezervasyonlar", SpaHosting.Prefix + "/rezervasyonlar"),
        new("/teklifler", SpaHosting.Prefix + "/teklifler"),
        new("/takvim", SpaHosting.Prefix + "/takvim"),
        new("/musaitlik", SpaHosting.Prefix + "/musaitlik"),
        new("/rez-sartlari", SpaHosting.Prefix + "/rez-sartlari"),
        new("/filo-kiralama", SpaHosting.Prefix + "/filo-kiralama"),
        // F6
        new("/vehicles", SpaHosting.Prefix + "/araclar"),
        new("/vehicles/detayli", SpaHosting.Prefix + "/araclar/detayli"),
        new("/vehicles/{id:guid}", SpaHosting.Prefix + "/araclar/{id}"),
        new("/araclar/{id:guid}", SpaHosting.Prefix + "/araclar/{id}/detay"),
        new("/arac-durum", SpaHosting.Prefix + "/arac-durum"),
        new("/arac-sahipleri", SpaHosting.Prefix + "/arac-sahipleri"),
        new("/segmentler", SpaHosting.Prefix + "/segmentler"),
        new("/arac-tipleri", SpaHosting.Prefix + "/arac-tipleri"),
        new("/arac-kredi", SpaHosting.Prefix + "/arac-kredi"),
        new("/musteri-taksit", SpaHosting.Prefix + "/musteri-taksit"),
        new("/arac-siparis", SpaHosting.Prefix + "/arac-siparis"),
        new("/baf", SpaHosting.Prefix + "/baf"),
        new("/hasar", SpaHosting.Prefix + "/hasar"),
        new("/filo-plan", SpaHosting.Prefix + "/filo-plan"),
        // F7
        new("/cariler", SpaHosting.Prefix + "/cariler"),
        new("/cariler/{id:guid}", SpaHosting.Prefix + "/cariler/{id}"),
        new("/cariler/{id:guid}/detay", SpaHosting.Prefix + "/cariler/{id}/detay"),
        new("/anketler", SpaHosting.Prefix + "/anketler"),
        new("/sikayetler", SpaHosting.Prefix + "/sikayetler"),
        new("/assistans", SpaHosting.Prefix + "/assistans"),
        new("/hukuk", SpaHosting.Prefix + "/hukuk"),
        new("/crm", SpaHosting.Prefix + "/crm"),
        // F10
        new("/raporlar/arac-durum-takip", SpaHosting.Prefix + "/raporlar/arac-durum-takip"),
        new("/raporlar/arac-gunluk-durum", SpaHosting.Prefix + "/raporlar/arac-gunluk-durum"),
        new("/raporlar/arac-karne/{id:guid}", SpaHosting.Prefix + "/raporlar/arac-karne/{id}"),
        new("/raporlar/cari-bakiye", SpaHosting.Prefix + "/raporlar/cari-bakiye"),
        new("/raporlar/doluluk", SpaHosting.Prefix + "/raporlar/doluluk"),
        new("/raporlar/ek-hizmet", SpaHosting.Prefix + "/raporlar/ek-hizmet"),
        new("/raporlar/extre-ozeti", SpaHosting.Prefix + "/raporlar/extre-ozeti"),
        new("/raporlar/fatura-donem", SpaHosting.Prefix + "/raporlar/fatura-donem"),
        new("/raporlar/filo-analiz", SpaHosting.Prefix + "/raporlar/filo-analiz"),
        new("/raporlar/filo", SpaHosting.Prefix + "/raporlar/filo"),
        new("/raporlar/finans-analiz", SpaHosting.Prefix + "/raporlar/finans-analiz"),
        new("/raporlar/gelir-gider", SpaHosting.Prefix + "/raporlar/gelir-gider"),
        new("/raporlar/gunluk", SpaHosting.Prefix + "/raporlar/gunluk"),
        new("/raporlar/karlilik", SpaHosting.Prefix + "/raporlar/karlilik"),
        new("/raporlar/karsilastirmali-analiz", SpaHosting.Prefix + "/raporlar/karsilastirmali-analiz"),
        new("/raporlar/kasa-banka", SpaHosting.Prefix + "/raporlar/kasa-banka"),
        new("/raporlar/kdv-listesi", SpaHosting.Prefix + "/raporlar/kdv-listesi"),
        new("/raporlar/km-detay", SpaHosting.Prefix + "/raporlar/km-detay"),
        new("/raporlar/otomatik-servisler", SpaHosting.Prefix + "/raporlar/otomatik-servisler"),
        new("/raporlar/periyodik-servis", SpaHosting.Prefix + "/raporlar/periyodik-servis"),
        new("/raporlar/personel-calisma", SpaHosting.Prefix + "/raporlar/personel-calisma"),
        new("/raporlar/rezervasyon-kaynak", SpaHosting.Prefix + "/raporlar/rezervasyon-kaynak"),
        new("/raporlar/servis-ozet", SpaHosting.Prefix + "/raporlar/servis-ozet"),
        new("/raporlar/sigorta-muayene", SpaHosting.Prefix + "/raporlar/sigorta-muayene"),
        new("/raporlar/tahsilat-fatura", SpaHosting.Prefix + "/raporlar/tahsilat-fatura"),
        new("/raporlar/virman-gecmisi", SpaHosting.Prefix + "/raporlar/virman-gecmisi"),
        // F9
        new("/servisler", SpaHosting.Prefix + "/servisler"),
        new("/regulasyon", SpaHosting.Prefix + "/regulasyon"),
        new("/vade", SpaHosting.Prefix + "/vade"),
        new("/servis-tanimlari", SpaHosting.Prefix + "/servis-tanimlari"),
        new("/tarifeler", SpaHosting.Prefix + "/tarifeler"),
        new("/tarife-matris", SpaHosting.Prefix + "/tarife-matris"),
        new("/tarife-gruplari", SpaHosting.Prefix + "/tarife-gruplari"),
        new("/tarife-aktar", SpaHosting.Prefix + "/tarife-aktar"),
        new("/sigorta-urunleri", SpaHosting.Prefix + "/sigorta-urunleri"),
        new("/kira-kurallari", SpaHosting.Prefix + "/kira-kurallari"),
        new("/broker-yasaklari", SpaHosting.Prefix + "/broker-yasaklari"),
        new("/fiyat-hesapla", SpaHosting.Prefix + "/fiyat-hesapla"),
        new("/maliyet-hesapla", SpaHosting.Prefix + "/maliyet-hesapla"),
        new("/maliyet-teklifleri", SpaHosting.Prefix + "/maliyet-teklifleri"),
        new("/ek-hizmetler", SpaHosting.Prefix + "/ek-hizmetler"),
        // F8
        new("/kasa", SpaHosting.Prefix + "/kasa"),
        new("/finans/nakit-islem", SpaHosting.Prefix + "/finans/nakit-islem"),
        new("/finans/bakiye-duzeltme", SpaHosting.Prefix + "/finans/bakiye-duzeltme"),
        new("/cari-virman", SpaHosting.Prefix + "/cari-virman"),
        new("/depozito", SpaHosting.Prefix + "/depozito"),
        new("/tek-cari-toplu", SpaHosting.Prefix + "/tek-cari-toplu"),
        new("/toplu-tahsilat", SpaHosting.Prefix + "/toplu-tahsilat"),
        new("/toplu-gider", SpaHosting.Prefix + "/toplu-gider"),
        new("/otomatik-tahsilat", SpaHosting.Prefix + "/otomatik-tahsilat"),
        new("/donem-kapanis", SpaHosting.Prefix + "/donem-kapanis"),
        new("/kurlar", SpaHosting.Prefix + "/kurlar"),
        new("/cariler/{id:guid}/ekstre", SpaHosting.Prefix + "/cariler/{id}/ekstre"),
        new("/faturalar", SpaHosting.Prefix + "/faturalar"),
        new("/faturalar/detay-listesi", SpaHosting.Prefix + "/faturalar/detay-listesi"),
        new("/faturalar/{id:guid}/yazdir", SpaHosting.Prefix + "/faturalar/{id}/yazdir"),
        new("/cezalar", SpaHosting.Prefix + "/cezalar"),
        new("/giderler", SpaHosting.Prefix + "/giderler"),
        new("/gelen-efatura", SpaHosting.Prefix + "/gelen-efatura"),
        new("/satislar", SpaHosting.Prefix + "/satislar"),
        // F11
        new("/aksesuarlar", SpaHosting.Prefix + "/aksesuarlar"),
        new("/arac-gruplari", SpaHosting.Prefix + "/arac-gruplari"),
        new("/bankalar", SpaHosting.Prefix + "/bankalar"),
        new("/belge-sablonlari", SpaHosting.Prefix + "/belge-sablonlari"),
        new("/ceza-turleri", SpaHosting.Prefix + "/ceza-turleri"),
        new("/departmanlar", SpaHosting.Prefix + "/departmanlar"),
        new("/doluluk-kurallari", SpaHosting.Prefix + "/doluluk-kurallari"),
        new("/dovizler", SpaHosting.Prefix + "/dovizler"),
        new("/drop-tanimlari", SpaHosting.Prefix + "/drop-tanimlari"),
        new("/gider-turleri", SpaHosting.Prefix + "/gider-turleri"),
        new("/hesap-kodlari", SpaHosting.Prefix + "/hesap-kodlari"),
        new("/hesaplar", SpaHosting.Prefix + "/hesaplar"),
        new("/iptal-sebepleri", SpaHosting.Prefix + "/iptal-sebepleri"),
        new("/kdv-oranlari", SpaHosting.Prefix + "/kdv-oranlari"),
        new("/lokasyonlar", SpaHosting.Prefix + "/lokasyonlar"),
        new("/markalar", SpaHosting.Prefix + "/markalar"),
        new("/musteri-gruplari", SpaHosting.Prefix + "/musteri-gruplari"),
        new("/odeme-tipleri", SpaHosting.Prefix + "/odeme-tipleri"),
        new("/ozel-kodlar", SpaHosting.Prefix + "/ozel-kodlar"),
        new("/personel", SpaHosting.Prefix + "/personel"),
        new("/renkler", SpaHosting.Prefix + "/renkler"),
        new("/rezervasyon-kaynaklari", SpaHosting.Prefix + "/rezervasyon-kaynaklari"),
        new("/sigorta-sirketleri", SpaHosting.Prefix + "/sigorta-sirketleri"),
        new("/subeler", SpaHosting.Prefix + "/subeler"),
        new("/ulkeler", SpaHosting.Prefix + "/ulkeler"),
        new("/vites-turleri", SpaHosting.Prefix + "/vites-turleri"),
        new("/yakit-turleri", SpaHosting.Prefix + "/yakit-turleri"),
        new("/dokumanlar", SpaHosting.Prefix + "/dokumanlar"),
        new("/firma-belgeleri", SpaHosting.Prefix + "/firma-belgeleri"),
        new("/takvim-abonelik", SpaHosting.Prefix + "/takvim-abonelik"),
        new("/ice-aktar", SpaHosting.Prefix + "/ice-aktar"),
        new("/kullanicilar", SpaHosting.Prefix + "/kullanicilar"),
        new("/yetki", SpaHosting.Prefix + "/yetki"),
        new("/ayarlar", SpaHosting.Prefix + "/ayarlar"),
        new("/mesaj-sablonlari", SpaHosting.Prefix + "/mesaj-sablonlari"),
        new("/denetim", SpaHosting.Prefix + "/denetim"),
        new("/bildirimler", SpaHosting.Prefix + "/bildirimler"),
        new("/ara", SpaHosting.Prefix + "/ara"),
        new("/profil/sifre-degistir", SpaHosting.Prefix + "/profil/sifre-degistir"),
        new("/web-sitesi", SpaHosting.Prefix + "/web-sitesi"),
        new("/web-sitesi/arac-ekle", SpaHosting.Prefix + "/web-sitesi/arac-ekle"),
        new("/web-sitesi/ilan/{id:guid}/fiyat", SpaHosting.Prefix + "/web-sitesi/ilan/{id}/fiyat"),
        new("/web-sitesi/ilan/{id:guid}/ozellikler", SpaHosting.Prefix + "/web-sitesi/ilan/{id}/ozellikler"),
        new("/site-icerik", SpaHosting.Prefix + "/site-icerik"),
        new("/blog-yonetim", SpaHosting.Prefix + "/blog-yonetim"),
        new("/blog-yonetim/{id:guid}/onizleme", SpaHosting.Prefix + "/blog-yonetim/{id}/onizleme"),
        new("/gelen-talepler", SpaHosting.Prefix + "/gelen-talepler"),
    ];

    private const string GuidParameter = "{id:guid}";

    /// <summary>
    /// Yol haritadaki bir şablonla birebir eşleşiyorsa SPA yolu (sorgusuz), değilse <c>null</c>.
    /// Karşılaştırma segment segment, büyük/küçük harf duyarsız (ASP.NET yönlendirmesi gibi); sondaki
    /// <c>/</c> yok sayılır; <c>{id:guid}</c> yalnız Guid'e uyar ve hedefe kanonik (<c>D</c>) yazılır.
    /// </summary>
    public static string? SpaPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/') return null;
        var parts = Split(path);
        if (parts is null) return null;
        foreach (var e in Map)
        {
            var template = Split(e.Kaynak)!;
            if (template.Length != parts.Length) continue;
            Guid? id = null;
            var warn = true;
            for (var i = 0; i < template.Length && warn; i++)
            {
                if (template[i] == GuidParameter)
                {
                    if (Guid.TryParse(parts[i], out var g)) id = g;
                    else warn = false;
                }
                else warn = string.Equals(template[i], parts[i], StringComparison.OrdinalIgnoreCase);
            }
            if (!warn) continue;
            return id is { } v ? e.Hedef.Replace("{id}", v.ToString("D"), StringComparison.Ordinal) : e.Hedef;
        }
        return null;
    }

    /// <summary>
    /// İsteğin SPA hedefi (yol + AYNEN sorgu) ya da <c>null</c>. YALNIZ GET/HEAD: form gönderimleri
    /// (POST <c>/kiralar/create</c> …) ve diğer yöntemler asla yönlenmez. Pilot kararı çağıranın (middleware).
    /// </summary>
    public static string? SpaTarget(string method, PathString path, QueryString query)
    {
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) return null;
        // Sorgu HAM taşınır (tarayıcı zaten yüzde-kodlar). Yazdırılabilir ASCII dışı ham bayt yalnız elle
        // üretilmiş istekte olur; Location başlığına yazılamaz (Kestrel reddeder → 500) → yönlendirme yapılmaz,
        // Blazor sayfası açılır.
        foreach (var c in query.Value ?? "")
            if (c < '!' || c > '~') return null;
        return SpaPath(path.Value) is { } target ? target + query.Value : null;
    }

    /// <summary>
    /// SPA menü rotasının (parametresiz) Blazor karşılığı — pilot OLMAYAN kiracıda Blazor menüsü bunu açar
    /// (<c>/app/kiralar</c> → <c>/kiralar</c>). Haritada yoksa <c>null</c>.
    /// </summary>
    public static string? BlazorEquivalent(string spaRoute)
        => Map.FirstOrDefault(e => !e.Kaynak.Contains('{') && string.Equals(e.Hedef, spaRoute, StringComparison.OrdinalIgnoreCase))?.Kaynak;

    /// <summary><c>/login</c> (ya da <c>/login/</c>) mı — segment eşitliği; <c>/platform/login</c>, <c>/loginx</c> DEĞİL.</summary>
    public static bool IsBlazorEntry(PathString path)
    {
        var v = path.Value;
        if (string.IsNullOrEmpty(v)) return false;
        if (v.Length > 1 && v[^1] == '/') v = v[..^1];
        return string.Equals(v, BlazorLogin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Oturumsuz <c>GET /login</c> → <c>/app/giris</c>. <c>ReturnUrl</c> yalnız <see cref="PermissionRedirect.SafeReturn"/>'ten
    /// geçerse (Panel değilse) SPA'nın <c>returnUrl</c>'ine yazılır — yabancı/indirme/döngü adresi hiç taşınmaz.
    /// <c>?hata=kapali</c> (kapatılan firmanın oturumu düşürüldü, <c>TenantActiveMiddleware</c>) →
    /// <c>?neden=kiraci_kapali</c> (SPA mesajı). Diğer parametreler taşınmaz.
    /// </summary>
    public static string LoginRedirect(QueryString query)
    {
        var q = QueryHelpers.ParseQuery(query.Value);
        var newItem = QueryString.Empty;
        if (q.TryGetValue(PermissionRedirect.ReturnParameter, out var raw)
            && PermissionRedirect.SafeReturn(raw.ToString()) is var returnInfo && returnInfo != PermissionRedirect.Default)
            newItem = newItem.Add(SpaReturnParameter, returnInfo);
        if (q.TryGetValue("hata", out var error) && error.ToString() == "kapali")
            newItem = newItem.Add(SpaReasonParameter, "kiraci_kapali");
        return SpaLogin + newItem.ToUriComponent();
    }

    /// <summary>
    /// Oturum açmış firma kullanıcısının <c>/login</c>'den sonraki hedefi (SPA girişi de Blazor dönüş adresini
    /// buraya verir). Dönüş önce <see cref="PermissionRedirect.SafeReturn"/>'ten geçer (açık yönlendirme çiti).
    /// <list type="bullet">
    /// <item>Pilot: <c>/app/…</c> dönüş olduğu gibi (<c>/app/giris…</c> → Panel — döngü yok); haritadaki Blazor
    /// adresi SPA karşılığına (<c>/</c> → <c>/app/panel</c>, <c>/kiralar?x</c> → <c>/app/kiralar?x</c>); diğer
    /// Blazor sayfaları (henüz taşınmamış modüller) olduğu gibi.</item>
    /// <item>Pilot değil: <c>/app/…</c> dönüş → Blazor Panel (<c>/</c>; yeni arayüz bu firmada kapalı), diğerleri
    /// olduğu gibi.</item>
    /// </list>
    /// </summary>
    public static string AfterLogin(bool pilot, string? returnInfo)
    {
        var g = PermissionRedirect.SafeReturn(returnInfo);
        var end = g.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? g : g[..end];
        var remaining = end < 0 ? "" : g[end..];
        var spa = IsSpaPath(path);

        if (!pilot) return spa ? PermissionRedirect.Default : g;
        if (spa) return IsSpaEntryPath(path) ? SpaPanel : g;
        return SpaPath(path) is { } target ? target + remaining : g;
    }

    /// <summary><c>/app</c> ya da altı (segment sınırı: <c>/apps</c> DEĞİL).</summary>
    public static bool IsSpaPath(string path)
        => new PathString(path).StartsWithSegments(SpaHosting.Prefix, StringComparison.OrdinalIgnoreCase);

    private static bool IsSpaEntryPath(string path)
        => new PathString(path).StartsWithSegments(SpaLogin, StringComparison.OrdinalIgnoreCase);

    /// <summary>Yolu segmentlere ayırır (sondaki <c>/</c> yok sayılır, kök = boş dizi). Boş ara segment → <c>null</c>.</summary>
    private static string[]? Split(string path)
    {
        var v = path.Length > 1 && path[^1] == '/' ? path[..^1] : path;
        if (v == "/") return [];
        var parts = v[1..].Split('/');
        return parts.Any(p => p.Length == 0) ? null : parts;
    }
}
