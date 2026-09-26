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
public static partial class SpaHosting
{
    public const string Prefix = "/app";
    public const string SettingKey = "Spa:Dizin";
    public const string DefaultDirectory = "../app/browser";
    public const string IndexFile = "index.html";

    /// <summary>Kabuk (index.html) ve hash'siz dosyalar: her seferinde yeniden doğrula (ETag/Last-Modified).</summary>
    public const string Revalidate = "no-cache";

    /// <summary>İçerik hash'li dosyalar: adı içerikle değiştiği için sonsuza dek önbelleklenebilir.</summary>
    public const string Persistent = "public, max-age=31536000, immutable";

    /// <summary>
    /// <c>Spa:Dizin</c> ayarını mutlak köke çevirir: boşsa varsayılan, göreliyse content root'a göre.
    /// </summary>
    public static string RootDirectory(string contentRoot, string? setting)
    {
        var directory = string.IsNullOrWhiteSpace(setting) ? DefaultDirectory : setting.Trim();
        return Path.GetFullPath(Path.Combine(contentRoot, directory));
    }

    /// <summary>
    /// İstenen yolun son parçasında uzantı var mı? Uzantılı istek bir DOSYA istemektir: yoksa 404
    /// (index.html'e düşülürse tarayıcı HTML'i JS diye yükler, hata gizlenir). Uzantısız istek
    /// istemci tarafı rotadır (<c>/app/kiralar/5</c>) → index.html.
    /// </summary>
    public static bool IsFileRequest(string subPath)
    {
        var lastPart = subPath.TrimEnd('/');
        var cut = lastPart.LastIndexOf('/');
        if (cut >= 0) lastPart = lastPart[(cut + 1)..];
        return lastPart.Contains('.');
    }

    /// <summary>
    /// Dosya adına göre <c>Cache-Control</c>. Angular (esbuild) çıktısı <c>ad-HASH.uzanti</c> biçimindedir
    /// (HASH: 8+ büyük harf/rakam; <c>main-2QJ7N5ZT.js</c>, <c>chunk-4GXQWSUJ.js</c>, <c>styles-5INURTSO.css</c>,
    /// <c>media/font-ABCD1234.woff2</c>) → kalıcı. index.html ve hash'siz her şey (<c>favicon.ico</c>) →
    /// yeniden doğrula: yayından sonra eski kabuk/ikon takılı kalmaz.
    /// </summary>
    public static string CacheHeader(string subPath)
    {
        var name = Path.GetFileName(subPath.TrimEnd('/'));
        return HashedFile().IsMatch(name) ? Persistent : Revalidate;
    }

    [GeneratedRegex(@"^.+-[A-Z0-9]{8,}\.(js|mjs|css|map|woff2?|ttf|otf|eot|svg|png|jpe?g|gif|webp|avif|ico)$")]
    private static partial Regex HashedFile();

    /// <summary>
    /// <c>/app</c> ve altını anonim uç olarak eşler. GET/HEAD dışı yöntemler bu uca düşmez.
    /// </summary>
    public static IEndpointConventionBuilder MapSpaHosting(this WebApplication app)
    {
        var setting = app.Configuration[SettingKey];
        var root = RootDirectory(app.Environment.ContentRootPath, setting);
        if (!string.IsNullOrWhiteSpace(setting) && Path.IsPathRooted(setting.Trim()))
            app.Logger.LogWarning(
                "Spa:Dizin mutlak ({Dizin}); release'e göreli yol (../app/browser) önerilir — swap sonrası eski süreç yeni SPA'yı servis edebilir.",
                setting);
        if (File.Exists(Path.Combine(root, IndexFile)))
            app.Logger.LogInformation("Yeni arayüz /app altında sunuluyor: {Dizin}", root);
        else
            app.Logger.LogWarning("Yeni arayüz bulunamadı ({Dizin}/index.html yok) — /app 404 döner.", root);

        var source = new SpaSource(root);
        var types = new FileExtensionContentTypeProvider();

        return app.MapMethods(Prefix + "/{**yol}", [HttpMethods.Get, HttpMethods.Head],
                (HttpContext ctx) => Sun(ctx, source, types))
            .AllowAnonymous()           // cookie challenge YOK (döngü koruması)
            .ExcludeFromDescription()   // OpenAPI'de görünmez (API değil)
            .WithDisplayName("SPA /app");
    }

    private static IResult Sun(HttpContext ctx, SpaSource source, IContentTypeProvider types)
    {
        // Durum sayfası re-execute'u kapalı: SPA'nın 404'ü /not-found Blazor sayfasına (HTML) dönüşmesin;
        // eksik .js için tarayıcı ham 404 görmeli.
        var scp = ctx.Features.Get<IStatusCodePagesFeature>();
        if (scp is not null) scp.Enabled = false;

        ctx.Request.Path.StartsWithSegments(Prefix, out var remaining);
        var subPath = remaining.Value ?? "";

        // "/app" → "/app/": index.html'deki <base href="/app/"> ve göreli varlık yolları eğik çizgiyle çözülür.
        if (subPath.Length == 0)
            return Results.Redirect($"{ctx.Request.PathBase}{Prefix}/{ctx.Request.QueryString}", permanent: true);

        var file = subPath == "/" ? null : source.Find(subPath);
        if (file is null)
        {
            if (subPath != "/" && IsFileRequest(subPath))
                return NotFound("Dosya bulunamadı.");
            file = source.Find(IndexFile);
            if (file is null)
                return NotFound("Yeni arayüz bu sunucuda kurulu değil (Spa:Dizin).");
        }

        if (!types.TryGetContentType(file.Name, out var type)) type = "application/octet-stream";
        ctx.Response.Headers.CacheControl = CacheHeader(file.Name);
        return Results.File(file.PhysicalPath!, type, lastModified: file.LastModified);
    }

    private static IResult NotFound(string message)
        => Results.Text(message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);

    /// <summary>
    /// SPA kökünü TEMBEL açar: dizin açılışta yoksa (henüz dağıtılmamış) sağlayıcı oluşturulmaz
    /// (<see cref="PhysicalFileProvider"/> var olmayan kökte fırlatır) ve istek 404'e düşer.
    /// Sağlayıcı <c>..</c> ile kökten çıkmayı ve nokta-önekli/gizli dosyaları zaten reddeder.
    /// </summary>
    private sealed class SpaSource(string root)
    {
        private PhysicalFileProvider? _provider;

        public IFileInfo? Find(string subPath)
        {
            var provider = Volatile.Read(ref _provider);
            if (provider is null)
            {
                if (!Directory.Exists(root)) return null;
                var newItem = new PhysicalFileProvider(root);
                provider = Interlocked.CompareExchange(ref _provider, newItem, null) ?? newItem;
                if (!ReferenceEquals(provider, newItem)) newItem.Dispose();
            }

            var file = provider.GetFileInfo(subPath);
            return file is { Exists: true, IsDirectory: false, PhysicalPath: not null } ? file : null;
        }
    }
}
