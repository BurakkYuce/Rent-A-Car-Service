using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.Web.Identity;
using Settings = RentACar.Domain.Entities.TenantSettings;

namespace RentACar.Web.Api.Oturum;

/// <summary>
/// <c>/api/ui/v1/oturum</c> — yeni arayüzün oturum uçları (F1.2).
/// <list type="bullet">
/// <item><c>GET xsrf</c>: anonim; CSRF belirtecini verir (SPA ilk güvensiz istekten — girişten — önce çağırır).</item>
/// <item><c>POST giris</c>: Blazor <c>/auth/login</c> ile AYNI kimlik doğrulaması (<see cref="LoginService"/>),
/// AYNI claim seti (<see cref="OturumPrincipal"/>), AYNI cookie şeması ve AYNI hız sınırı (<c>login</c>).</item>
/// <item><c>POST cikis</c>: oturumu kapatır; anonim kimliğe bağlı yeni belirteç verir.</item>
/// <item><c>GET ben</c>: kullanıcı, firma, rol, etkin izinler (kullanıcı-bazlı istisnalar dahil), şube kapsamı,
/// modül bayrakları, firma renkleri, pilot bayrağı.</item>
/// </list>
/// İzin kapısı bilinçli olarak yok (<see cref="AuthExtensions.IzinMuaf{TBuilder}"/>); pilot kapısından muaf
/// (pilot olmayan firmanın kullanıcısı "yeni arayüz açık değil" bandını <c>ben.pilot</c>'tan okur).
/// </summary>
public static class OturumApi
{
    public sealed record GirisIstegi(string? Firma, string? Kullanici, string? Sifre);

    public sealed record BenYaniti(
        BenKullanici Kullanici,
        BenKiraci Kiraci,
        string Rol,
        IReadOnlyList<string> Izinler,
        BenSubeKapsami SubeKapsami,
        BenModuller Moduller,
        IReadOnlyDictionary<string, string> Renkler,
        bool Pilot);

    public sealed record BenKullanici(Guid Id, string KullaniciAdi, string? AdSoyad);
    public sealed record BenKiraci(Guid Id, string Kod, string Ad);
    /// <summary><c>TumSubeler</c> true ise kapsam sınırsız; değilse operatör yalnız bu şubeyi görür.</summary>
    public sealed record BenSubeKapsami(bool TumSubeler, Guid? SubeId, string? SubeAd);
    public sealed record BenModuller(bool WebSitesi);

    // TUZAK: yalnız (HttpContext) alıp Task<T> dönen yöntem grubu MapGet/MapPost'un RequestDelegate
    // aşırı yüklemesine bağlanır → dönüş değeri YOK SAYILIR, 200 boş gövde gider. Bu yüzden ben/cikis
    // ikinci bir parametre (CancellationToken) alır; Delegate aşırı yüklemesi seçilir.
    public static RouteGroupBuilder MapOturumApi(this RouteGroupBuilder v1)
    {
        var g = v1.MapGroup("/oturum")
            .IzinMuaf("Oturum uçları: giriş anonim olmak zorunda; ben/çıkış yalnız çağıranın KENDİ oturumunu okur/kapatır.");

        g.MapGet("/xsrf", Xsrf).AllowAnonymous();
        g.MapPost("/giris", Giris).AllowAnonymous().RequireRateLimiting("login"); // brute-force: /auth/login ile aynı politika
        g.MapPost("/cikis", Cikis).AllowAnonymous(); // oturumsuz çıkış da 204 (idempotent)
        g.MapGet("/ben", Ben);
        return g;
    }

    private static NoContent Xsrf(HttpContext http)
    {
        UiApiExtensions.XsrfVer(http);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<BenYaniti>, ProblemHttpResult>> Giris(
        GirisIstegi istek, HttpContext http, LoginService login)
    {
        var sonuc = await login.ValidateAsync(istek.Firma ?? "", istek.Kullanici ?? "", istek.Sifre ?? "", http.RequestAborted);
        if (sonuc is null)
            // Hangi alanın yanlış olduğu SÖYLENMEZ (kullanıcı/firma keşfi olmasın). 401 değil: SPA 401'i
            // "oturum düştü" diyaloğu olarak ele alır; giriş formunda bu bir form hatasıdır.
            return UiHata.Problem(UiHata.Dogrulama, "Firma kodu, kullanıcı adı ya da şifre hatalı.");

        var principal = OturumPrincipal.Olustur(sonuc);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        // SIRA KRİTİK: antiforgery belirteci ctx.User'a bağlanır. Önce yeni principal, SONRA belirteç —
        // girişten önce alınmış (anonim) belirteç bundan sonra reddedilir.
        http.User = principal;
        UiApiExtensions.XsrfVer(http);

        return await BenOlusturAsync(http) is { } ben
            ? TypedResults.Ok(ben)
            : UiHata.Problem(UiHata.OturumYok, "Oturum kurulamadı.");
    }

    private static async Task<NoContent> Cikis(HttpContext http, CancellationToken ct)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        http.User = new ClaimsPrincipal(new ClaimsIdentity()); // SIRA KRİTİK: belirteç anonim kimliğe bağlansın
        UiApiExtensions.XsrfVer(http);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<BenYaniti>, ProblemHttpResult>> Ben(HttpContext http, CancellationToken ct)
    {
        if (await BenOlusturAsync(http) is not { } ben)
            // Oturum var ama FİRMA oturumu değil (platform operatörü): yeni arayüz için oturum yok.
            return UiHata.Problem(UiHata.OturumYok, "Firma oturumu yok.");
        UiApiExtensions.XsrfVer(http); // sayfa yenilemede belirteç tazelenir
        return TypedResults.Ok(ben);
    }

    private static async Task<BenYaniti?> BenOlusturAsync(HttpContext http)
    {
        var u = http.User;
        if (u.Identity?.IsAuthenticated != true
            || !Guid.TryParse(u.FindFirst(IdentityClaims.TenantId)?.Value, out var tenantId)
            || !Guid.TryParse(u.FindFirst(IdentityClaims.UserId)?.Value, out var userId))
            return null;

        var sp = http.RequestServices;
        var ct = http.RequestAborted;
        var current = sp.GetRequiredService<ICurrentUser>(); // HybridIdentity: http.User'ı CANLI okur

        string? adSoyad;
        string kiraciAd;
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ct))
        {
            adSoyad = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.DisplayName).FirstOrDefaultAsync(ct);
            kiraciAd = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId).Select(x => x.Name).FirstOrDefaultAsync(ct) ?? "";
        }

        var ayar = await sp.GetRequiredService<ITenantSettingsRepository>().GetAsync(ct);
        var webSitesi = await sp.GetRequiredService<TenantStatusCache>().WebsiteModuleAsync(tenantId, ct);
        var sube = BranchScope.EffectiveFilter(current);

        return new BenYaniti(
            new BenKullanici(userId, u.FindFirst(ClaimTypes.Name)?.Value ?? "", adSoyad),
            new BenKiraci(tenantId, u.FindFirst(IdentityClaims.TenantCode)?.Value ?? "", kiraciAd),
            u.FindFirst(ClaimTypes.Role)?.Value ?? "",
            // Etkin izin = rol matrisi + kullanıcı-bazlı ek − yasak; Blazor uç kapılarıyla AYNI fonksiyon.
            Enum.GetValues<Permission>().Where(p => AuthExtensions.HasPermission(u, p)).Select(p => p.ToString()).ToList(),
            new BenSubeKapsami(sube.Unrestricted, sube.SubeId, sube.SubeAd),
            new BenModuller(webSitesi),
            Renkler(ayar),
            ayar?.YeniArayuzPilot == true);
    }

    private static readonly Regex HexRenk = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    /// <summary>Firma renk kodları — yalnız geçerli <c>#rrggbb</c> değerler (MainLayout'taki CSS değişkeni adlarıyla).
    /// Seçilmemiş renk HİÇ dönmez → SPA varsayılan temayı kullanır.</summary>
    private static Dictionary<string, string> Renkler(Settings? a)
    {
        var d = new Dictionary<string, string>();
        if (a is null) return d;
        void Ekle(string ad, string? renk)
        {
            if (renk is not null && HexRenk.IsMatch(renk)) d[ad] = renk;
        }
        Ekle("gecikenler", a.RenkGecikenler);
        Ekle("bugun-donecekler", a.RenkBugunDonecekler);
        Ekle("bugun-cikacaklar", a.RenkBugunCikacaklar);
        Ekle("opsiyonlu", a.RenkOpsiyonlu);
        Ekle("limit-bakiye", a.RenkLimitBakiye);
        Ekle("alacakli", a.RenkAlacakli);
        Ekle("rez-atanan-plaka", a.RenkRezAtananPlaka);
        Ekle("kiralanmayan", a.RenkKiralanmayan);
        return d;
    }
}
