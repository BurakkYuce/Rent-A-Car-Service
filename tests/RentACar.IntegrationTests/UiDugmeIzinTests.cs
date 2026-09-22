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

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    private static List<Dugme> Harita()
    {
        var metin = File.ReadAllText(Path.Combine(RepoKok(),
            "src/RentACar.Frontend/src/app/core/oturum/dugme-izinleri.ts"));
        var govde = metin[metin.IndexOf("export const DUGME_IZINLERI", StringComparison.Ordinal)..];
        var girisler = Regex.Matches(govde,
            @"(?<ad>\w+):\s*\{\s*izinler:\s*\[(?<izinler>[^\]]*)\],\s*uc:\s*'(?<yontem>[A-Z]+) (?<rota>[^']+)',?\s*\}");
        var sonuc = girisler.Select(m => new Dugme(
            m.Groups["ad"].Value,
            Regex.Matches(m.Groups["izinler"].Value, "'(?<i>\\w+)'").Select(i => Enum.Parse<Permission>(i.Groups["i"].Value)).ToList(),
            m.Groups["yontem"].Value,
            m.Groups["rota"].Value)).ToList();
        // Ayrıştırma sessizce giriş kaçırmasın: her "izinler:" bir girişe dönüşmüş olmalı.
        Assert.Equal(Regex.Matches(govde, @"\bizinler:").Count, sonuc.Count);
        return sonuc;
    }

    private RouteEndpoint Uc(Dugme d)
    {
        var adaylar = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(Normal(e.RoutePattern.RawText), Normal(d.Rota), StringComparison.OrdinalIgnoreCase))
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(d.Yontem) == true)
            .ToList();
        Assert.True(adaylar.Count == 1, $"{d.Ad}: '{d.Yontem} {d.Rota}' ucu {adaylar.Count} kez bulundu (1 beklenir).");
        return adaylar[0];
    }

    /// <summary>Grup öneki + boş alt rota ("") ham metinde sondaki "/" ile kalabilir: karşılaştırma için kırpılır.</summary>
    private static string Normal(string? rota) => "/" + (rota ?? "").Trim('/');

    [Fact]
    public void Harita_dolu_ve_kira_listesi_dugmelerini_kapsar()
    {
        var harita = Harita();
        Assert.True(harita.Count >= 8, $"Düğme haritası şüpheli: {harita.Count}");
        Assert.Contains(harita, d => d.Ad == "kiraIptal"
            && d.Izinler.Contains(Permission.OperationsDelete) && d.Izinler.Contains(Permission.OperationsWrite));

        // Kira listesi izni elle yazmaz: her izin kapısı DUGME_IZINLERI'nden gelir.
        var liste = File.ReadAllText(Path.Combine(RepoKok(),
            "src/RentACar.Frontend/src/app/features/kiralar/kira-listesi/kira-listesi.ts"));
        Assert.DoesNotContain("izinVar('", liste, StringComparison.Ordinal);
        Assert.Contains("DUGME_IZINLERI", liste, StringComparison.Ordinal);
    }

    [Fact]
    public void Her_dugme_ucunun_izin_kapisiyla_tum_kombinasyonlarda_ayni_karari_verir()
    {
        var izinler = Enum.GetValues<Permission>();
        Assert.Equal(6, izinler.Length); // 2^6 kombinasyon; enum büyürse test kendiliğinden genişler (üst sınır uyarısı)
        var hatalar = new List<string>();
        foreach (var d in Harita())
        {
            var uc = Uc(d);
            var hepsi = uc.Metadata.GetOrderedMetadata<IzinMetadata>().Select(m => m.Izin).Distinct().ToList();
            var biri = uc.Metadata.GetOrderedMetadata<IzinlerdenBiriMetadata>().Select(m => m.Izinler).ToList();
            if (hepsi.Count == 0 && biri.Count == 0)
            {
                hatalar.Add($"{d.Ad}: '{d.Yontem} {d.Rota}' izin kapısı taşımıyor — düğme izni anlamsız.");
                continue;
            }

            for (var maske = 0; maske < 1 << izinler.Length; maske++)
            {
                var kume = izinler.Where((_, i) => (maske & (1 << i)) != 0).ToHashSet();
                var gorunur = d.Izinler.All(kume.Contains);
                var izinVerir = hepsi.All(kume.Contains) && biri.All(b => b.Any(kume.Contains));
                if (gorunur == izinVerir) continue;
                hatalar.Add($"{d.Ad}: izinler {{{string.Join(",", kume)}}} → düğme {(gorunur ? "GÖRÜNÜR" : "gizli")}, " +
                            $"uç {(izinVerir ? "izin verir" : "403")} (uç kapısı: hepsi [{string.Join(",", hepsi)}], " +
                            $"biri [{string.Join(" | ", biri.Select(b => string.Join("/", b)))}])");
                break;
            }
        }
        Assert.True(hatalar.Count == 0, "Düğme görünürlüğü ile uç izni tutarsız:\n  " + string.Join("\n  ", hatalar));
    }
}
