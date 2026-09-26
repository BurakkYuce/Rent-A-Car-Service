using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// Girişten sonraki iniş ve dönüş adresi (ReturnUrl) kurallarını kilitler.
///
/// <para><b>Neden var (canlı hata):</b> girişten sonra kullanıcı Panel yerine sabit
/// <c>/vehicles</c>'a düşüyordu. Oturumu düşmüş kullanıcı bildirimden / WhatsApp'tan gelen
/// <c>/kiralar/…</c> bağlantısını açınca da düz <c>/login</c>'e (dönüş adresi OLMADAN) gidiyor,
/// girişten sonra yine <c>/vehicles</c>'a iniyordu — derin bağlantı kayboluyordu.</para>
///
/// <para><b>Neden bu kadar kenar durum:</b> dönüş adresi kullanıcının tarayıcısından gelir,
/// dolayısıyla saldırgan kontrolündedir. <c>/login?ReturnUrl=//evil.com</c> bağlantısını paylaşan
/// biri, gerçek giriş ekranımızda şifresini yazan kullanıcıyı kendi sahte sayfasına taşıyabilirdi
/// (açık yönlendirme → kimlik avı). Tablodaki her saldırı satırı <c>GuvenliDonus</c>'taki bir
/// kurala karşılık gelir; kural gevşerse ilgili satır kırmızıya döner.</para>
///
/// <para><b>Bağımsız oracle:</b> beklenen değerlerin hepsi elle yazıldı (senaryodan: "bu adres
/// tarayıcıda nereye gider?"). Hiçbiri test edilen koddan türetilmedi. Yüzde-kodlu beklentiler
/// RFC 3986 kodlamasıyla elle kuruldu: "/"→%2F, "?"→%3F, "="→%3D, "&amp;"→%26, "%"→%25.</para>
///
/// <para><c>Program.cs</c> test edilemediği için 401 yönlendirme kararı
/// (<c>OnRedirectToLogin</c>) saf <see cref="PermissionRedirect.LoginRedirect"/>'ne çıkarıldı;
/// burada o karar test edilir, kaynak taraması da Program.cs'in onu çağırdığını kilitler.</para>
/// </summary>
public sealed class GuvenliDonusTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    // ---------------------------------------------------------------------------------------------
    // GuvenliDonus — elle yazılmış tablo
    // ---------------------------------------------------------------------------------------------

    [Theory]
    // --- Meşru derin bağlantılar: AYNEN döner (kodlanmış hal korunur; tarayıcı bir kez çözecek) ---
    [InlineData("/kiralar", "/kiralar")]
    [InlineData("/kiralar/3f2b8c1e-0000-4000-8000-000000000001?sekme=odeme",
                "/kiralar/3f2b8c1e-0000-4000-8000-000000000001?sekme=odeme")]           // WhatsApp bağlantısı
    [InlineData("/rezervasyonlar?durum=Aktif&sayfa=2", "/rezervasyonlar?durum=Aktif&sayfa=2")] // çok parametre
    [InlineData("/kiralar/5#sekme=finans", "/kiralar/5#sekme=finans")]                      // sekme deep-link
    [InlineData("/ara%C3%A7lar", "/ara%C3%A7lar")]                    // Türkçe yol, yüzde-kodlu
    [InlineData("/kiralar?ara=%2F%2Fevil.com", "/kiralar?ara=%2F%2Fevil.com")] // SORGUDAKİ kodlu "/" zararsız
    [InlineData("/platformlar", "/platformlar")]                      // segment kuralı; önek değil
    [InlineData("/", "/")]
    // --- Boş: gidilecek yer yok → Panel ---
    [InlineData(null, "/")]
    [InlineData("", "/")]
    // --- Başka alan adına kaçış (açık yönlendirme) ---
    [InlineData("//evil.com", "/")]                   // şema-göreli URL
    [InlineData("/\\evil.com", "/")]                  // tarayıcı "\"'yi "/" sayar → //evil.com
    [InlineData("\\\\evil.com", "/")]                 // \\evil.com
    [InlineData("https://evil.com", "/")]             // mutlak URL
    [InlineData("javascript:alert(1)", "/")]          // şema
    [InlineData("~/kiralar", "/")]                    // IsLocalUrl kabul eder; biz tek "/" isteriz
    [InlineData("kiralar", "/")]                      // göreli yol: sayfaya göre çözülür
    // --- Kodlanmış ayırıcılar: arada bir kez daha çözen katman //evil.com yapar ---
    [InlineData("/%2F%2Fevil.com", "/")]
    [InlineData("/%2f/evil.com", "/")]                // küçük harf kodlama
    [InlineData("/%5Cevil.com", "/")]                 // kodlu ters bölü
    [InlineData("/%252F%252Fevil.com", "/")]          // iki kat kodlama
    [InlineData("/%25252F%25252Fevil.com", "/")]      // üç kat kodlama
    [InlineData("/%2525252525252F", "/")]             // durulmayan kodlama (4 turdan derin)
    // --- Kontrol karakteri / boşluk / ASCII dışı ---
    [InlineData("/\t/evil.com", "/")]                 // tarayıcı sekmeyi siler → //evil.com
    [InlineData("/%09/evil.com", "/")]                // aynısının kodlu hali
    [InlineData("/kiralar\r\nSet-Cookie:x=1", "/")]   // başlık enjeksiyonu denemesi
    [InlineData(" /kiralar", "/")]                    // baştaki boşluk tarayıcıda kırpılır
    [InlineData("/araç", "/")]                        // ham ASCII dışı → Location başlığı 500 verirdi
    [InlineData("/kiralar?ara=a\\b", "/")]            // ters bölü sorguda da yok
    // --- Döngü ve alan geçişi ---
    [InlineData("/login", "/")]
    [InlineData("/Login?ReturnUrl=%2Fkiralar", "/")]  // büyük harf + iç içe dönüş
    [InlineData("/auth/logout", "/")]                 // girişten hemen sonra çıkış ettirmesin
    [InlineData("/auth/login", "/")]
    [InlineData("/platform/tenants", "/")]            // süper-admin alanı: ayrı kabuk
    [InlineData("/PLATFORM", "/")]
    [InlineData("/%70latform/tenants", "/")]          // kodlu "p" ile alan kuralını atlatma
    // --- Nokta segmenti: tarayıcı indirger ve döngü/alan kuralını atlatır ---
    [InlineData("/kiralar/../login", "/")]
    [InlineData("/kiralar/%2e%2e/platform", "/")]
    [InlineData("/kiralar/.", "/")]
    public void GuvenliDonus_tablosu(string? input, string expected)
        => Assert.Equal(expected, PermissionRedirect.SafeReturn(input));

    [Fact]
    public void Varsayilan_inis_Panel()
    {
        // Eskiden sabit "/vehicles" idi. Varsayılan iniş Panel ("/") — sabit elle yazıldı.
        Assert.Equal("/", PermissionRedirect.Default);
        Assert.Equal("/", PermissionRedirect.SafeReturn(null));
    }

    // ---------------------------------------------------------------------------------------------
    // 401 yönlendirme kararı (OnRedirectToLogin) — YALNIZ GET dönüş taşır
    // ---------------------------------------------------------------------------------------------

    /// <summary>Yönlendirme adresini sayfa + ayrıştırılmış sorgu parametrelerine böler.</summary>
    private static (string Sayfa, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Parametreler) Split(string address)
    {
        var i = address.IndexOf('?');
        return i < 0
            ? (address, new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>())
            : (address[..i], QueryHelpers.ParseQuery(address[i..]));
    }

    [Theory]
    [InlineData("/kiralar", "", "/kiralar")]
    [InlineData("/kiralar/5", "?sekme=odeme", "/kiralar/5?sekme=odeme")]
    [InlineData("/rezervasyonlar", "?durum=Aktif&sayfa=2", "/rezervasyonlar?durum=Aktif&sayfa=2")]
    [InlineData("/cariler", "?ara=ali+veli%20can", "/cariler?ara=ali+veli%20can")]
    public void GET_asil_hedefi_ReturnUrl_olarak_tasir(string path, string query, string expectedReturn)
    {
        var address = PermissionRedirect.LoginRedirect("GET", path, new QueryString(query));

        // F13.1b: 401 hedefi yeni arayüzün girişi; dönüş SPA'nın returnUrl'i (SPA girişten sonra /login?ReturnUrl='e
        // geri verir → Cutover.AfterLogin, aynı SafeReturn çiti).
        var (page, parameters) = Split(address);
        Assert.Equal("/app/giris", page);
        // TEK parametre: asıl hedefin kendi "&"'i kodlanmalı; kodlanmasaydı "sayfa=2" giriş sayfasının
        // ayrı bir parametresi olur ve dönüş adresi "/rezervasyonlar?durum=Aktif"a kırpılırdı.
        Assert.Equal("returnUrl", Assert.Single(parameters.Keys));
        Assert.Equal(expectedReturn, parameters["returnUrl"].ToString());
    }

    [Fact]
    public void GET_yonlendirmesinin_tam_metni()
    {
        // Elle kodlandı: "/kiralar/5?sekme=odeme" → %2Fkiralar%2F5%3Fsekme%3Dodeme
        Assert.Equal("/app/giris?returnUrl=%2Fkiralar%2F5%3Fsekme%3Dodeme",
            PermissionRedirect.LoginRedirect("GET", "/kiralar/5", new QueryString("?sekme=odeme")));
        Assert.Equal("/app/giris?returnUrl=%2Fkiralar",
            PermissionRedirect.LoginRedirect("GET", "/kiralar", QueryString.Empty));
    }

    [Theory]
    // Oturumu düşmüş kullanıcının FORM GÖNDERİMİ: uç adresi dönüş olsaydı giriş sonrası tarayıcı
    // POST ucuna GET atardı (405/404) ve form verisi zaten kaybolmuştu. Dönüş TAŞINMAZ → Panel.
    [InlineData("POST", "/kiralar/kaydet", "")]
    [InlineData("POST", "/kiralar/kaydet", "?id=5")]
    [InlineData("PUT", "/kiralar/5", "")]
    [InlineData("DELETE", "/kiralar/5", "")]
    [InlineData("PATCH", "/kiralar/5", "")]
    [InlineData("HEAD", "/kiralar", "")]  // sayfa açmaz; kural "yalnız GET"
    public void GET_disi_istek_ReturnUrl_tasimaz(string method, string path, string query)
        => Assert.Equal("/app/giris", PermissionRedirect.LoginRedirect(method, path, new QueryString(query)));

    [Theory]
    [InlineData("GET", "/platform/tenants", "?a=1")]
    [InlineData("GET", "/platform", "")]
    [InlineData("POST", "/platform/tenants/kapat", "")]
    public void Platform_alani_kendi_logine_doner_ve_donus_tasimaz(string method, string path, string query)
        => Assert.Equal("/app/platform/giris", PermissionRedirect.LoginRedirect(method, path, new QueryString(query)));

    [Theory]
    [InlineData("/")]                   // Panel zaten varsayılan → "?ReturnUrl=%2F" gürültüsü yok
    [InlineData("/auth/logout")]        // reddedilecek hedef URL'ye hiç konmaz
    [InlineData("/login")]
    [InlineData("/%2F%2Fevil.com")]     // Kestrel %2F'yi yolda çözmez; çit yine yakalar
    public void GET_ama_guvenli_olmayan_ya_da_varsayilan_hedef_ReturnUrl_tasimaz(string path)
        => Assert.Equal("/app/giris", PermissionRedirect.LoginRedirect("GET", path, QueryString.Empty));

    [Fact]
    public void Tam_tur_bildirim_baglantisi_giristen_sonra_ayni_yere_acilir()
    {
        // Senaryo: WhatsApp'taki "/kiralar/5?sekme=odeme&not=a+b%20c" bağlantısı, oturum düşmüşken.
        // 1) 401 → SPA girişi (returnUrl)   2) SPA girişten sonra /login?ReturnUrl='e geri verir
        // 3) Cutover.AfterLogin → SafeReturn → haritadaki SPA karşılığı (sorgu AYNEN).
        var loginUrl = PermissionRedirect.LoginRedirect(
            "GET", "/kiralar/5", new QueryString("?sekme=odeme&not=a+b%20c"));
        var (_, parameters) = Split(loginUrl);
        var landing = RentACar.Web.Spa.Cutover.AfterLogin(parameters["returnUrl"].ToString());

        Assert.Equal("/kiralar/5?sekme=odeme&not=a+b%20c", landing); // Guid'siz "/kiralar/5" haritada değil → olduğu gibi
    }

    // F13.1b: "hatalı giriş" ve "hız sınırı" dönüş hedefleri (Blazor giriş formunun /login?hata=1|limit yönlendirmesi)
    // silindi — form uçları kalktı; yeni arayüz girişi 401/429'u JSON (ProblemDetails) alır, form korunur
    // (UiApiTests.Giris_hiz_siniri_429_cok_istek_json).

    // ---------------------------------------------------------------------------------------------
    // İndirme uçları dönüş OLAMAZ (adversarial bulgu: giriş ekranına hapsolma)
    // ---------------------------------------------------------------------------------------------
    // Senaryo: 8 saatlik oturum düşmüş; kullanıcı Cariler > Excel'e tıklıyor → /login?ReturnUrl=
    // /listeler/export/cariler → giriş → 302 export'a → dosya İNER ama tarayıcı belgeyi değiştirmez,
    // ekranda giriş formu kalır. Yenileyince Login.razor girişli kullanıcıyı yine indirmeye yollar.

    private const string G = "3f2b8c1e-0000-4000-8000-000000000001";

    [Theory]
    // Web'de Results.File kullanan her tenant ucu — elle yazılmış temsilci adresler.
    [InlineData("/listeler/export/cariler")]                          // ListExportEndpoints
    [InlineData("/listeler/export/kiralar?format=csv&ara=x")]
    [InlineData("/listeler/export-personel")]                         // ListExportEndpoints (personel grubu)
    [InlineData("/listeler/export-personel/?format=pdf")]
    [InlineData("/raporlar/export/filo?format=pdf")]                  // ReportExportEndpoints
    [InlineData("/raporlar/export/arac-karne?vehicleId=" + G + "&format=excel")]
    [InlineData("/dokumanlar/" + G + "/indir")]                       // FirmaDokumanEndpoints (daima ek)
    [InlineData("/firma-belgeleri/" + G + "/indir")]                  // FirmaBelgeEndpoints
    [InlineData("/firma-belgeleri/" + G + "/indir?indir=1")]
    [InlineData("/kiralar/" + G + "/pdf?indir=1")]                    // PdfEndpoints — indir=1 ek
    [InlineData("/kiralar/ornek-sozlesme/pdf?indir=1")]
    [InlineData("/faturalar/" + G + "/pdf?indir=1")]
    [InlineData("/kasa/makbuz/" + G + "/pdf?indir=1")]
    // Harf/kodlama farkı kuralı atlatmaz.
    [InlineData("/LISTELER/EXPORT/cariler")]
    [InlineData("/listeler/%65xport/cariler")]
    [InlineData("/kiralar/" + G + "/pdf?x=1&INDIR=1")]
    public void Indirme_adresi_donus_olamaz(string address)
    {
        Assert.True(PermissionRedirect.IsDownloadUrl(address), address);
        Assert.Equal("/", PermissionRedirect.SafeReturn(address));
    }

    [Theory]
    [InlineData("/kiralar/" + G + "/pdf")]              // anahtarsız PDF tarayıcıda GÖRÜNTÜLENİR → dönüş olabilir
    [InlineData("/faturalar/" + G + "/pdf")]
    [InlineData("/listeler")]
    [InlineData("/raporlar/filo")]                       // rapor EKRANI (export değil)
    [InlineData("/raporlar/exportlar")]                  // segment kuralı; önek değil
    [InlineData("/dokumanlar")]
    [InlineData("/kiralar?ara=indir")]                   // arama DEĞERİ, anahtar değil
    [InlineData("/indirimler")]
    public void Indirme_olmayan_adres_donus_olarak_kalir(string address)
    {
        Assert.False(PermissionRedirect.IsDownloadUrl(address), address);
        Assert.Equal(address, PermissionRedirect.SafeReturn(address));
    }

    [Fact]
    public void Indirme_ucuna_401_indirmeyi_baslatan_sayfaya_doner()
    {
        // Referer'dan çıkarılmış önceki sayfa: kullanıcı girişten sonra listeye döner, Excel'e yeniden basar.
        Assert.Equal("/app/giris?returnUrl=%2Fcariler%3Fara%3Dx",
            PermissionRedirect.LoginRedirect("GET", "/listeler/export/cariler", new QueryString("?ara=x"), "/cariler?ara=x"));
        // Önceki sayfa bilinmiyor → Panel (dönüş yok).
        Assert.Equal("/app/giris",
            PermissionRedirect.LoginRedirect("GET", "/listeler/export/cariler", QueryString.Empty));
        // Önceki sayfa da çitten geçer: indirme / login / yabancı adres yine Panel.
        Assert.Equal("/app/giris",
            PermissionRedirect.LoginRedirect("GET", "/raporlar/export/filo", QueryString.Empty, "/raporlar/export/filo"));
        Assert.Equal("/app/giris",
            PermissionRedirect.LoginRedirect("GET", "/dokumanlar/" + G + "/indir", QueryString.Empty, "//evil.com"));
        Assert.Equal("/app/giris",
            PermissionRedirect.LoginRedirect("GET", "/listeler/export/cariler", QueryString.Empty, "/login?ReturnUrl=%2Fx"));
        // İndirme DEĞİLSE önceki sayfa YOK SAYILIR — derin bağlantının kendisi taşınır.
        Assert.Equal("/app/giris?returnUrl=%2Fkiralar%2F5",
            PermissionRedirect.LoginRedirect("GET", "/kiralar/5", QueryString.Empty, "/cariler"));
        // Panel'e giden (reddedilen) indirme-dışı hedefte de önceki sayfa KULLANILMAZ.
        Assert.Equal("/app/giris",
            PermissionRedirect.LoginRedirect("GET", "/auth/logout", QueryString.Empty, "/cariler"));
    }

    [Theory]
    [InlineData("https://rent.example.com/cariler?ara=x", "rent.example.com", "/cariler?ara=x")]
    [InlineData("http://localhost:5220/kiralar/5?sekme=odeme", "localhost:5220", "/kiralar/5?sekme=odeme")]
    [InlineData("https://rent.example.com/cariler", "rent.example.com:443", "/cariler")]  // Host'ta varsayılan port
    [InlineData("https://RENT.example.com/a", "rent.example.com", "/a")]                // host büyük/küçük harf duyarsız
    [InlineData("https://evil.com/cariler", "rent.example.com", null)]                  // yabancı köken
    [InlineData("http://localhost:9999/x", "localhost:5220", null)]                     // farklı port
    [InlineData("/cariler", "rent.example.com", null)]                                  // mutlak değil
    [InlineData("javascript:alert(1)", "rent.example.com", null)]
    [InlineData("", "rent.example.com", null)]
    [InlineData(null, "rent.example.com", null)]
    public void Ayni_koken_referer_yolu(string? referer, string host, string? expected)
        => Assert.Equal(expected, PermissionRedirect.SameOriginPath(referer, new HostString(host)));

    [Fact]
    public void Dosya_donen_her_web_ucu_dosyasi_indirme_kuralinda_gozden_gecirildi()
    {
        // Yeni bir dosya ucu (Results.File) eklenirse bu test kırılır ve IndirmeAdresiMi'nin o ucu
        // kapsayıp kapsamadığı ELLE gözden geçirilir; kapsıyorsa yukarıdaki tabloya temsilci adres,
        // buraya dosya adı eklenir. Platform alanı dönüş taşımadığı için dışarıda.
        var root = RepoRoot();
        var web = Path.Combine(root, "src/RentACar.Web");
        var separator = Path.DirectorySeparatorChar;
        var found = Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                        && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"\b(Typed)?Results\.File\("))
            .Select(f => Path.GetRelativePath(web, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("Platform/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            // F6.1a — araç fotoğraf içeriği (/api/ui/v1/araclar/{id}/fotograflar/{fotoId}[/kucuk]): indirme DEĞİL
            // (dosya adı verilmez → Content-Disposition yok, görsel satır içi gösterilir) ve /api/ui 401'i
            // yönlendirmesiz JSON'dur (cookie challenge ReturnUrl üretmez). İkisi de UiAracTests
            // .Photo_content_is_inline_and_401_has_no_redirect'te kilitli. IndirmeAdresiMi kapsamı gerekmez.
            "Api/Arac/VehicleApi.Foto.cs",
            // F12.1 — platform konsolu UI API'si: belge içeriği (/api/ui/v1/platform/belgeler/{id}/icerik) ve firma logosu
            // önizlemesi (/api/ui/v1/platform/kiracilar/{id}/logo). Blazor Platform/ alanı gibi DÖNÜŞ taşımaz: /api/ui 401'i
            // yönlendirmesiz JSON'dur (cookie challenge ReturnUrl üretmez; PlatformUiApiTests.Anonymous_gets_401_json_without_redirect).
            // IndirmeAdresiMi kapsamı gerekmez.
            "Api/Platform/PlatformApi.Documents.cs",
            "Api/Platform/PlatformApi.TenantWrites.cs",
            // F11.1b — PDF logosu (/api/ui/v1/ayarlar/logo): indirme DEĞİL (dosya adı yok → Content-Disposition yok, satır
            // içi görsel), nosniff; /api/ui 401'i yönlendirmesiz JSON. UiSystemAdminTests.Logo_upload_...'da kilitli.
            "Api/Sistem/SystemAdminApi.Settings.cs",
            // F11.1b — blog kapağı içeriği (/api/ui/v1/blog-yonetim/{id}/kapak[/kucuk]): araç fotoğrafıyla aynı gerekçe —
            // dosya adı yok (satır içi görsel, indirme değil), /api/ui 401'i yönlendirmesiz JSON. Tür yüklemede İÇERİKTEN
            // tespit edilir, nosniff boru hattında. UiWebsiteApiTests.Blog_cover_* kilitler.
            "Api/Sistem/WebsiteApi.Blog.cs",
            "Documents/CompanyDocumentEndpoints.cs",
            "Documents/CompanyFileEndpoints.cs",
            "Reports/ListExportEndpoints.cs",
            "Reports/PdfEndpoints.cs",
            "Reports/ReportExportEndpoints.cs",
            // F1.5 — /app SPA kabuğu: ANONİM (challenge yok → 401 dönüş kuralı hiç devreye girmez) ve
            // indirme değil (dosyalar satır içi sunulur). IndirmeAdresiMi kapsamı gerekmez.
            "Spa/SpaHosting.cs",
        }, found);
    }

    // ---------------------------------------------------------------------------------------------
    // Kaynak taraması: saf fonksiyonların GERÇEKTEN çağrıldığı (Program.cs / uç / sayfa test edilemez)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Program_cs_401de_refereri_tasir()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src/RentACar.Web/Program.cs"));

        Assert.Contains("PermissionRedirect.SameOriginPath(ctx.Request.Headers.Referer", program, StringComparison.Ordinal);
        // F13.1b: hız sınırı Blazor giriş formuna yönlendirmez (form yok) — /api/ui 429 ProblemDetails, diğerleri ham 429.
        Assert.DoesNotContain("\"/login?hata=limit\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Blazor_giris_ucu_yok_cikis_ucu_SPA_girisine_doner()
    {
        // F13.1b: POST /auth/login (Blazor formu) silindi; giriş yalnız /api/ui/v1/oturum/giris'te. Çıkış ucu sabit
        // SPA girişine döner (kullanıcı girdisi taşımaz — açık yönlendirme yok).
        var endpoint = File.ReadAllText(Path.Combine(RepoRoot(), "src/RentACar.Web/Identity/AuthEndpoints.cs"));
        Assert.DoesNotContain("\"/auth/login\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/auth/logout\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("Results.Redirect(RentACar.Web.Spa.Cutover.SpaLogin", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_cs_401_kararini_saf_fonksiyona_yontemle_birlikte_verir()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src/RentACar.Web/Program.cs"));

        // Yöntem verilmezse "yalnız GET" kuralı uygulanamaz; eski hali yalnız yolu veriyordu.
        Assert.Contains("PermissionRedirect.LoginRedirect(", program, StringComparison.Ordinal);
        Assert.Contains("ctx.Request.Method", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Redirect(PermissionRedirect.LoginTarget(ctx.Request.Path))", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// F13.1a: Blazor giriş sayfası (<c>Login.razor</c>, gizli dönüş alanı) silindi. <c>GET /login</c> tek girişe
    /// (<c>/app/giris</c>) gider ve dönüş adresini YALNIZ <see cref="PermissionRedirect.SafeReturn"/>'ten geçmiş haliyle
    /// taşır (<c>Cutover.LoginRedirect</c>; davranış <c>IlkKesisTests.Oturumsuz_login_SPA_girisine</c>'de kilitli).
    /// </summary>
    [Fact]
    public void Login_donusu_yalniz_suzulmus_haliyle_tasinir()
    {
        Assert.False(Directory.Exists(Path.Combine(RepoRoot(), "src/RentACar.Web/Components")));
        var cutover = File.ReadAllText(Path.Combine(RepoRoot(), "src/RentACar.Web/Spa/Cutover.cs"));
        Assert.Contains("PermissionRedirect.SafeReturn(raw.ToString())", cutover, StringComparison.Ordinal);
    }
}
