using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using RentACar.Application.Authorization;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Menu;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.6 — menü kaydının (<see cref="MenuKaydi"/>) KAYMA ÇİTİ. F4.6'dan beri Blazor <c>MainLayout</c> da menüyü kayıttan
/// çizer; bu testler kaydın F4.6 ÖNCESİ MainLayout menüsüyle (dondurulmuş kopya: <c>Oracle/menu-f46-oncesi.tsv</c>)
/// birebir kalmasını, her öğenin izninin SAYFANIN KENDİ yetkisinden türemesini ve rol → izin geçişinin KASITLI
/// farkını (önce/sonra) kilitler.
/// BAĞIMSIZ ORACLE: dondurulmuş MainLayout menüsü (bağlantılar, gruplar, sıra, rozetler), sayfa [Authorize]
/// öznitelikleri ve elle yazılmış rol farkı tablosu — kayıt kodundan değil.
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

    /// <summary>
    /// F4.6 öncesi MainLayout menüsü (kısa yollar + nav) — sırasıyla (grup, rota, etiket, rozet). MainLayout artık
    /// menüyü kayıttan çizdiği için oracle onun o günkü DONDURULMUŞ kopyasıdır (<see cref="EskiLayoutMenusu"/> bu
    /// dosyayı üretmek için kullanılan ayrıştırıcıdır; bkz. <c>Oracle/menu-f46-oncesi.tsv</c> başlığı).
    /// </summary>
    private static List<LayoutOgesi> LayoutMenusu()
        => File.ReadAllLines(Path.Combine(RepoKok(), "tests", "RentACar.IntegrationTests", "Oracle", "menu-f46-oncesi.tsv"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Select(p => new LayoutOgesi(p[0], p[1], p[2], p[3].Length == 0 ? null : p[3]))
            .ToList();

    /// <summary>Kaydın, F4.6 öncesi menüyle karşılaştırılabilir hâli: <c>spa</c> öğesinin rotası Blazor karşılığına çevrilir.</summary>
    private static string EskiRota(MenuOgesi o)
        => o.Sahip == MenuKaydi.Spa ? RentACar.Web.Spa.IlkKesis.BlazorKarsiligi(o.Rota) ?? o.Rota : o.Rota;

    /// <summary>F4.6 ÖNCESİ MainLayout'u ayrıştıran kod (oracle dosyası bununla üretildi; bugün MainLayout'ta sabit bağlantı yok).</summary>
    private static List<LayoutOgesi> EskiLayoutMenusu(string metin)
    {
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
    public void Kayit_F46_oncesi_MainLayout_menusuyle_birebir_ayni_sirada()
    {
        var layout = LayoutMenusu();
        var kayit = MenuKaydi.Ogeler.OrderBy(o => o.Sira)
            .Select(o => new LayoutOgesi(o.Grup, EskiRota(o), o.Etiket, o.RozetKodu)).ToList();

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
        // F4.6: F4 sayfaları (Panel, Kiralar, Yeni Kira) yeni arayüzün; sahip rotadan türer (/app → spa).
        // F5.4: Rezervasyon grubu (4), Kira grubundan Teklifler + Filo Kiralama, iki kısa yol (Yeni Rezervasyon, Müsaitlik).
        // F6.4: Araçlar grubunun tamamı (12) + Tanımlar'daki Araç Tipleri ve Segmentler (F6 sayfaları; iki grupta birer kez).
        // F7.3: Cariler & CRM grubunun F7 sayfaları (6; Blog ve Gelen Talepler Blazor'da kalır).
        // F10.3: Raporlar grubunun tamamı (25; araç karnesi kimlikli olduğu için menüde yok).
        // F9.3: Servis & Sigorta (3) + Fiyat & Tarife (11) gruplarının tamamı + grupsuz Vade Panosu (15).
        // F8.3: Finans grubunun tamamı (17; cari ekstresi ve fatura yazdır kimlikli olduğu için menüde yok).
        Assert.Equal(new[]
            {
                "/app/anketler", "/app/arac-durum", "/app/arac-kredi", "/app/arac-sahipleri", "/app/arac-siparis", "/app/arac-tipleri",
                "/app/arac-tipleri", "/app/araclar", "/app/araclar/detayli", "/app/assistans", "/app/baf", "/app/broker-yasaklari",
                "/app/cari-virman", "/app/cariler", "/app/cezalar", "/app/crm", "/app/depozito", "/app/donem-kapanis",
                "/app/ek-hizmetler", "/app/faturalar", "/app/faturalar/detay-listesi", "/app/filo-kiralama", "/app/filo-plan",
                "/app/finans/bakiye-duzeltme", "/app/finans/nakit-islem", "/app/fiyat-hesapla", "/app/gelen-efatura",
                "/app/giderler", "/app/hasar", "/app/hukuk", "/app/kasa", "/app/kira-kurallari", "/app/kiralar",
                "/app/kiralar/yeni", "/app/kurlar", "/app/maliyet-hesapla", "/app/maliyet-teklifleri", "/app/musaitlik",
                "/app/musaitlik", "/app/musteri-taksit", "/app/otomatik-tahsilat", "/app/panel",
                "/app/raporlar/arac-durum-takip", "/app/raporlar/arac-gunluk-durum", "/app/raporlar/cari-bakiye",
                "/app/raporlar/doluluk", "/app/raporlar/ek-hizmet", "/app/raporlar/extre-ozeti", "/app/raporlar/fatura-donem",
                "/app/raporlar/filo", "/app/raporlar/filo-analiz", "/app/raporlar/finans-analiz", "/app/raporlar/gelir-gider",
                "/app/raporlar/gunluk", "/app/raporlar/karlilik", "/app/raporlar/karsilastirmali-analiz",
                "/app/raporlar/kasa-banka", "/app/raporlar/kdv-listesi", "/app/raporlar/km-detay",
                "/app/raporlar/otomatik-servisler", "/app/raporlar/periyodik-servis", "/app/raporlar/personel-calisma",
                "/app/raporlar/rezervasyon-kaynak", "/app/raporlar/servis-ozet", "/app/raporlar/sigorta-muayene",
                "/app/raporlar/tahsilat-fatura", "/app/raporlar/virman-gecmisi", "/app/regulasyon",
                "/app/rez-sartlari", "/app/rezervasyonlar", "/app/rezervasyonlar", "/app/satislar", "/app/segmentler",
                "/app/segmentler", "/app/servis-tanimlari", "/app/servisler", "/app/sigorta-urunleri", "/app/sikayetler",
                "/app/takvim", "/app/tarife-aktar", "/app/tarife-gruplari", "/app/tarife-matris", "/app/tarifeler",
                "/app/tek-cari-toplu", "/app/teklifler", "/app/toplu-gider", "/app/toplu-tahsilat", "/app/vade",
            },
            ogeler.Where(o => o.Sahip == MenuKaydi.Spa).Select(o => o.Rota).OrderBy(r => r, StringComparer.Ordinal));
        Assert.All(ogeler, o => Assert.Equal(o.Rota.StartsWith("/app/", StringComparison.Ordinal) ? MenuKaydi.Spa : MenuKaydi.Blazor, o.Sahip));
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
            // spa öğesi: Blazor karşılığı yaşadıkça (F4.6b'ye dek) onun yetkisi; sayfa silinince grup kapısı.
            if (!sayfalar.TryGetValue(EskiRota(o), out var auth))
            {
                if (o.Sahip == MenuKaydi.Spa) auth = null;
                else { hatalar.Add($"{o.Rota}: sayfa yok"); continue; }
            }
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

    [Fact]
    public void MainLayout_menuyu_kayittan_cizer_sabit_menu_baglantisi_yok()
    {
        var metin = File.ReadAllText(Path.Combine(RepoKok(), "src", "RentACar.Web", "Components", "Layout", "MainLayout.razor"));
        Assert.Contains("MenuApi.Gorunur(", metin, StringComparison.Ordinal);   // yeni arayüzle AYNI süzgeç (etkin izin + modül)
        Assert.Contains("MenuGorunumu.Kur(", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("<AuthorizeView Roles=", metin, StringComparison.Ordinal); // rol kapısı kalmadı
        var sabit = MenuKaydi.Ogeler.Select(o => o.Rota).Concat(MenuKaydi.Ogeler.Select(EskiRota)).Distinct()
            .Where(r => r != "/" && metin.Contains($"href=\"{r}\"", StringComparison.Ordinal)).ToList();
        Assert.True(sabit.Count == 0, "MainLayout'ta elle yazılmış menü bağlantısı: " + string.Join(", ", sabit));
        Assert.Empty(EskiLayoutMenusu(metin).Where(x => x.Rota.StartsWith('/'))); // menü bölgesinde sabit (literal) href kalmadı
    }

    // ---- F4.6 rol → izin geçişi (KASITLI davranış değişikliği; PR'daki önce/sonra tablosunun kilidi)

    private static readonly string[] OperasyonGruplari =
        [KisaYollar, "Araçlar", "Kira", "Rezervasyon", "Cariler & CRM", "Web Sitesi", "Servis & Sigorta", "Fiyat & Tarife", "Tanımlar"];

    /// <summary>F4.6 ÖNCESİ MainLayout görünürlüğü (AuthorizeView Roles) — elle yazılmış oracle.</summary>
    private static bool EskidenGorunur(LayoutOgesi o, UserRole rol)
    {
        if (o.Rota == "/tarife-aktar") return rol == UserRole.Admin;                         // iç içe Roles="Admin"
        if (OperasyonGruplari.Contains(o.Grup)) return rol is UserRole.Admin or UserRole.Yonetici or UserRole.Operator;
        if (o.Grup is "Finans" or "Raporlar") return rol is UserRole.Admin or UserRole.Yonetici or UserRole.Muhasebe;
        if (o.Grup == "Sistem") return rol == UserRole.Admin;
        return true;                                                                            // grupsuz: tüm roller
    }

    private static System.Security.Claims.ClaimsPrincipal Kullanici(UserRole rol, string[]? ek = null, string[]? yasak = null)
    {
        var claims = new List<System.Security.Claims.Claim> { new(System.Security.Claims.ClaimTypes.Role, rol.ToString()) };
        claims.AddRange((ek ?? []).Select(i => new System.Security.Claims.Claim(RentACar.Web.Identity.IdentityClaims.IzinEk, i)));
        claims.AddRange((yasak ?? []).Select(i => new System.Security.Claims.Claim(RentACar.Web.Identity.IdentityClaims.IzinYasak, i)));
        return new(new System.Security.Claims.ClaimsIdentity(claims, "test"));
    }

    private static HashSet<string> YeniGorunur(System.Security.Claims.ClaimsPrincipal u)
        => MenuApi.Gorunur(u, webSitesiModulu: true).Select(o => $"{o.Grup}|{EskiRota(o)}").ToHashSet();

    [Theory]
    [InlineData(UserRole.Admin, "", "")]
    [InlineData(UserRole.Yonetici, "", "")]
    [InlineData(UserRole.Operator,
        "", "Araçlar|/vehicles/detayli;Araçlar|/musteri-taksit;Cariler & CRM|/crm;Fiyat & Tarife|/maliyet-hesapla;Fiyat & Tarife|/maliyet-teklifleri")]
    [InlineData(UserRole.Muhasebe,
        "Araçlar|/vehicles/detayli;Araçlar|/musteri-taksit;Cariler & CRM|/crm;Fiyat & Tarife|/maliyet-hesapla;Fiyat & Tarife|/maliyet-teklifleri", "")]
    public void Rol_bazinda_menu_farki_F46_oncesine_gore(UserRole rol, string kazanir, string kaybeder)
    {
        var eski = LayoutMenusu().Where(o => EskidenGorunur(o, rol)).Select(o => $"{o.Grup}|{o.Rota}").ToHashSet();
        var yeni = YeniGorunur(Kullanici(rol));
        static string[] Liste(string s) => s.Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Liste(kazanir).OrderBy(x => x), yeni.Except(eski).OrderBy(x => x));
        Assert.Equal(Liste(kaybeder).OrderBy(x => x), eski.Except(yeni).OrderBy(x => x));
    }

    [Fact]
    public void Kullanici_bazli_istisna_menuye_yansir()
    {
        // Operatöre EK FinanceWrite: Finans grubu (+ FinanceWrite'lı operasyon öğeleri) görünür olur.
        var ek = YeniGorunur(Kullanici(UserRole.Operator, ek: ["FinanceWrite"]));
        Assert.Contains("Finans|/kasa", ek);
        Assert.Contains("Araçlar|/musteri-taksit", ek);
        Assert.DoesNotContain("Raporlar|/raporlar/gelir-gider", ek);

        // Operatöre YASAK OperationsWrite: operasyon grupları ve kısa yollar (Yeni Kira dahil) gizlenir.
        var yasak = YeniGorunur(Kullanici(UserRole.Operator, yasak: ["OperationsWrite"]));
        Assert.DoesNotContain(yasak, x => x.StartsWith("Araçlar|", StringComparison.Ordinal) || x.StartsWith(KisaYollar + "|", StringComparison.Ordinal));
        Assert.DoesNotContain("Kira|/kiralar", yasak);
        Assert.DoesNotContain("Tanımlar|/segmentler", yasak);  // F6.4: spa öğesi de izinle gizlenir
        Assert.DoesNotContain("Cariler & CRM|/cariler", yasak); // F7.3: aynı kural
        // F7.3: CRM Analiz spa öğesi ViewReports'a bağlı — operatöre ek ViewReports ile görünür, Muhasebe'ye yasakla gizlenir.
        Assert.Contains("Cariler & CRM|/crm", YeniGorunur(Kullanici(UserRole.Operator, ek: ["ViewReports"])));
        Assert.DoesNotContain("Cariler & CRM|/crm", YeniGorunur(Kullanici(UserRole.Muhasebe, yasak: ["ViewReports"])));
        // F10.3: Raporlar spa öğeleri de izne bağlı — ek ViewReports ile görünür, yasakla gizlenir.
        Assert.Contains("Raporlar|/raporlar/gunluk", YeniGorunur(Kullanici(UserRole.Operator, ek: ["ViewReports"])));
        Assert.DoesNotContain(YeniGorunur(Kullanici(UserRole.Muhasebe, yasak: ["ViewReports"])),
            x => x.StartsWith("Raporlar|", StringComparison.Ordinal));
        // F8.3: Finans spa öğeleri FinanceWrite'a bağlı — operatöre ek FinanceWrite ile görünür (yukarıda), Muhasebe'ye
        // yasakla gizlenir.
        Assert.Contains("Finans|/faturalar", ek);
        Assert.DoesNotContain(YeniGorunur(Kullanici(UserRole.Muhasebe, yasak: ["FinanceWrite"])),
            x => x.StartsWith("Finans|", StringComparison.Ordinal));
        // F9.3: Servis & Sigorta ve Fiyat & Tarife spa öğeleri de izne bağlı; maliyet ekranları FinanceWrite ister.
        Assert.DoesNotContain(yasak, x => x.StartsWith("Servis & Sigorta|", StringComparison.Ordinal));
        Assert.DoesNotContain("Fiyat & Tarife|/tarifeler", yasak);
        Assert.Contains("Fiyat & Tarife|/maliyet-hesapla", ek);
        Assert.DoesNotContain("Fiyat & Tarife|/tarife-aktar", ek);   // ManageUsers
        Assert.Contains("|/", yasak);                // Panel ve grupsuz öğeler kalır
        Assert.Contains("|/vade", yasak);            // F9.3: Vade Panosu spa öğesi grupsuz, izinsiz kalır
    }
}
