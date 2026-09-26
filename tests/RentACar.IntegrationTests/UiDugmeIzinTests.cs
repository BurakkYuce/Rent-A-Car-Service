using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.6 — <c>UcIzinKapsamaTests</c>'in (Blazor: "dar izin isteyen ucun düğmesi o izinle kapılı") yeni arayüz
/// karşılığı. SPA düğmeleri görünürlüğü <c>ben.izinler</c>'den, izin adlarını TEK dosyadan alır:
/// <c>src/RentACar.Frontend/src/app/core/oturum/dugme-izinleri.ts</c> (<c>DUGME_IZINLERI</c>). Bu test her girişin
/// ucunu GERÇEK uç tablosunda (EndpointDataSource) bulur ve 6 iznin 64 kombinasyonunun HER BİRİNDE
/// "düğme görünür ⇔ uç izin verir" eşitliğini doğrular (kullanıcı-bazlı ek/yasak istisnaları dahil — rol
/// matrisine bakılmaz). Kaynak: <c>IzinMetadata</c> (hepsi VE) + <c>IzinlerdenBiriMetadata</c> (biri yeter).
/// <para><b>Neden (canlı hata, 2026-08-26):</b> <c>/kiralar/cancel</c> OperationsDelete isterken düğmesi
/// OperationsWrite'a bakıyordu; operatör düğmeyi görüp 403 alıyordu. SPA'da ters yön de yakalanır: kira
/// iptal düğmesi yalnız OperationsDelete'e bakıyordu — grup izni (OperationsWrite) yasaklı kullanıcıda düğme
/// görünür, uç 403 verirdi (F4.6'da <c>kiraIptal</c> = OperationsWrite + OperationsDelete).</para>
/// </summary>
[Collection("web")]
public sealed class UiDugmeIzinTests(WebFixture fx)
{
    private sealed record Dugme(string Ad, IReadOnlyList<Permission> Izinler, string Yontem, string Rota);

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    private static List<Dugme> Map()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(),
            "src/RentACar.Frontend/src/app/core/oturum/dugme-izinleri.ts"));
        var body = text[text.IndexOf("export const DUGME_IZINLERI", StringComparison.Ordinal)..];
        var entries = Regex.Matches(body,
            @"(?<ad>\w+):\s*\{\s*izinler:\s*\[(?<izinler>[^\]]*)\],\s*uc:\s*'(?<yontem>[A-Z]+) (?<rota>[^']+)',?\s*\}");
        var result = entries.Select(m => new Dugme(
            m.Groups["ad"].Value,
            Regex.Matches(m.Groups["izinler"].Value, "'(?<i>\\w+)'").Select(i => Enum.Parse<Permission>(i.Groups["i"].Value)).ToList(),
            m.Groups["yontem"].Value,
            m.Groups["rota"].Value)).ToList();
        // Ayrıştırma sessizce giriş kaçırmasın: her "izinler:" bir girişe dönüşmüş olmalı.
        Assert.Equal(Regex.Matches(body, @"\bizinler:").Count, result.Count);
        return result;
    }

    private RouteEndpoint Endpoint(Dugme d)
    {
        var candidates = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(Normal(e.RoutePattern.RawText), Normal(d.Rota), StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(d.Yontem) == true)
            .ToList();
        Assert.True(candidates.Count == 1, $"{d.Ad}: '{d.Yontem} {d.Rota}' ucu {candidates.Count} kez bulundu (1 beklenir).");
        return candidates[0];
    }

    /// <summary>Grup öneki + boş alt rota ("") ham metinde sondaki "/" ile kalabilir: karşılaştırma için kırpılır.</summary>
    private static string Normal(string? route) => "/" + (route ?? "").Trim('/');

    [Fact]
    public void Harita_dolu_ve_kira_listesi_dugmelerini_kapsar()
    {
        var map = Map();
        Assert.True(map.Count >= 8, $"Düğme haritası şüpheli: {map.Count}");
        Assert.Contains(map, d => d.Ad == "kiraIptal"
            && d.Izinler.Contains(Permission.OperationsDelete) && d.Izinler.Contains(Permission.OperationsWrite));

        // Kira listesi izni elle yazmaz: her izin kapısı DUGME_IZINLERI'nden gelir.
        var list = File.ReadAllText(Path.Combine(RepoRoot(),
            "src/RentACar.Frontend/src/app/features/kiralar/kira-listesi/kira-listesi.ts"));
        Assert.DoesNotContain("izinVar('", list, StringComparison.Ordinal);
        Assert.Contains("DUGME_IZINLERI", list, StringComparison.Ordinal);
    }

    [Fact]
    public void Her_dugme_ucunun_izin_kapisiyla_tum_kombinasyonlarda_ayni_karari_verir()
    {
        var permissions = Enum.GetValues<Permission>();
        Assert.Equal(6, permissions.Length); // 2^6 kombinasyon; enum büyürse test kendiliğinden genişler (üst sınır uyarısı)
        var errors = new List<string>();
        foreach (var d in Map())
        {
            var endpoint = Endpoint(d);
            var all = endpoint.Metadata.GetOrderedMetadata<IzinMetadata>().Select(m => m.Izin).Distinct().ToList();
            var one = endpoint.Metadata.GetOrderedMetadata<IzinlerdenBiriMetadata>().Select(m => m.Izinler).ToList();
            if (all.Count == 0 && one.Count == 0)
            {
                errors.Add($"{d.Ad}: '{d.Yontem} {d.Rota}' izin kapısı taşımıyor — düğme izni anlamsız.");
                continue;
            }

            for (var mask = 0; mask < 1 << permissions.Length; mask++)
            {
                var set = permissions.Where((_, i) => (mask & (1 << i)) != 0).ToHashSet();
                var visible = d.Izinler.All(set.Contains);
                var allows = all.All(set.Contains) && one.All(b => b.Any(set.Contains));
                if (visible == allows) continue;
                errors.Add($"{d.Ad}: izinler {{{string.Join(",", set)}}} → düğme {(visible ? "GÖRÜNÜR" : "gizli")}, " +
                            $"uç {(allows ? "izin verir" : "403")} (uç kapısı: hepsi [{string.Join(",", all)}], " +
                            $"biri [{string.Join(" | ", one.Select(b => string.Join("/", b)))}])");
                break;
            }
        }
        Assert.True(errors.Count == 0, "Düğme görünürlüğü ile uç izni tutarsız:\n  " + string.Join("\n  ", errors));
    }
}
