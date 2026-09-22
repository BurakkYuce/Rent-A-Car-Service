using Microsoft.AspNetCore.WebUtilities;
using RentACar.Web.Identity;

namespace RentACar.Web.Spa;

/// <summary>
/// F4.6 ilk kesiş — Blazor ↔ yeni arayüz (<c>/app</c>) geçiş kararları. SAF fonksiyonlar (Program.cs ve
/// middleware test edilemez; karar burada, birim testiyle kilitli). Uygulayan: <see cref="IlkKesisMiddleware"/>.
///
/// <para><b>Yönlendirme haritası</b> (<see cref="Harita"/>): F4 envanterinde silinecek Blazor <c>@page</c>
/// ŞABLONLARINDAN türetilmiş AÇIK liste — önek eşleşmesi YOK. Bu yüzden aynı öneki paylaşan GET uçları
/// (<c>/kiralar/{id}/pdf</c>, <c>/kiralar/hesapla</c>, <c>/kiralar/donus-hesapla</c>, <c>/kiralar/musait-arac</c>,
/// <c>/kiralar/ornek-sozlesme/pdf</c>, export, makbuz…) YÖNLENMEZ: bir şablonla segment segment birebir
/// eşleşmeyen yol haritada yoktur. Yalnız GET/HEAD, yalnız PİLOT kiracının oturumu (middleware karar verir).
/// Sorgu dizesi AYNEN taşınır (<c>?varac=…&amp;vfrom=…</c>); fragment tarayıcıda kalır (Location'a eklenmez —
/// tarayıcı fragment'sız Location'da özgün fragment'ı korur). 302 (geçici; F13'te kalıcı olur).</para>
///
/// <para><b>Tek giriş</b>: <c>GET /login</c> artık form çizmez. Oturumsuz → <c>/app/giris</c> (dönüş adresi
/// <see cref="YetkiYonlendirme.GuvenliDonus"/>'ten geçerek taşınır); oturumlu → <see cref="GirisSonrasi"/>.
/// SPA girişi Blazor dönüş adresini kendisi çözmez, <c>/login?ReturnUrl=</c>'e geri verir: açık yönlendirme
/// çiti TEK yerde (sunucu) kalır. <b>Döngü yok</b>: <c>/app</c> anonim (cookie challenge almaz), <c>/login</c>
/// oturumsuzken yalnız <c>/app/giris</c>'e, oturumluyken hiçbir zaman <c>/login</c>'e ya da <c>/app/giris</c>'e
/// yönlendirmez (GuvenliDonus <c>/login</c>'i reddeder; <c>/app/giris</c> burada Panel'e çevrilir).</para>
/// </summary>
public static class IlkKesis
{
    /// <summary>Blazor giriş sayfası (cookie şemasının <c>LoginPath</c>'i).</summary>
    public const string BlazorGiris = "/login";

    /// <summary>Yeni arayüz giriş sayfası (anonim).</summary>
    public const string SpaGiris = SpaBarindirma.Onek + "/giris";

    /// <summary>Pilot kiracının varsayılan inişi.</summary>
    public const string SpaPanel = SpaBarindirma.Onek + "/panel";

    /// <summary>SPA'nın dönüş adresi parametresi (Angular <c>queryParamMap</c> büyük/küçük harf duyarlı).</summary>
    public const string SpaDonusParametresi = "returnUrl";

    /// <summary>SPA giriş sayfasının bilgi mesajı parametresi (<c>kiraci_kapali</c>, <c>cikis</c>).</summary>
    public const string SpaNedenParametresi = "neden";

    /// <summary>Kaynak (<c>@page</c> şablonu) → SPA şablonu. Tek parametre biçimi <c>{id:guid}</c> → <c>{id}</c>.</summary>
    public sealed record Eslem(string Kaynak, string Hedef);

    /// <summary>
    /// F4 envanteri (<c>docs/roadmap/F4.md</c>): <c>Home.razor</c> <c>/</c>, <c>RentalList.razor</c> <c>/kiralar</c>,
    /// <c>KiraForm.razor</c> <c>/kiralar/yeni</c> + <c>/kiralar/{Id:guid}</c>, <c>RentalPrint.razor</c>
    /// <c>/kiralar/{Id:guid}/yazdir</c>. <c>Login.razor</c> (<c>/login</c>) haritada DEĞİL — pilot olsun olmasın
    /// herkes için <see cref="GirisYonlendirmesi"/>/<see cref="GirisSonrasi"/>. <c>IlkKesisTests</c> bu listeyi
    /// sayfaların gerçek <c>@page</c> satırlarıyla karşılaştırır.
    /// </summary>
    public static IReadOnlyList<Eslem> Harita { get; } =
    [
        new("/", SpaPanel),
        new("/kiralar", SpaBarindirma.Onek + "/kiralar"),
        new("/kiralar/yeni", SpaBarindirma.Onek + "/kiralar/yeni"),
        new("/kiralar/{id:guid}", SpaBarindirma.Onek + "/kiralar/{id}"),
        new("/kiralar/{id:guid}/yazdir", SpaBarindirma.Onek + "/kiralar/{id}/yazdir"),
    ];

    private const string GuidParametre = "{id:guid}";

    /// <summary>
    /// Yol haritadaki bir şablonla birebir eşleşiyorsa SPA yolu (sorgusuz), değilse <c>null</c>.
    /// Karşılaştırma segment segment, büyük/küçük harf duyarsız (ASP.NET yönlendirmesi gibi); sondaki
    /// <c>/</c> yok sayılır; <c>{id:guid}</c> yalnız Guid'e uyar ve hedefe kanonik (<c>D</c>) yazılır.
    /// </summary>
    public static string? SpaYolu(string? yol)
    {
        if (string.IsNullOrEmpty(yol) || yol[0] != '/') return null;
        var parcalar = Parcala(yol);
        if (parcalar is null) return null;
        foreach (var e in Harita)
        {
            var sablon = Parcala(e.Kaynak)!;
            if (sablon.Length != parcalar.Length) continue;
            Guid? id = null;
            var uyar = true;
            for (var i = 0; i < sablon.Length && uyar; i++)
            {
                if (sablon[i] == GuidParametre)
                {
                    if (Guid.TryParse(parcalar[i], out var g)) id = g;
                    else uyar = false;
                }
                else uyar = string.Equals(sablon[i], parcalar[i], StringComparison.OrdinalIgnoreCase);
            }
            if (!uyar) continue;
            return id is { } v ? e.Hedef.Replace("{id}", v.ToString("D"), StringComparison.Ordinal) : e.Hedef;
        }
        return null;
    }

    /// <summary>
    /// İsteğin SPA hedefi (yol + AYNEN sorgu) ya da <c>null</c>. YALNIZ GET/HEAD: form gönderimleri
    /// (POST <c>/kiralar/create</c> …) ve diğer yöntemler asla yönlenmez. Pilot kararı çağıranın (middleware).
    /// </summary>
    public static string? SpaHedefi(string yontem, PathString yol, QueryString sorgu)
    {
        if (!HttpMethods.IsGet(yontem) && !HttpMethods.IsHead(yontem)) return null;
        // Sorgu HAM taşınır (tarayıcı zaten yüzde-kodlar). Yazdırılabilir ASCII dışı ham bayt yalnız elle
        // üretilmiş istekte olur; Location başlığına yazılamaz (Kestrel reddeder → 500) → yönlendirme yapılmaz,
        // Blazor sayfası açılır.
        foreach (var c in sorgu.Value ?? "")
            if (c < '!' || c > '~') return null;
        return SpaYolu(yol.Value) is { } hedef ? hedef + sorgu.Value : null;
    }

    /// <summary>
    /// SPA menü rotasının (parametresiz) Blazor karşılığı — pilot OLMAYAN kiracıda Blazor menüsü bunu açar
    /// (<c>/app/kiralar</c> → <c>/kiralar</c>). Haritada yoksa <c>null</c>.
    /// </summary>
    public static string? BlazorKarsiligi(string spaRota)
        => Harita.FirstOrDefault(e => !e.Kaynak.Contains('{') && string.Equals(e.Hedef, spaRota, StringComparison.OrdinalIgnoreCase))?.Kaynak;

    /// <summary><c>/login</c> (ya da <c>/login/</c>) mı — segment eşitliği; <c>/platform/login</c>, <c>/loginx</c> DEĞİL.</summary>
    public static bool BlazorGirisMi(PathString yol)
    {
        var v = yol.Value;
        if (string.IsNullOrEmpty(v)) return false;
        if (v.Length > 1 && v[^1] == '/') v = v[..^1];
        return string.Equals(v, BlazorGiris, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Oturumsuz <c>GET /login</c> → <c>/app/giris</c>. <c>ReturnUrl</c> yalnız <see cref="YetkiYonlendirme.GuvenliDonus"/>'ten
    /// geçerse (Panel değilse) SPA'nın <c>returnUrl</c>'ine yazılır — yabancı/indirme/döngü adresi hiç taşınmaz.
    /// <c>?hata=kapali</c> (kapatılan firmanın oturumu düşürüldü, <c>TenantActiveMiddleware</c>) →
    /// <c>?neden=kiraci_kapali</c> (SPA mesajı). Diğer parametreler taşınmaz.
    /// </summary>
    public static string GirisYonlendirmesi(QueryString sorgu)
    {
        var q = QueryHelpers.ParseQuery(sorgu.Value);
        var yeni = QueryString.Empty;
        if (q.TryGetValue(YetkiYonlendirme.DonusParametresi, out var ham)
            && YetkiYonlendirme.GuvenliDonus(ham.ToString()) is var donus && donus != YetkiYonlendirme.Varsayilan)
            yeni = yeni.Add(SpaDonusParametresi, donus);
        if (q.TryGetValue("hata", out var hata) && hata.ToString() == "kapali")
            yeni = yeni.Add(SpaNedenParametresi, "kiraci_kapali");
        return SpaGiris + yeni.ToUriComponent();
    }

    /// <summary>
    /// Oturum açmış firma kullanıcısının <c>/login</c>'den sonraki hedefi (SPA girişi de Blazor dönüş adresini
    /// buraya verir). Dönüş önce <see cref="YetkiYonlendirme.GuvenliDonus"/>'ten geçer (açık yönlendirme çiti).
    /// <list type="bullet">
    /// <item>Pilot: <c>/app/…</c> dönüş olduğu gibi (<c>/app/giris…</c> → Panel — döngü yok); haritadaki Blazor
    /// adresi SPA karşılığına (<c>/</c> → <c>/app/panel</c>, <c>/kiralar?x</c> → <c>/app/kiralar?x</c>); diğer
    /// Blazor sayfaları (henüz taşınmamış modüller) olduğu gibi.</item>
    /// <item>Pilot değil: <c>/app/…</c> dönüş → Blazor Panel (<c>/</c>; yeni arayüz bu firmada kapalı), diğerleri
    /// olduğu gibi.</item>
    /// </list>
    /// </summary>
    public static string GirisSonrasi(bool pilot, string? donus)
    {
        var g = YetkiYonlendirme.GuvenliDonus(donus);
        var bitis = g.AsSpan().IndexOfAny('?', '#');
        var yol = bitis < 0 ? g : g[..bitis];
        var kalan = bitis < 0 ? "" : g[bitis..];
        var spa = SpaYoluMu(yol);

        if (!pilot) return spa ? YetkiYonlendirme.Varsayilan : g;
        if (spa) return SpaGirisYoluMu(yol) ? SpaPanel : g;
        return SpaYolu(yol) is { } hedef ? hedef + kalan : g;
    }

    /// <summary><c>/app</c> ya da altı (segment sınırı: <c>/apps</c> DEĞİL).</summary>
    public static bool SpaYoluMu(string yol)
        => new PathString(yol).StartsWithSegments(SpaBarindirma.Onek, StringComparison.OrdinalIgnoreCase);

    private static bool SpaGirisYoluMu(string yol)
        => new PathString(yol).StartsWithSegments(SpaGiris, StringComparison.OrdinalIgnoreCase);

    /// <summary>Yolu segmentlere ayırır (sondaki <c>/</c> yok sayılır, kök = boş dizi). Boş ara segment → <c>null</c>.</summary>
    private static string[]? Parcala(string yol)
    {
        var v = yol.Length > 1 && yol[^1] == '/' ? yol[..^1] : yol;
        if (v == "/") return [];
        var parcalar = v[1..].Split('/');
        return parcalar.Any(p => p.Length == 0) ? null : parcalar;
    }
}
