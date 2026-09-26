using Microsoft.AspNetCore.WebUtilities;
using RentACar.Web.Identity;

namespace RentACar.Web.Spa;

/// <summary>
/// F4.6 ilk kesiş (+ F5.4 rezervasyon, F6.4 araç, F7.3 cari/CRM, F10.3 rapor, F9.3 servis/sigorta/fiyat, F8.3 finans, F11.3 tanım/sistem/web, F12 platform konsolu kesişi) — Blazor ↔ yeni arayüz (<c>/app</c>) geçiş kararları. SAF fonksiyonlar (Program.cs ve
/// middleware test edilemez; karar burada, birim testiyle kilitli). Uygulayan: <see cref="CutoverMiddleware"/>.
///
/// <para><b>Yönlendirme haritası</b> (<see cref="Map"/>): kesişi yapılmış fazların (F4, F5, F6, F7, F10, F9, F8, F11, F12) envanterinde
/// silinecek Blazor <c>@page</c> ŞABLONLARINDAN türetilmiş AÇIK liste — önek eşleşmesi YOK. Bu yüzden aynı öneki paylaşan GET uçları
/// (<c>/kiralar/{id}/pdf</c>, <c>/kiralar/hesapla</c>, <c>/kiralar/donus-hesapla</c>, <c>/kiralar/musait-arac</c>,
/// <c>/kiralar/ornek-sozlesme/pdf</c>, export, makbuz…) YÖNLENMEZ: bir şablonla segment segment birebir
/// eşleşmeyen yol haritada yoktur. Yalnız GET/HEAD; F13.1b'den beri HERKES için (pilot kapısı yok, oturum koşulu yok —
/// Blazor sayfaları silindi, eski adresin tek karşılığı SPA). Sorgu dizesi AYNEN taşınır (<c>?varac=…&amp;vfrom=…</c>);
/// fragment tarayıcıda kalır (Location'a eklenmez — tarayıcı fragment'sız Location'da özgün fragment'ı korur).
/// <b>301 (kalıcı)</b>: yer imleri ve bildirim bağlantıları kalıcı olarak SPA'ya taşınır.</para>
///
/// <para><b>Kabuk sayfaları</b> (<see cref="ShellTarget"/>): eski Blazor <c>/Error</c>, <c>/not-found</c>, <c>/hata</c>,
/// <c>/yetkisiz</c> yeni arayüzün Panel'ine gider; <c>?hata=&lt;kod&gt;</c> (SPA kodu sabit çeviri metnine çevirir). Hedef
/// SABİT yol ve sabit kod — kullanıcı girdisi taşınmaz (açık yönlendirme ve içerik sahteciliği yok).</para>
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

    /// <summary>Firma kullanıcısının varsayılan inişi (yeni arayüz Panel'i).</summary>
    public const string SpaPanel = SpaHosting.Prefix + "/panel";

    /// <summary>SPA'nın hata bandı parametresi (<c>core/geri-bildirim/query-messages.ts</c>). Değeri bir KOD'dur.</summary>
    public const string SpaErrorParameter = "hata";

    /// <summary>500 destek kodu parametresi (trace id; SPA yalnız hex biçimini gösterir).</summary>
    public const string SpaSupportParameter = "destek";

    /// <summary>
    /// Hata bandı kodları — SPA <c>ERROR_CODES</c> ile AYNI küme; her biri SPA'da sabit bir çeviri metnine çevrilir.
    /// F13 sonrası güvenlik: URL'de SERBEST METİN taşınmaz (içerik sahteciliği / kimlik avı — saldırganın kendi metnini
    /// bizim bandımızda göstermesi). SPA bilinmeyen değeri genel metne düşürür.
    /// </summary>
    public static class ErrorCode
    {
        public const string NoPermission = "yetki_yok";
        public const string NotFound = "bulunamadi";
        public const string NoSession = "oturum_yok";
        public const string Validation = "dogrulama";
        public const string Unexpected = "beklenmeyen";

        public static readonly IReadOnlyList<string> All = [NoPermission, NotFound, NoSession, Validation, Unexpected];
    }

    /// <summary>
    /// SPA Panel'i + hata bandı: <c>/app/panel?hata=&lt;kod&gt;</c>. Yalnız <see cref="ErrorCode"/> kabul edilir (başka
    /// değer <see cref="ArgumentException"/> — sunucu içi programlama hatası); hedef yol sabit. <paramref name="supportCode"/>
    /// yalnız 16–32 hane hex ise eklenir.
    /// </summary>
    public static string ErrorTarget(string code, string? supportCode = null)
    {
        if (!ErrorCode.All.Contains(code)) throw new ArgumentException($"Bilinmeyen hata kodu: {code}", nameof(code));
        var query = QueryString.Create(SpaErrorParameter, code);
        if (supportCode is { Length: >= 16 and <= 32 } && supportCode.All(Uri.IsHexDigit))
            query = query.Add(SpaSupportParameter, supportCode);
        return SpaPanel + query.ToUriComponent();
    }

    /// <summary>
    /// Müşteriye/dış sisteme verilen anonim bağlantılar: <c>/sozlesme/{token}</c> (paylaşılan sözleşme), <c>/feed/…</c>
    /// (iCal). Geçersiz ya da iptal edilmiş bağlantı personel arayüzüne (giriş/Panel) YÖNLENMEZ — müşteri personel
    /// ekranını görmesin; sade bir metin alır (<see cref="PublicLinkNotFoundText"/>).
    /// </summary>
    public static bool IsPublicLinkPath(PathString path)
        => path.StartsWithSegments("/sozlesme", StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments("/feed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Geçersiz/iptal edilmiş anonim bağlantı metni (personel arayüzüne gönderilmez).</summary>
    public const string PublicLinkNotFoundText =
        "Bu bağlantı geçersiz ya da artık kullanılamıyor. Güncel bağlantı için kiralama firmanızla iletişime geçin.";

    /// <summary>
    /// Gövdesiz hata durumunun (StatusCodePages) ya da işlenmemiş istisnanın (ExceptionHandler) tarayıcı hedefi: YALNIZ
    /// sayfa gezinmesi sayılabilecek istek — GET/HEAD, <c>/api/ui</c> değil (orada ProblemDetails), <c>/app</c> değil
    /// (SPA kendi 404'ünü verir), müşteri bağlantısı değil (<see cref="IsPublicLinkPath"/>), dosya adı taşımayan yol
    /// (<c>/favicon.ico</c> gibi uzantılı istek ham durum kodunu alır: tarayıcının arka plan isteği SPA'ya yönlenmesin).
    /// 404 → <c>bulunamadi</c>; 500 → <c>beklenmeyen</c> + destek kodu. Diğer durumlar <c>null</c> (ham durum kodu).
    /// </summary>
    public static string? StatusTarget(string method, PathString path, int status, string? supportCode = null)
    {
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) return null;
        if (RentACar.Web.Api.UiApiExtensions.UiPath(path) || IsSpaPath(path.Value ?? "") || IsPublicLinkPath(path)) return null;
        if (Path.HasExtension(path.Value)) return null;
        return status switch
        {
            StatusCodes.Status404NotFound => ErrorTarget(ErrorCode.NotFound),
            StatusCodes.Status500InternalServerError => ErrorTarget(ErrorCode.Unexpected, supportCode),
            _ => null,
        };
    }

    /// <summary>
    /// Eski Blazor kabuk sayfalarının (GET/HEAD) SPA karşılığı ya da <c>null</c>: <c>/hata</c> → <c>dogrulama</c> (eski
    /// <c>?mesaj=</c> serbest metni YOK SAYILIR — içerik sahteciliği), <c>/yetkisiz</c> → <c>yetki_yok</c>, <c>/Error</c>
    /// → <c>beklenmeyen</c>, <c>/not-found</c> → <c>bulunamadi</c>. Segment eşitliği, büyük/küçük harf duyarsız.
    /// </summary>
    public static string? ShellTarget(PathString path, QueryString query)
    {
        _ = query; // bilinçli: sorgu (ör. ?mesaj=) taşınmaz
        var p = (path.Value ?? "").TrimEnd('/');
        if (p.Equals("/hata", StringComparison.OrdinalIgnoreCase)) return ErrorTarget(ErrorCode.Validation);
        if (p.Equals("/yetkisiz", StringComparison.OrdinalIgnoreCase)) return ErrorTarget(ErrorCode.NoPermission);
        if (p.Equals("/Error", StringComparison.OrdinalIgnoreCase)) return ErrorTarget(ErrorCode.Unexpected);
        if (p.Equals("/not-found", StringComparison.OrdinalIgnoreCase)) return ErrorTarget(ErrorCode.NotFound);
        return null;
    }

    /// <summary>Blazor platform konsolunun kökü (F12 kesişinden sonra yalnız POST uçları ve dosya GET'leri).</summary>
    public const string PlatformConsoleRoot = "/platform";

    /// <summary>Yeni arayüz platform konsolunun kökü (kendi oturumu ve kabuğu).</summary>
    public const string SpaPlatformRoot = SpaHosting.Prefix + "/platform";

    /// <summary>Yeni arayüz platform girişi (anonim).</summary>
    public const string SpaPlatformLogin = SpaPlatformRoot + "/giris";

    /// <summary>Platform operatörünün varsayılan inişi (firma konsolu).</summary>
    public const string SpaPlatformTenants = SpaPlatformRoot + "/kiracilar";

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
    /// <item><b>F12</b> (F12 kesiş): 4 platform konsolu sayfası → <c>/app/platform/…</c> (<c>/platform/login</c> →
    /// <c>giris</c>, <c>/platform/tenants</c> → <c>kiracilar</c>, detay <c>/platform/tenants/{id}</c> →
    /// <c>kiracilar/{id}</c>, <c>/platform/belgeler</c> → <c>belgeler</c>). Platform bir kiracı değildir, pilot kavramı
    /// yoktur: bu blok HER oturum için (oturumsuz dahil) yönlenir (<see cref="IsPlatformConsolePath"/>). Belge indirme
    /// GET'leri ve Blazor POST'ları (<c>/platform/auth/…</c>, <c>/platform/tenants/create</c> …) segment ya da yöntem
    /// farkıyla dışarıda.</item>
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
        // F12
        new("/platform/login", SpaPlatformLogin),
        new("/platform/tenants", SpaPlatformTenants),
        new("/platform/tenants/{id:guid}", SpaPlatformTenants + "/{id}"),
        new("/platform/belgeler", SpaPlatformRoot + "/belgeler"),
    ];

    /// <summary>
    /// Platform konsolu alanı (<c>/platform</c> ve altı; segment eşleşmesi — <c>/platformlar</c> DEĞİL). Haritanın F12
    /// bloğu bu alandadır (401/403 hedefi platform girişi — <see cref="PermissionRedirect"/>).
    /// </summary>
    public static bool IsPlatformConsolePath(PathString path)
        => path.StartsWithSegments(PlatformConsoleRoot, StringComparison.OrdinalIgnoreCase);

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
    /// (POST …) ve diğer yöntemler asla yönlenmez.
    /// </summary>
    public static string? SpaTarget(string method, PathString path, QueryString query)
    {
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)) return null;
        // Sorgu HAM taşınır (tarayıcı zaten yüzde-kodlar). Yazdırılabilir ASCII dışı ham bayt yalnız elle
        // üretilmiş istekte olur; Location başlığına yazılamaz (Kestrel reddeder → 500) → yönlendirme yapılmaz
        // (istek uçsuz kalır → 404 akışı).
        foreach (var c in query.Value ?? "")
            if (c < '!' || c > '~') return null;
        return SpaPath(path.Value) is { } target ? target + query.Value : null;
    }

    /// <summary>
    /// SPA menü rotasının (parametresiz) eski Blazor karşılığı (<c>/app/kiralar</c> → <c>/kiralar</c>) — menü kaydı
    /// testlerinin eski oracle'larıyla eşleşmek için. Haritada yoksa <c>null</c>.
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
    /// <c>/app/…</c> dönüş olduğu gibi (<c>/app/giris…</c> → Panel — döngü yok); haritadaki eski Blazor adresi SPA
    /// karşılığına (<c>/</c> → <c>/app/panel</c>, <c>/kiralar?x</c> → <c>/app/kiralar?x</c>); diğer yerel adresler
    /// (PDF/export gibi dosya uçları — indirme adresi <see cref="PermissionRedirect.SafeReturn"/>'ten geçmez) olduğu gibi.
    /// F13.1b: pilot ayrımı kalktı (herkes yeni arayüzde).
    /// </summary>
    public static string AfterLogin(string? returnInfo)
    {
        var g = PermissionRedirect.SafeReturn(returnInfo);
        var end = g.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? g : g[..end];
        var remaining = end < 0 ? "" : g[end..];

        if (IsSpaPath(path)) return IsSpaEntryPath(path) ? SpaPanel : g;
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
