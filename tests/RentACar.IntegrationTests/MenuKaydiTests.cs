using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using RentACar.Application.Authorization;
using RentACar.Web.Api.Menu;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.6 — menü kaydının (<see cref="MenuKaydi"/>) KAYMA ÇİTİ. Blazor F4.6'ya dek kendi menüsünü çizer; bu testler
/// kaydın MainLayout'la birebir kalmasını ve her öğenin izninin SAYFANIN KENDİ yetkisinden türemesini kilitler.
/// BAĞIMSIZ ORACLE: MainLayout.razor metni (bağlantılar, gruplar, sıra, rozetler) ve sayfa [Authorize] öznitelikleri —
/// kayıt kodundan değil.
/// </summary>
public sealed class MenuKaydiTests
{
    private const string KisaYollar = "Kısa Yollar";

    /// <summary>MainLayout grup kapısının (AuthorizeView Roles) izin karşılığı — elle yazılmış oracle.</summary>
    private static readonly Dictionary<string, Permission?> GrupKapisi = new()
    {
        [KisaYollar] = Permission.OperationsWrite,     // Roles="Admin,Yonetici,Operator"
        [""] = null,                                   // grupsuz: tüm roller
        ["Araçlar"] = Permission.OperationsWrite,
        ["Kira"] = Permission.OperationsWrite,
        ["Rezervasyon"] = Permission.OperationsWrite,
        ["Cariler & CRM"] = Permission.OperationsWrite,
        ["Web Sitesi"] = Permission.OperationsWrite,
        ["Servis & Sigorta"] = Permission.OperationsWrite,
        ["Fiyat & Tarife"] = Permission.OperationsWrite,
        ["Tanımlar"] = Permission.OperationsWrite,
        ["Finans"] = Permission.FinanceWrite,          // Roles="Admin,Yonetici,Muhasebe"
        ["Raporlar"] = Permission.ViewReports,         // Roles="Admin,Yonetici,Muhasebe"
        ["Sistem"] = Permission.ManageUsers,           // Roles="Admin"
    };

    private sealed record LayoutOgesi(string Grup, string Rota, string Etiket, string? Rozet);

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }

    private static string Temizle(string html)
    {
        var s = Regex.Replace(html, @"@if\s*\([^)]*\)\s*\{[^}]*\}", " ");
        s = Regex.Replace(s, "<[^>]+>", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>MainLayout'un kenar çubuğu menüsü (kısa yollar + nav) — sırasıyla (grup, rota, etiket, rozet).</summary>
    private static List<LayoutOgesi> LayoutMenusu()
    {
        var metin = File.ReadAllText(Path.Combine(RepoKok(), "src", "RentACar.Web", "Components", "Layout", "MainLayout.razor"));
        metin = Regex.Replace(metin, @"@\*.*?\*@", "", RegexOptions.Singleline); // Razor yorumları
        var bas = metin.IndexOf("<div class=\"sb-quick\">", StringComparison.Ordinal);
        var son = metin.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.True(bas > 0 && son > bas, "MainLayout menü bölgesi bulunamadı (sb-quick … </nav>).");
        var bolge = metin[bas..son];
        var navBas = bolge.IndexOf("<nav", StringComparison.Ordinal);

        var sonuc = new List<LayoutOgesi>();
        var grup = KisaYollar;
        var belirtec = new Regex(@"<summary>(?<grup>.*?)</summary>|</details>|<a\b(?<attr>[^>]*)>(?<ic>.*?)</a>", RegexOptions.Singleline);
        foreach (Match m in belirtec.Matches(bolge))
        {
            if (grup == KisaYollar && m.Index > navBas) grup = "";
            if (m.Groups["grup"].Success) grup = Temizle(m.Groups["grup"].Value);
            else if (m.Value == "</details>") grup = "";
            else
            {
                var href = Regex.Match(m.Groups["attr"].Value, "href=\"([^\"]+)\"").Groups[1].Value;
                var ic = m.Groups["ic"].Value;
                var rozet = ic.Contains("_yeniTalep", StringComparison.Ordinal) ? MenuKaydi.RozetYeniTalep
                    : ic.Contains("_okunmamis", StringComparison.Ordinal) ? MenuKaydi.RozetOkunmamisBildirim : null;
                sonuc.Add(new LayoutOgesi(grup, href, Temizle(ic), rozet));
            }
        }
        return sonuc;
    }

    [Fact]
    public void Kayit_MainLayout_ile_birebir_ayni_sirada()
    {
        var layout = LayoutMenusu();
        var kayit = MenuKaydi.Ogeler.OrderBy(o => o.Sira)
            .Select(o => new LayoutOgesi(o.Grup, o.Rota, o.Etiket, o.RozetKodu)).ToList();

        Assert.True(layout.Count > 100, $"MainLayout ayrıştırması şüpheli: {layout.Count} bağlantı.");
        var eksik = layout.Except(kayit).Select(x => $"{x.Grup}|{x.Rota}|{x.Etiket}|{x.Rozet}").ToList();
        var fazla = kayit.Except(layout).Select(x => $"{x.Grup}|{x.Rota}|{x.Etiket}|{x.Rozet}").ToList();
        Assert.True(eksik.Count == 0 && fazla.Count == 0,
            $"MenuKaydi MainLayout'tan kaymış.\nKayıtta eksik: {string.Join(", ", eksik)}\nKayıtta fazla: {string.Join(", ", fazla)}");
        Assert.Equal(layout, kayit); // sıra dahil
    }

    [Fact]
    public void Her_bag_tam_bir_kez_grup_rota_benzersiz_sira_benzersiz()
    {
        var ogeler = MenuKaydi.Ogeler;
        Assert.Equal(ogeler.Count, ogeler.Select(o => (o.Grup, o.Rota)).Distinct().Count());
        Assert.Equal(ogeler.Count, ogeler.Select(o => o.Sira).Distinct().Count());
        Assert.All(ogeler, o => Assert.Equal(MenuKaydi.Blazor, o.Sahip));
        Assert.All(ogeler, o => Assert.Equal(o.Grup == KisaYollar, o.HizliBaglanti));
        // Modül bayrağı yalnız MainLayout'un @if (_webSitesiModulu) bloğundaki "Web Sitesi" grubunda.
        Assert.All(ogeler, o => Assert.Equal(o.Grup == "Web Sitesi" ? "WebSitesi" : null, o.Modul));
    }

    /// <summary>Rota → Blazor sayfasının [Authorize] özniteliği (Web derlemesindeki bileşenlerden).</summary>
    private static Dictionary<string, AuthorizeAttribute?> SayfaYetkileri()
    {
        var d = new Dictionary<string, AuthorizeAttribute?>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in typeof(MenuKaydi).Assembly.GetTypes().Where(t => typeof(IComponent).IsAssignableFrom(t)))
        {
            var auth = t.GetCustomAttributes<AuthorizeAttribute>(inherit: true).FirstOrDefault();
            foreach (var r in t.GetCustomAttributes<RouteAttribute>())
                d[r.Template] = auth;
        }
        return d;
    }

    [Fact]
    public void Her_ogenin_izni_sayfanin_yetkisinden_turetilmis()
    {
        var sayfalar = SayfaYetkileri();
        var hatalar = new List<string>();
        foreach (var o in MenuKaydi.Ogeler)
        {
            if (!sayfalar.TryGetValue(o.Rota, out var auth)) { hatalar.Add($"{o.Rota}: sayfa yok"); continue; }
            Permission? beklenen;
            if (auth?.Policy is { } pol && pol.StartsWith("izin:", StringComparison.Ordinal))
                beklenen = Enum.Parse<Permission>(pol["izin:".Length..]);
            else if (auth?.Roles is "Admin")
                beklenen = Permission.ManageUsers;
            else
                beklenen = GrupKapisi[o.Grup];
            if (beklenen != o.Izin) hatalar.Add($"{o.Grup}|{o.Rota}: beklenen {beklenen?.ToString() ?? "(herkes)"}, kayıtta {o.Izin?.ToString() ?? "(herkes)"}");
        }
        Assert.True(hatalar.Count == 0, "Menü izni sayfa yetkisiyle uyuşmuyor:\n" + string.Join("\n", hatalar));
    }
}
