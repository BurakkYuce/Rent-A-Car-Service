using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul C-CRMSIL / D-4 + güvenlik incelemesi F3 — YAPISAL ÇİT: <c>/api/ui/v1</c> altındaki her DELETE ucu ve her
/// yıkıcı POST ucu (son parçası <c>iptal</c>/<c>toplu-iptal</c> ya da <c>-sil</c> ile biten; güvenlik takip a) gerçek uç
/// tablosundan (<see cref="EndpointDataSource"/>) taranır ve rol matrisinin silme iznini ister:
/// <see cref="Permission.OperationsDelete"/> (operatör siler DEĞİL) ya da finans kaydında
/// <see cref="Permission.FinanceReverse"/>. Bilinçli istisnalar aşağıda GEREKÇESİYLE listelidir; yeni bir uç ya izni taşır
/// ya da buraya gerekçeyle eklenir (sessiz geçiş yok). Elle sayım değil tablo taraması: kabul koşusu elle bulunanın
/// dışında 15+ ucu yakaladı.
/// </summary>
[Collection("web")]
public sealed class DeletePermissionFenceTests(WebFixture fx)
{
    /// <summary>DELETE rota şablonu → gerekçe. Silme izni ARANMAZ (izin kapısı yine zorunlu — UiApiYapisalTests).</summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/api/ui/v1/tablo-duzenleri/{tabloKodu}"] = "Kullanıcının kendi tablo düzeni (kişisel tercih, iş kaydı değil).",
        ["/api/ui/v1/kullanicilar/{id:guid}/istisnalar/{izin}"] = "ManageUsers yönetim işlemi (izin istisnası kaldırma).",
        ["/api/ui/v1/ayarlar/logo"] = "ManageUsers firma ayarı (logo kaldırma).",
        ["/api/ui/v1/yetki/ekranlar/{kod}"] = "ManageUsers ekran yetkisi override'ı.",
        ["/api/ui/v1/yetki/gruplar"] = "ManageUsers yetki grubu.",
        ["/api/ui/v1/kiralar/{id:guid}/ek-hizmetler/{kalemId:guid}"] = "Açık kira kalemi düzeltmesi; kira formunun parçası (kira kilidi + durum kuralları).",
        ["/api/ui/v1/kiralar/{id:guid}/paylasim"] = "Sözleşme paylaşım bağlantısını iptal (kayıt silmez; güvenlik eylemi).",
        ["/api/ui/v1/araclar/{id:guid}/fotograflar/{fotoId:guid}"] = "Araç kartı fotoğraf düzenlemesi (kayıt değil, ek).",
        ["/api/ui/v1/musteri-taksitleri/{id:guid}"] = "Finans kaydı FinanceWrite + kendi durum kuralları (ödenmiş taksit silinmez).",
        ["/api/ui/v1/maliyet-teklifleri/{id:guid}"] = "Finans grubu (FinanceWrite) teklif taslağı; deftere yazmaz.",
        // Web sitesi: blog yazısı, sayfa, SSS ve ilan SİLME çitin içinde (güvenlik takip b). Yalnız içerik bakımı muaf:
        ["/api/ui/v1/blog-yonetim/{id:guid}/kapak"] = "Web sitesi modülü: yazının kapak görselini kaldırma (yazı silinmez; içerik düzenlemesi).",
        ["/api/ui/v1/web-sitesi/ilanlar/{id:guid}/fotograflar/{aracId:guid}/{fotoId:guid}"] =
            "Web sitesi modülü: ilandan tek fotoğraf kaldırma (ilan ve araç kaydı silinmez; yayından kaldırma ayrı durum geçişi).",
    };

    /// <summary>Yıkıcı POST rota şablonu → gerekçe (silme izni aranmaz).</summary>
    private static readonly Dictionary<string, string> PostExempt = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    private List<(string Route, RouteEndpoint Endpoint)> UiEndpoints(string method)
        => fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .Select(e => (Route: "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), Endpoint: e))
            .Where(x => x.Route.StartsWith("/api/ui/v1/", StringComparison.OrdinalIgnoreCase)
                        && !x.Route.StartsWith("/api/ui/v1/platform", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool RequiresDeletePermission(RouteEndpoint endpoint)
    {
        var permissions = endpoint.Metadata.GetOrderedMetadata<IzinMetadata>().Select(m => m.Izin).ToHashSet();
        return permissions.Contains(Permission.OperationsDelete) || permissions.Contains(Permission.FinanceReverse);
    }

    /// <summary>Son rota parçası yıkıcı mı: <c>iptal</c>, <c>toplu-iptal</c> ya da <c>-sil</c> sonu.</summary>
    internal static bool IsDestructivePost(string route)
    {
        var last = route.TrimEnd('/').Split('/')[^1];
        return last.Equals("iptal", StringComparison.OrdinalIgnoreCase)
               || last.Equals("toplu-iptal", StringComparison.OrdinalIgnoreCase)
               || last.EndsWith("-sil", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Her_DELETE_ucu_silme_iznini_ister_ya_da_gerekceli_istisnadir()
    {
        var deletes = UiEndpoints("DELETE");
        Assert.True(deletes.Count > 30, $"uç tablosu beklenenden küçük: {deletes.Count}");

        var violations = deletes
            .Where(x => !Exempt.ContainsKey(x.Route) && !RequiresDeletePermission(x.Endpoint))
            .Select(x => x.Route).Distinct().Order().ToList();
        Assert.True(violations.Count == 0, "Silme izni (OperationsDelete/FinanceReverse) istemeyen DELETE uçları:\n" + string.Join("\n", violations));

        // İstisna listesi bayatlamasın: listedeki her rota gerçekten var ve gerçekten muafiyete ihtiyaç duyuyor.
        var routes = deletes.Select(x => x.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = Exempt.Keys.Where(k => !routes.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Bayat istisna: " + string.Join(", ", stale));
    }

    [Fact]
    public void Her_yikici_POST_ucu_silme_iznini_ister_ya_da_gerekceli_istisnadir()
    {
        var posts = UiEndpoints("POST").Where(x => IsDestructivePost(x.Route)).ToList();
        // Tarama gerçekten yüzeyi görüyor: bilinen yıkıcı uçlar listede (rezervasyon/kira/sipariş iptali, kanal sil).
        var seen = posts.Select(x => x.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var known in new[]
                 {
                     "/api/ui/v1/arac-siparisleri/{id:guid}/iptal", "/api/ui/v1/tarife-aktar/kanal-sil",
                     "/api/ui/v1/arac-kredileri/toplu-iptal",
                 })
            Assert.True(seen.Contains(known), $"tarama bilinen yıkıcı ucu görmedi: {known}\nGörülen:\n{string.Join("\n", seen.Order())}");

        var violations = posts
            .Where(x => !PostExempt.ContainsKey(x.Route) && !RequiresDeletePermission(x.Endpoint))
            .Select(x => x.Route).Distinct().Order().ToList();
        Assert.True(violations.Count == 0, "Silme izni (OperationsDelete/FinanceReverse) istemeyen yıkıcı POST uçları:\n" + string.Join("\n", violations));

        var stale = PostExempt.Keys.Where(k => !seen.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Bayat istisna: " + string.Join(", ", stale));
    }

    [Theory]
    [InlineData("/api/ui/v1/rezervasyonlar/{id:guid}/iptal", true)]
    [InlineData("/api/ui/v1/arac-kredileri/toplu-iptal", true)]
    [InlineData("/api/ui/v1/tarife-aktar/kanal-sil", true)]
    [InlineData("/api/ui/v1/kiralar/{id:guid}/iptal-edilenler", false)]
    [InlineData("/api/ui/v1/silinenler", false)]
    [InlineData("/api/ui/v1/araclar/{id:guid}/sil-onizleme", false)]
    public void Yikici_POST_siniflandirmasi(string route, bool destructive)
        => Assert.Equal(destructive, IsDestructivePost(route));
}
