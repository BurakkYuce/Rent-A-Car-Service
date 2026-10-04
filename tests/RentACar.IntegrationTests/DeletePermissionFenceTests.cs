using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul C-CRMSIL / D-4 + güvenlik incelemesi F3 — YAPISAL ÇİT: <c>/api/ui/v1</c> altındaki her DELETE ucu (gerçek uç
/// tablosu, <see cref="EndpointDataSource"/>) rol matrisinin silme iznini ister: <see cref="Permission.OperationsDelete"/>
/// (operatör siler DEĞİL) ya da finans kaydında <see cref="Permission.FinanceReverse"/>. Bilinçli istisnalar aşağıda
/// GEREKÇESİYLE listelidir; yeni bir DELETE ucu ya izni taşır ya da buraya gerekçeyle eklenir (sessiz geçiş yok).
/// Elle sayım değil tablo taraması: kabul koşusu elle bulunanın dışında 15+ ucu yakaladı.
/// </summary>
[Collection("web")]
public sealed class DeletePermissionFenceTests(WebFixture fx)
{
    /// <summary>Rota şablonu → gerekçe. Silme izni ARANMAZ (izin kapısı yine zorunlu — UiApiYapisalTests).</summary>
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
        ["/api/ui/v1/blog-yonetim/{id:guid}"] = "Web sitesi modülü içerik yönetimi (modül kapılı; iş kaydı değil).",
        ["/api/ui/v1/blog-yonetim/{id:guid}/kapak"] = "Web sitesi modülü: kapak görselini kaldırma (içerik düzenlemesi).",
        ["/api/ui/v1/site-icerik/sayfalar/{id:guid}"] = "Web sitesi modülü içerik yönetimi.",
        ["/api/ui/v1/site-icerik/sss/{id:guid}"] = "Web sitesi modülü içerik yönetimi.",
        ["/api/ui/v1/web-sitesi/ilanlar/{id:guid}"] = "Web sitesi modülü: ilanı yayından kaldırma (araç kaydı silinmez).",
        ["/api/ui/v1/web-sitesi/ilanlar/{id:guid}/fotograflar/{aracId:guid}/{fotoId:guid}"] = "Web sitesi modülü: ilan fotoğrafı.",
    };

    private static readonly string[] WebsitePrefixes = [];

    [Fact]
    public void Her_DELETE_ucu_silme_iznini_ister_ya_da_gerekceli_istisnadir()
    {
        var deletes = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("DELETE") == true)
            .Select(e => (Route: "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'), Endpoint: e))
            .Where(x => x.Route.StartsWith("/api/ui/v1/", StringComparison.OrdinalIgnoreCase)
                        && !x.Route.StartsWith("/api/ui/v1/platform", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(deletes.Count > 30, $"uç tablosu beklenenden küçük: {deletes.Count}");

        var violations = deletes
            .Where(x => !Exempt.ContainsKey(x.Route) && !WebsitePrefixes.Any(p => x.Route.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .Where(x =>
            {
                var permissions = x.Endpoint.Metadata.GetOrderedMetadata<IzinMetadata>().Select(m => m.Izin).ToHashSet();
                return !permissions.Contains(Permission.OperationsDelete) && !permissions.Contains(Permission.FinanceReverse);
            })
            .Select(x => x.Route).Distinct().Order().ToList();
        Assert.True(violations.Count == 0, "Silme izni (OperationsDelete/FinanceReverse) istemeyen DELETE uçları:\n" + string.Join("\n", violations));

        // İstisna listesi bayatlamasın: listedeki her rota gerçekten var.
        var routes = deletes.Select(x => x.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = Exempt.Keys.Where(k => !routes.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Bayat istisna: " + string.Join(", ", stale));
    }
}
