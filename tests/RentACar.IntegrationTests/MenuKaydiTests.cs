using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using RentACar.Application.Authorization;
using RentACar.Domain.Enums;
using RentACar.Web.Api.Menu;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.6 — menü kaydının (<see cref="MenuRegistry"/>) KAYMA ÇİTİ. F4.6'dan beri Blazor <c>MainLayout</c> da menüyü kayıttan
/// çizer; bu testler kaydın F4.6 ÖNCESİ MainLayout menüsüyle (dondurulmuş kopya: <c>Oracle/menu-f46-oncesi.tsv</c>)
/// birebir kalmasını, her öğenin izninin SAYFANIN KENDİ yetkisinden türemesini ve rol → izin geçişinin KASITLI
/// farkını (önce/sonra) kilitler.
/// BAĞIMSIZ ORACLE: dondurulmuş MainLayout menüsü (bağlantılar, gruplar, sıra, rozetler), sayfa [Authorize]
/// öznitelikleri ve elle yazılmış rol farkı tablosu — kayıt kodundan değil.
/// </summary>
public sealed class MenuKaydiTests
{
    private const string Shortcuts = "Kısa Yollar";

    /// <summary>MainLayout grup kapısının (AuthorizeView Roles) izin karşılığı — elle yazılmış oracle.</summary>
    private static readonly Dictionary<string, Permission?> GroupGate = new()
    {
        [Shortcuts] = Permission.OperationsWrite,     // Roles="Admin,Yonetici,Operator"
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

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repo kökü bulunamadı.");
    }


    /// <summary>
    /// F4.6 öncesi MainLayout menüsü (kısa yollar + nav) — sırasıyla (grup, rota, etiket, rozet). MainLayout artık
    /// menüyü kayıttan çizdiği için oracle onun o günkü DONDURULMUŞ kopyasıdır (<see cref="OldLayoutMenu"/> bu
    /// dosyayı üretmek için kullanılan ayrıştırıcıdır; bkz. <c>Oracle/menu-f46-oncesi.tsv</c> başlığı).
    /// </summary>
    private static List<LayoutOgesi> LayoutMenu()
        => File.ReadAllLines(Path.Combine(RepoRoot(), "tests", "RentACar.IntegrationTests", "Oracle", "menu-f46-oncesi.tsv"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('\t'))
            .Select(p => new LayoutOgesi(p[0], p[1], p[2], p[3].Length == 0 ? null : p[3]))
            .ToList();

    /// <summary>Kaydın, F4.6 öncesi menüyle karşılaştırılabilir hâli: <c>spa</c> öğesinin rotası Blazor karşılığına çevrilir.</summary>
    private static string OldRoute(MenuOgesi o)
        => o.Sahip != MenuRegistry.Spa ? o.Rota
            : SpaOnlyRoutes.TryGetValue(o.Rota, out var old) ? old
            : RentACar.Web.Spa.Cutover.BlazorEquivalent(o.Rota) ?? o.Rota;

    /// <summary>
    /// SPA'da ayrı rotası olan ama Blazor'da başka sayfanın İÇİNDE çizilen ekranlar (haritada yok — Blazor'da @page'i
    /// yoktu). Kabul testi: "Yeni Rezervasyon" kısa yolu boş formu açar; Blazor'da form rezervasyon listesindeydi.
    /// </summary>
    private static readonly Dictionary<string, string> SpaOnlyRoutes = new(StringComparer.Ordinal)
    {
        ["/app/rezervasyonlar/yeni"] = "/rezervasyonlar",
    };

    [Fact]
    public void Kayit_F46_oncesi_MainLayout_menusuyle_birebir_ayni_sirada()
    {
        var layout = LayoutMenu();
        var record = MenuRegistry.Items.OrderBy(o => o.Sira)
            .Select(o => new LayoutOgesi(o.Grup, OldRoute(o), o.Etiket, o.RozetKodu)).ToList();

        Assert.True(layout.Count > 100, $"MainLayout ayrıştırması şüpheli: {layout.Count} bağlantı.");
        var missing = layout.Except(record).Select(x => $"{x.Grup}|{x.Rota}|{x.Etiket}|{x.Rozet}").ToList();
        var excess = record.Except(layout).Select(x => $"{x.Grup}|{x.Rota}|{x.Etiket}|{x.Rozet}").ToList();
        Assert.True(missing.Count == 0 && excess.Count == 0,
            $"MenuKaydi MainLayout'tan kaymış.\nKayıtta eksik: {string.Join(", ", missing)}\nKayıtta fazla: {string.Join(", ", excess)}");
        Assert.Equal(layout, record); // sıra dahil
    }

    [Fact]
    public void Her_bag_tam_bir_kez_grup_rota_benzersiz_sira_benzersiz()
    {
        var items = MenuRegistry.Items;
        Assert.Equal(items.Count, items.Select(o => (o.Grup, o.Rota)).Distinct().Count());
        Assert.Equal(items.Count, items.Select(o => o.Sira).Distinct().Count());
        // F4.6: F4 sayfaları (Panel, Kiralar, Yeni Kira) yeni arayüzün; sahip rotadan türer (/app → spa).
        // F5.4: Rezervasyon grubu (4), Kira grubundan Teklifler + Filo Kiralama, iki kısa yol (Yeni Rezervasyon, Müsaitlik).
        // F6.4: Araçlar grubunun tamamı (12) + Tanımlar'daki Araç Tipleri ve Segmentler (F6 sayfaları; iki grupta birer kez).
        // F7.3: Cariler & CRM grubunun F7 sayfaları (6; Blog ve Gelen Talepler Blazor'da kalır).
        // F10.3: Raporlar grubunun tamamı (25; araç karnesi kimlikli olduğu için menüde yok).
        // F11.3: Tanımlar (24), Web Sitesi (4), Sistem (9), Cariler & CRM'deki Blog ve Gelen Talepler (2), grupsuz
        // Bildirimler/Takvim Aboneliği/Firma Belgeleri/Dokümanlar (4) — 43 öğe (Blog ve Gelen Talepler iki grupta).
        string[] f11 =
        [
            "/app/aksesuarlar", "/app/arac-gruplari", "/app/ayarlar", "/app/bankalar", "/app/belge-sablonlari",
            "/app/bildirimler", "/app/blog-yonetim", "/app/blog-yonetim", "/app/ceza-turleri", "/app/denetim",
            "/app/departmanlar", "/app/dokumanlar", "/app/doluluk-kurallari", "/app/dovizler", "/app/drop-tanimlari",
            "/app/firma-belgeleri", "/app/gelen-talepler", "/app/gelen-talepler", "/app/gider-turleri",
            "/app/hesap-kodlari", "/app/hesaplar", "/app/ice-aktar", "/app/iptal-sebepleri", "/app/kdv-oranlari",
            "/app/kullanicilar", "/app/lokasyonlar", "/app/markalar", "/app/mesaj-sablonlari", "/app/musteri-gruplari",
            "/app/odeme-tipleri", "/app/ozel-kodlar", "/app/personel", "/app/renkler", "/app/rezervasyon-kaynaklari",
            "/app/sigorta-sirketleri", "/app/site-icerik", "/app/subeler", "/app/takvim-abonelik", "/app/ulkeler",
            "/app/vites-turleri", "/app/web-sitesi", "/app/yakit-turleri", "/app/yetki",
        ];
        Assert.Equal(43, f11.Length);
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
                "/app/rez-sartlari", "/app/rezervasyonlar", "/app/rezervasyonlar/yeni", "/app/satislar", "/app/segmentler",
                "/app/segmentler", "/app/servis-tanimlari", "/app/servisler", "/app/sigorta-urunleri", "/app/sikayetler",
                "/app/takvim", "/app/tarife-aktar", "/app/tarife-gruplari", "/app/tarife-matris", "/app/tarifeler",
                "/app/tek-cari-toplu", "/app/teklifler", "/app/toplu-gider", "/app/toplu-tahsilat", "/app/vade",
            }.Concat(f11).OrderBy(r => r, StringComparer.Ordinal),
            items.Where(o => o.Sahip == MenuRegistry.Spa).Select(o => o.Rota).OrderBy(r => r, StringComparer.Ordinal));
        Assert.All(items, o => Assert.Equal(o.Rota.StartsWith("/app/", StringComparison.Ordinal) ? MenuRegistry.Spa : MenuRegistry.Blazor, o.Sahip));
        Assert.All(items, o => Assert.Equal(o.Grup == Shortcuts, o.HizliBaglanti));
        // Modül bayrağı yalnız MainLayout'un @if (_webSitesiModulu) bloğundaki "Web Sitesi" grubunda.
        Assert.All(items, o => Assert.Equal(o.Grup == "Web Sitesi" ? "WebSitesi" : null, o.Modul));
    }

    /// <summary>
    /// F13.1a: Blazor sayfaları silindi; önceden sayfaların <c>[Authorize]</c> özniteliğinden OKUNAN, grup kapısından farklı
    /// sayfa yetkileri burada ELLE donduruldu (silinmeden önceki son ölçüm, 2026-09-26). Anahtar eski Blazor rotası.
    /// </summary>
    private static readonly Dictionary<string, Permission> FrozenPagePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/vehicles/detayli"] = Permission.ViewReports,
        ["/musteri-taksit"] = Permission.FinanceWrite,
        ["/crm"] = Permission.ViewReports,
        ["/tarife-aktar"] = Permission.ManageUsers,
        ["/maliyet-hesapla"] = Permission.FinanceWrite,
        ["/maliyet-teklifleri"] = Permission.FinanceWrite,
    };

    [Fact]
    public void Her_ogenin_izni_sayfanin_yetkisinden_turetilmis()
    {
        var errors = new List<string>();
        foreach (var o in MenuRegistry.Items)
        {
            Permission? expected = FrozenPagePermissions.TryGetValue(OldRoute(o), out var p) ? p : GroupGate[o.Grup];
            if (expected != o.Izin) errors.Add($"{o.Grup}|{o.Rota}: beklenen {expected?.ToString() ?? "(herkes)"}, kayıtta {o.Izin?.ToString() ?? "(herkes)"}");
        }
        Assert.True(errors.Count == 0, "Menü izni sayfa yetkisiyle uyuşmuyor:\n" + string.Join("\n", errors));
    }

    /// <summary>
    /// Kabul testi: rota kapısı "izinlerden BİRİ" olan sayfaların menü görünürlüğü rota ile AYNI küme. Elle yazılmış
    /// oracle — SPA rota dosyalarındaki <c>anyPermissionGuard(...)</c> ve uçların <c>RequireAnyPermission(...)</c>
    /// satırlarından (reports.routes.ts "ops", finance-documents.routes.ts cezalar/satislar, finance.routes.ts kurlar).
    /// Listede olmayan öğenin alternatif izni OLMAZ (tek izinle görünür).
    /// </summary>
    private static readonly Dictionary<string, Permission[]> AnyOfRouteGates = new(StringComparer.Ordinal)
    {
        ["/app/raporlar/arac-durum-takip"] = [Permission.OperationsWrite, Permission.ViewReports],
        ["/app/raporlar/km-detay"] = [Permission.OperationsWrite, Permission.ViewReports],
        ["/app/raporlar/periyodik-servis"] = [Permission.OperationsWrite, Permission.ViewReports],
        ["/app/raporlar/sigorta-muayene"] = [Permission.OperationsWrite, Permission.ViewReports],
        ["/app/raporlar/karsilastirmali-analiz"] = [Permission.OperationsWrite, Permission.ViewReports],
        ["/app/raporlar/personel-calisma"] = [Permission.OperationsWrite, Permission.ViewReports],
        ["/app/cezalar"] = [Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports],
        ["/app/satislar"] = [Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports],
        ["/app/kurlar"] = [Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports],
    };

    [Fact]
    public void Izinlerden_biri_kapili_sayfalarin_menu_izni_rota_kapisiyla_ayni()
    {
        var errors = new List<string>();
        foreach (var o in MenuRegistry.Items)
        {
            var actual = new[] { o.Izin }.OfType<Permission>().Concat(o.AlternatifIzinler ?? []).Distinct().Order().ToList();
            if (AnyOfRouteGates.TryGetValue(o.Rota, out var expected))
            {
                if (!expected.Order().SequenceEqual(actual))
                    errors.Add($"{o.Grup}|{o.Rota}: beklenen {string.Join("∨", expected)}, kayıtta {string.Join("∨", actual)}");
            }
            else if (o.AlternatifIzinler is { Count: > 0 })
                errors.Add($"{o.Grup}|{o.Rota}: alternatif izin beklenmiyordu ({string.Join("∨", o.AlternatifIzinler)})");
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.Equal(AnyOfRouteGates.Keys.Order(), MenuRegistry.Items.Where(o => o.AlternatifIzinler is { Count: > 0 }).Select(o => o.Rota).Order());
    }

    // F13.1b: "MainLayout menüyü kayıttan çizer" testi Blazor kabuğuyla birlikte silindi; menü yalnız /api/ui/v1/menu
    // (MenuApi.Visible — UiSecimMenuTests) üzerinden çizilir.

    // ---- F4.6 rol → izin geçişi (KASITLI davranış değişikliği; PR'daki önce/sonra tablosunun kilidi)

    private static readonly string[] OperationGroups =
        [Shortcuts, "Araçlar", "Kira", "Rezervasyon", "Cariler & CRM", "Web Sitesi", "Servis & Sigorta", "Fiyat & Tarife", "Tanımlar"];

    /// <summary>F4.6 ÖNCESİ MainLayout görünürlüğü (AuthorizeView Roles) — elle yazılmış oracle.</summary>
    private static bool IsPreviouslyVisible(LayoutOgesi o, UserRole rol)
    {
        if (o.Rota == "/tarife-aktar") return rol == UserRole.Admin;                         // iç içe Roles="Admin"
        if (OperationGroups.Contains(o.Grup)) return rol is UserRole.Admin or UserRole.Yonetici or UserRole.Operator;
        if (o.Grup is "Finans" or "Raporlar") return rol is UserRole.Admin or UserRole.Yonetici or UserRole.Muhasebe;
        if (o.Grup == "Sistem") return rol == UserRole.Admin;
        return true;                                                                            // grupsuz: tüm roller
    }

    private static System.Security.Claims.ClaimsPrincipal User(UserRole rol, string[]? extra = null, string[]? ban = null)
    {
        var claims = new List<System.Security.Claims.Claim> { new(System.Security.Claims.ClaimTypes.Role, rol.ToString()) };
        claims.AddRange((extra ?? []).Select(i => new System.Security.Claims.Claim(RentACar.Web.Identity.IdentityClaims.PermissionExtra, i)));
        claims.AddRange((ban ?? []).Select(i => new System.Security.Claims.Claim(RentACar.Web.Identity.IdentityClaims.PermissionDenied, i)));
        return new(new System.Security.Claims.ClaimsIdentity(claims, "test"));
    }

    private static HashSet<string> NewVisible(System.Security.Claims.ClaimsPrincipal u)
        => MenuApi.Visible(u, websiteModule: true).Select(o => $"{o.Grup}|{OldRoute(o)}").ToHashSet();

    [Theory]
    [InlineData(UserRole.Admin, "", "")]
    [InlineData(UserRole.Yonetici, "", "")]
    // Kabul testi: operatör, rota kapısı "OperationsWrite VEYA …" olan altı operasyon raporunu ve Cezalar/Satışlar/Kurlar'ı
    // artık menüde görür (açabildiği sayfayı göremiyordu).
    [InlineData(UserRole.Operator,
        "Raporlar|/raporlar/arac-durum-takip;Raporlar|/raporlar/km-detay;Raporlar|/raporlar/periyodik-servis;" +
        "Raporlar|/raporlar/sigorta-muayene;Raporlar|/raporlar/karsilastirmali-analiz;Raporlar|/raporlar/personel-calisma;" +
        "Finans|/cezalar;Finans|/satislar;Finans|/kurlar",
        "Araçlar|/vehicles/detayli;Araçlar|/musteri-taksit;Cariler & CRM|/crm;Fiyat & Tarife|/maliyet-hesapla;Fiyat & Tarife|/maliyet-teklifleri")]
    [InlineData(UserRole.Muhasebe,
        "Araçlar|/vehicles/detayli;Araçlar|/musteri-taksit;Cariler & CRM|/crm;Fiyat & Tarife|/maliyet-hesapla;Fiyat & Tarife|/maliyet-teklifleri", "")]
    public void Rol_bazinda_menu_farki_F46_oncesine_gore(UserRole rol, string wins, string loses)
    {
        var old = LayoutMenu().Where(o => IsPreviouslyVisible(o, rol)).Select(o => $"{o.Grup}|{o.Rota}").ToHashSet();
        var newItem = NewVisible(User(rol));
        static string[] ListItems(string s) => s.Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(ListItems(wins).OrderBy(x => x), newItem.Except(old).OrderBy(x => x));
        Assert.Equal(ListItems(loses).OrderBy(x => x), old.Except(newItem).OrderBy(x => x));
    }

    [Fact]
    public void Kullanici_bazli_istisna_menuye_yansir()
    {
        // Operatöre EK FinanceWrite: Finans grubu (+ FinanceWrite'lı operasyon öğeleri) görünür olur.
        var extra = NewVisible(User(UserRole.Operator, extra: ["FinanceWrite"]));
        Assert.Contains("Finans|/kasa", extra);
        Assert.Contains("Araçlar|/musteri-taksit", extra);
        Assert.DoesNotContain("Raporlar|/raporlar/gelir-gider", extra);

        // Operatöre YASAK OperationsWrite: operasyon grupları ve kısa yollar (Yeni Kira dahil) gizlenir.
        var ban = NewVisible(User(UserRole.Operator, ban: ["OperationsWrite"]));
        Assert.DoesNotContain(ban, x => x.StartsWith("Araçlar|", StringComparison.Ordinal) || x.StartsWith(Shortcuts + "|", StringComparison.Ordinal));
        Assert.DoesNotContain("Kira|/kiralar", ban);
        Assert.DoesNotContain("Tanımlar|/segmentler", ban);  // F6.4: spa öğesi de izinle gizlenir
        Assert.DoesNotContain("Cariler & CRM|/cariler", ban); // F7.3: aynı kural
        // F7.3: CRM Analiz spa öğesi ViewReports'a bağlı — operatöre ek ViewReports ile görünür, Muhasebe'ye yasakla gizlenir.
        Assert.Contains("Cariler & CRM|/crm", NewVisible(User(UserRole.Operator, extra: ["ViewReports"])));
        Assert.DoesNotContain("Cariler & CRM|/crm", NewVisible(User(UserRole.Muhasebe, ban: ["ViewReports"])));
        // F10.3: Raporlar spa öğeleri de izne bağlı — ek ViewReports ile görünür, yasakla gizlenir.
        Assert.Contains("Raporlar|/raporlar/gunluk", NewVisible(User(UserRole.Operator, extra: ["ViewReports"])));
        Assert.DoesNotContain(NewVisible(User(UserRole.Muhasebe, ban: ["ViewReports"])),
            x => x.StartsWith("Raporlar|", StringComparison.Ordinal));
        // F8.3: Finans spa öğeleri FinanceWrite'a bağlı — operatöre ek FinanceWrite ile görünür (yukarıda), Muhasebe'ye
        // yasakla gizlenir — okuması "FinanceWrite VEYA ViewReports" olan Cezalar/Satışlar/Kurlar hariç (rota kapısı
        // ViewReports'la açılır; menü de gösterir).
        Assert.Contains("Finans|/faturalar", extra);
        var noFinance = NewVisible(User(UserRole.Muhasebe, ban: ["FinanceWrite"]));
        Assert.Equal(new[] { "Finans|/cezalar", "Finans|/kurlar", "Finans|/satislar" },
            noFinance.Where(x => x.StartsWith("Finans|", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        // F9.3: Servis & Sigorta ve Fiyat & Tarife spa öğeleri de izne bağlı; maliyet ekranları FinanceWrite ister.
        Assert.DoesNotContain(ban, x => x.StartsWith("Servis & Sigorta|", StringComparison.Ordinal));
        Assert.DoesNotContain("Fiyat & Tarife|/tarifeler", ban);
        Assert.Contains("Fiyat & Tarife|/maliyet-hesapla", extra);
        Assert.DoesNotContain("Fiyat & Tarife|/tarife-aktar", extra);   // ManageUsers
        // F11.3: Tanımlar spa öğeleri OperationsWrite'a, Sistem spa öğeleri ManageUsers'a bağlı.
        Assert.DoesNotContain("Tanımlar|/markalar", ban);
        Assert.Contains("Tanımlar|/markalar", NewVisible(User(UserRole.Muhasebe, extra: ["OperationsWrite"])));
        Assert.Contains("Sistem|/ayarlar", NewVisible(User(UserRole.Yonetici, extra: ["ManageUsers"])));
        Assert.DoesNotContain(NewVisible(User(UserRole.Admin, ban: ["ManageUsers"])),
            x => x.StartsWith("Sistem|", StringComparison.Ordinal));
        Assert.Contains("|/", ban);                // Panel ve grupsuz öğeler kalır
        Assert.Contains("|/vade", ban);            // F9.3: Vade Panosu spa öğesi grupsuz, izinsiz kalır
        Assert.Contains("|/bildirimler", ban);     // F11.3: grupsuz spa öğesi izinsiz
    }
}
