using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace RentACar.Web.Spa;

/// <summary>
/// Yeni arayüzün (Angular) derlenmiş statik kabuğunu <c>/app</c> altında sunar (roadmap F1.5).
///
/// <para><b>ANONİM</b>: kabuk ve fallback cookie challenge'ı ALMAZ. Oturum isteseydi
/// <c>/app</c> → <c>/login</c> → (girişten sonra) <c>/app/giris</c> → <c>/login</c> döngüsü doğardı.
/// Kabukta veri yok; veri <c>/api/ui</c>'da kendi yetkisiyle korunur.</para>
///
/// <para><b>Dizin</b>: <c>Spa:Dizin</c>, content root'a GÖRELİ çözülür (varsayılan <c>../app/browser</c>).
/// Üretimde content root = <c>WorkingDirectory</c> (<c>/opt/racar/current/web</c>); süreç çalışma dizinini
/// açılışta GERÇEK release yoluna çözdüğü için göreli yol o release'in SPA'sını gösterir — sembolik bağ
/// swap'ı ile restart arasında eski süreç yeni SPA'yı servis etmez. Mutlak yol çalışır ama bu garantiyi
/// kaybeder (uyarı loglanır).</para>
///
/// <para><b>Dizin/index.html yoksa</b> açılış BOZULMAZ; <c>/app/*</c> açıklayıcı 404 döner (500 değil).</para>
///
/// <para>Güvenlik başlıkları/CSP Program.cs'teki genel middleware'den gelir (uç, boru hattının sonunda
/// çalışır) — burada ayrıca başlık yazılmaz ve CSP gevşetilmez.</para>
/// </summary>
public static partial class SpaBarindirma
{
    public const string Onek = "/app";
    public const string AyarAnahtari = "Spa:Dizin";
    public const string VarsayilanDizin = "../app/browser";
    public const string IndexDosyasi = "index.html";

    /// <summary>Kabuk (index.html) ve hash'siz dosyalar: her seferinde yeniden doğrula (ETag/Last-Modified).</summary>
    public const string YenidenDogrula = "no-cache";

    /// <summary>İçerik hash'li dosyalar: adı içerikle değiştiği için sonsuza dek önbelleklenebilir.</summary>
    public const string Kalici = "public, max-age=31536000, immutable";

    /// <summary>
    /// <c>Spa:Dizin</c> ayarını mutlak köke çevirir: boşsa varsayılan, göreliyse content root'a göre.
    /// </summary>
    public static string KokDizin(string contentRoot, string? ayar)
    {
        var dizin = string.IsNullOrWhiteSpace(ayar) ? VarsayilanDizin : ayar.Trim();
        return Path.GetFullPath(Path.Combine(contentRoot, dizin));
    }

    /// <summary>
    /// İstenen yolun son parçasında uzantı var mı? Uzantılı istek bir DOSYA istemektir: yoksa 404
    /// (index.html'e düşülürse tarayıcı HTML'i JS diye yükler, hata gizlenir). Uzantısız istek
    /// istemci tarafı rotadır (<c>/app/kiralar/5</c>) → index.html.
    /// </summary>
    public static bool DosyaIstegiMi(string altYol)
    {
        var sonParca = altYol.TrimEnd('/');
        var kesme = sonParca.LastIndexOf('/');
        if (kesme >= 0) sonParca = sonParca[(kesme + 1)..];
        return sonParca.Contains('.');
    }

    /// <summary>
    /// Dosya adına göre <c>Cache-Control</c>. Angular (esbuild) çıktısı <c>ad-HASH.uzanti</c> biçimindedir
    /// (HASH: 8+ büyük harf/rakam; <c>main-2QJ7N5ZT.js</c>, <c>chunk-4GXQWSUJ.js</c>, <c>styles-5INURTSO.css</c>,
    /// <c>media/font-ABCD1234.woff2</c>) → kalıcı. index.html ve hash'siz her şey (<c>favicon.ico</c>) →
    /// yeniden doğrula: yayından sonra eski kabuk/ikon takılı kalmaz.
    /// </summary>
    public static string OnbellekBasligi(string altYol)
    {
        var ad = Path.GetFileName(altYol.TrimEnd('/'));
        return HashliDosya().IsMatch(ad) ? Kalici : YenidenDogrula;
    }

    [GeneratedRegex(@"^.+-[A-Z0-9]{8,}\.(js|mjs|css|map|woff2?|ttf|otf|eot|svg|png|jpe?g|gif|webp|avif|ico)$")]
    private static partial Regex HashliDosya();

    /// <summary>
    /// <c>/app</c> ve altını anonim uç olarak eşler. GET/HEAD dışı yöntemler bu uca düşmez.
    /// </summary>
    public static IEndpointConventionBuilder MapSpaBarindirma(this WebApplication app)
    {
        var ayar = app.Configuration[AyarAnahtari];
        var kok = KokDizin(app.Environment.ContentRootPath, ayar);
        if (!string.IsNullOrWhiteSpace(ayar) && Path.IsPathRooted(ayar.Trim()))
            app.Logger.LogWarning(
                "Spa:Dizin mutlak ({Dizin}); release'e göreli yol (../app/browser) önerilir — swap sonrası eski süreç yeni SPA'yı servis edebilir.",
                ayar);
        if (File.Exists(Path.Combine(kok, IndexDosyasi)))
            app.Logger.LogInformation("Yeni arayüz /app altında sunuluyor: {Dizin}", kok);
        else
            app.Logger.LogWarning("Yeni arayüz bulunamadı ({Dizin}/index.html yok) — /app 404 döner.", kok);

        var kaynak = new SpaKaynak(kok);
        var turler = new FileExtensionContentTypeProvider();

        return app.MapMethods(Onek + "/{**yol}", [HttpMethods.Get, HttpMethods.Head],
                (HttpContext ctx) => Sun(ctx, kaynak, turler))
            .AllowAnonymous()           // cookie challenge YOK (döngü koruması)
            .ExcludeFromDescription()   // OpenAPI'de görünmez (API değil)
            .WithDisplayName("SPA /app");
    }

    private static IResult Sun(HttpContext ctx, SpaKaynak kaynak, IContentTypeProvider turler)
    {
        // Durum sayfası re-execute'u kapalı: SPA'nın 404'ü /not-found Blazor sayfasına (HTML) dönüşmesin;
        // eksik .js için tarayıcı ham 404 görmeli.
        var scp = ctx.Features.Get<IStatusCodePagesFeature>();
        if (scp is not null) scp.Enabled = false;

        ctx.Request.Path.StartsWithSegments(Onek, out var kalan);
        var altYol = kalan.Value ?? "";

        // "/app" → "/app/": index.html'deki <base href="/app/"> ve göreli varlık yolları eğik çizgiyle çözülür.
        if (altYol.Length == 0)
            return Results.Redirect($"{ctx.Request.PathBase}{Onek}/{ctx.Request.QueryString}", permanent: true);

        var dosya = altYol == "/" ? null : kaynak.Bul(altYol);
        if (dosya is null)
        {
            if (altYol != "/" && DosyaIstegiMi(altYol))
                return Bulunamadi("Dosya bulunamadı.");
            dosya = kaynak.Bul(IndexDosyasi);
            if (dosya is null)
                return Bulunamadi("Yeni arayüz bu sunucuda kurulu değil (Spa:Dizin).");
        }

        if (!turler.TryGetContentType(dosya.Name, out var tur)) tur = "application/octet-stream";
        ctx.Response.Headers.CacheControl = OnbellekBasligi(dosya.Name);
        return Results.File(dosya.PhysicalPath!, tur, lastModified: dosya.LastModified);
    }

    private static IResult Bulunamadi(string mesaj)
        => Results.Text(mesaj, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// SPA kökünü TEMBEL açar: dizin açılışta yoksa (henüz dağıtılmamış) sağlayıcı oluşturulmaz
    /// (<see cref="PhysicalFileProvider"/> var olmayan kökte fırlatır) ve istek 404'e düşer.
    /// Sağlayıcı <c>..</c> ile kökten çıkmayı ve nokta-önekli/gizli dosyaları zaten reddeder.
    /// </summary>
    private sealed class SpaKaynak(string kok)
    {
        private PhysicalFileProvider? _saglayici;

        public IFileInfo? Bul(string altYol)
        {
            var saglayici = Volatile.Read(ref _saglayici);
            if (saglayici is null)
            {
                if (!Directory.Exists(kok)) return null;
                var yeni = new PhysicalFileProvider(kok);
                saglayici = Interlocked.CompareExchange(ref _saglayici, yeni, null) ?? yeni;
                if (!ReferenceEquals(saglayici, yeni)) yeni.Dispose();
            }

            var dosya = saglayici.GetFileInfo(altYol);
            return dosya is { Exists: true, IsDirectory: false, PhysicalPath: not null } ? dosya : null;
        }
    }
}
