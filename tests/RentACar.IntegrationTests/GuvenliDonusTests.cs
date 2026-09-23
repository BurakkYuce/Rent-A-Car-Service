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
/// (<c>OnRedirectToLogin</c>) saf <see cref="YetkiYonlendirme.GirisYonlendirmesi"/>'ne çıkarıldı;
/// burada o karar test edilir, kaynak taraması da Program.cs'in onu çağırdığını kilitler.</para>
/// </summary>
public sealed class GuvenliDonusTests
{
    private static string RepoKok()
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
    public void GuvenliDonus_tablosu(string? girdi, string beklenen)
        => Assert.Equal(beklenen, YetkiYonlendirme.GuvenliDonus(girdi));

    [Fact]
    public void Varsayilan_inis_Panel()
    {
        // Eskiden sabit "/vehicles" idi. Varsayılan iniş Panel ("/") — sabit elle yazıldı.
        Assert.Equal("/", YetkiYonlendirme.Varsayilan);
        Assert.Equal("/", YetkiYonlendirme.GuvenliDonus(null));
    }

    // ---------------------------------------------------------------------------------------------
    // 401 yönlendirme kararı (OnRedirectToLogin) — YALNIZ GET dönüş taşır
    // ---------------------------------------------------------------------------------------------

    /// <summary>Yönlendirme adresini sayfa + ayrıştırılmış sorgu parametrelerine böler.</summary>
    private static (string Sayfa, Dictionary<string, Microsoft.Extensions.Primitives.StringValues> Parametreler) Ayir(string adres)
    {
        var i = adres.IndexOf('?');
        return i < 0
            ? (adres, new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>())
            : (adres[..i], QueryHelpers.ParseQuery(adres[i..]));
    }

    [Theory]
    [InlineData("/kiralar", "", "/kiralar")]
    [InlineData("/kiralar/5", "?sekme=odeme", "/kiralar/5?sekme=odeme")]
    [InlineData("/rezervasyonlar", "?durum=Aktif&sayfa=2", "/rezervasyonlar?durum=Aktif&sayfa=2")]
    [InlineData("/cariler", "?ara=ali+veli%20can", "/cariler?ara=ali+veli%20can")]
    public void GET_asil_hedefi_ReturnUrl_olarak_tasir(string yol, string sorgu, string beklenenDonus)
    {
        var adres = YetkiYonlendirme.GirisYonlendirmesi("GET", yol, new QueryString(sorgu));

        var (sayfa, parametreler) = Ayir(adres);
        Assert.Equal("/login", sayfa);
        // TEK parametre: asıl hedefin kendi "&"'i kodlanmalı; kodlanmasaydı "sayfa=2" login sayfasının
        // ayrı bir parametresi olur ve dönüş adresi "/rezervasyonlar?durum=Aktif"a kırpılırdı.
        Assert.Equal(YetkiYonlendirme.DonusParametresi, Assert.Single(parametreler.Keys));
        Assert.Equal(beklenenDonus, parametreler[YetkiYonlendirme.DonusParametresi].ToString());
    }

    [Fact]
    public void GET_yonlendirmesinin_tam_metni()
    {
        // Elle kodlandı: "/kiralar/5?sekme=odeme" → %2Fkiralar%2F5%3Fsekme%3Dodeme
        Assert.Equal("/login?ReturnUrl=%2Fkiralar%2F5%3Fsekme%3Dodeme",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/kiralar/5", new QueryString("?sekme=odeme")));
        Assert.Equal("/login?ReturnUrl=%2Fkiralar",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/kiralar", QueryString.Empty));
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
    public void GET_disi_istek_ReturnUrl_tasimaz(string yontem, string yol, string sorgu)
        => Assert.Equal("/login", YetkiYonlendirme.GirisYonlendirmesi(yontem, yol, new QueryString(sorgu)));

    [Theory]
    [InlineData("GET", "/platform/tenants", "?a=1")]
    [InlineData("GET", "/platform", "")]
    [InlineData("POST", "/platform/tenants/kapat", "")]
    public void Platform_alani_kendi_logine_doner_ve_donus_tasimaz(string yontem, string yol, string sorgu)
        => Assert.Equal("/platform/login", YetkiYonlendirme.GirisYonlendirmesi(yontem, yol, new QueryString(sorgu)));

    [Theory]
    [InlineData("/")]                   // Panel zaten varsayılan → "?ReturnUrl=%2F" gürültüsü yok
    [InlineData("/auth/logout")]        // reddedilecek hedef URL'ye hiç konmaz
    [InlineData("/login")]
    [InlineData("/%2F%2Fevil.com")]     // Kestrel %2F'yi yolda çözmez; çit yine yakalar
    public void GET_ama_guvenli_olmayan_ya_da_varsayilan_hedef_ReturnUrl_tasimaz(string yol)
        => Assert.Equal("/login", YetkiYonlendirme.GirisYonlendirmesi("GET", yol, QueryString.Empty));

    [Fact]
    public void Tam_tur_bildirim_baglantisi_giristen_sonra_ayni_yere_acilir()
    {
        // Senaryo: WhatsApp'taki "/kiralar/5?sekme=odeme&not=a+b%20c" bağlantısı, oturum düşmüşken.
        // 1) 401 → login adresi   2) Login.razor ReturnUrl'i okur (sorgu bir kez çözülür)
        // 3) gizli alan → /auth/login → GuvenliDonus → LocalRedirect hedefi.
        var loginAdresi = YetkiYonlendirme.GirisYonlendirmesi(
            "GET", "/kiralar/5", new QueryString("?sekme=odeme&not=a+b%20c"));
        var (_, parametreler) = Ayir(loginAdresi);
        var gizliAlan = YetkiYonlendirme.GuvenliDonus(parametreler[YetkiYonlendirme.DonusParametresi].ToString());
        var inis = YetkiYonlendirme.GuvenliDonus(gizliAlan);

        Assert.Equal("/kiralar/5?sekme=odeme&not=a+b%20c", inis);
    }

    // ---------------------------------------------------------------------------------------------
    // Hatalı giriş: dönüş korunur (şifreyi bir kez yanlış yazan derin bağlantıyı kaybetmesin)
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/kiralar/5?sekme=odeme", "/login?hata=1&ReturnUrl=%2Fkiralar%2F5%3Fsekme%3Dodeme")]
    [InlineData("/kiralar", "/login?hata=1&ReturnUrl=%2Fkiralar")]
    [InlineData(null, "/login?hata=1")]
    [InlineData("", "/login?hata=1")]
    [InlineData("/", "/login?hata=1")]
    [InlineData("//evil.com", "/login?hata=1")]          // saldırgan değeri geri YANSITILMAZ
    [InlineData("/platform/tenants", "/login?hata=1")]
    public void Hatali_giris_hedefi(string? donus, string beklenen)
        => Assert.Equal(beklenen, YetkiYonlendirme.HataliGirisHedefi(donus));

    [Theory]
    [InlineData("/kiralar/5?sekme=odeme", "/login?hata=limit&ReturnUrl=%2Fkiralar%2F5%3Fsekme%3Dodeme")]
    [InlineData(null, "/login?hata=limit")]
    [InlineData("//evil.com", "/login?hata=limit")]
    [InlineData("/listeler/export/cariler", "/login?hata=limit")]  // indirme dönüş olamaz
    public void Hiz_siniri_hedefi_donusu_korur(string? donus, string beklenen)
        => Assert.Equal(beklenen, YetkiYonlendirme.LimitHedefi(donus));

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
    public void Indirme_adresi_donus_olamaz(string adres)
    {
        Assert.True(YetkiYonlendirme.IndirmeAdresiMi(adres), adres);
        Assert.Equal("/", YetkiYonlendirme.GuvenliDonus(adres));
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
    public void Indirme_olmayan_adres_donus_olarak_kalir(string adres)
    {
        Assert.False(YetkiYonlendirme.IndirmeAdresiMi(adres), adres);
        Assert.Equal(adres, YetkiYonlendirme.GuvenliDonus(adres));
    }

    [Fact]
    public void Indirme_ucuna_401_indirmeyi_baslatan_sayfaya_doner()
    {
        // Referer'dan çıkarılmış önceki sayfa: kullanıcı girişten sonra listeye döner, Excel'e yeniden basar.
        Assert.Equal("/login?ReturnUrl=%2Fcariler%3Fara%3Dx",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/listeler/export/cariler", new QueryString("?ara=x"), "/cariler?ara=x"));
        // Önceki sayfa bilinmiyor → Panel (dönüş yok).
        Assert.Equal("/login",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/listeler/export/cariler", QueryString.Empty));
        // Önceki sayfa da çitten geçer: indirme / login / yabancı adres yine Panel.
        Assert.Equal("/login",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/raporlar/export/filo", QueryString.Empty, "/raporlar/export/filo"));
        Assert.Equal("/login",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/dokumanlar/" + G + "/indir", QueryString.Empty, "//evil.com"));
        Assert.Equal("/login",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/listeler/export/cariler", QueryString.Empty, "/login?ReturnUrl=%2Fx"));
        // İndirme DEĞİLSE önceki sayfa YOK SAYILIR — derin bağlantının kendisi taşınır.
        Assert.Equal("/login?ReturnUrl=%2Fkiralar%2F5",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/kiralar/5", QueryString.Empty, "/cariler"));
        // Panel'e giden (reddedilen) indirme-dışı hedefte de önceki sayfa KULLANILMAZ.
        Assert.Equal("/login",
            YetkiYonlendirme.GirisYonlendirmesi("GET", "/auth/logout", QueryString.Empty, "/cariler"));
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
    public void Ayni_koken_referer_yolu(string? referer, string host, string? beklenen)
        => Assert.Equal(beklenen, YetkiYonlendirme.AyniKokenYolu(referer, new HostString(host)));

    [Fact]
    public void Dosya_donen_her_web_ucu_dosyasi_indirme_kuralinda_gozden_gecirildi()
    {
        // Yeni bir dosya ucu (Results.File) eklenirse bu test kırılır ve IndirmeAdresiMi'nin o ucu
        // kapsayıp kapsamadığı ELLE gözden geçirilir; kapsıyorsa yukarıdaki tabloya temsilci adres,
        // buraya dosya adı eklenir. Platform alanı dönüş taşımadığı için dışarıda.
        var kok = RepoKok();
        var web = Path.Combine(kok, "src/RentACar.Web");
        var ayirici = Path.DirectorySeparatorChar;
        var bulunan = Directory.EnumerateFiles(web, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{ayirici}obj{ayirici}", StringComparison.Ordinal)
                        && !f.Contains($"{ayirici}bin{ayirici}", StringComparison.Ordinal))
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
            "Api/Arac/AracApi.Foto.cs",
            "Documents/FirmaBelgeEndpoints.cs",
            "Documents/FirmaDokumanEndpoints.cs",
            "Reports/ListExportEndpoints.cs",
            "Reports/PdfEndpoints.cs",
            "Reports/ReportExportEndpoints.cs",
            // F1.5 — /app SPA kabuğu: ANONİM (challenge yok → 401 dönüş kuralı hiç devreye girmez) ve
            // indirme değil (dosyalar satır içi sunulur). IndirmeAdresiMi kapsamı gerekmez.
            "Spa/SpaBarindirma.cs",
        }, bulunan);
    }

    // ---------------------------------------------------------------------------------------------
    // Kaynak taraması: saf fonksiyonların GERÇEKTEN çağrıldığı (Program.cs / uç / sayfa test edilemez)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Program_cs_401de_refereri_hiz_sinirinda_donusu_tasir()
    {
        var program = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/Program.cs"));

        Assert.Contains("YetkiYonlendirme.AyniKokenYolu(ctx.Request.Headers.Referer", program, StringComparison.Ordinal);
        Assert.Contains("YetkiYonlendirme.LimitHedefi(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/login?hata=limit\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Giris_ucu_sabit_vehicles_e_degil_guvenli_donuse_yonlendirir()
    {
        var uc = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/Identity/AuthEndpoints.cs"));

        Assert.DoesNotContain("Redirect(\"/vehicles\")", uc, StringComparison.Ordinal);
        // LocalRedirect ikinci çit: GuvenliDonus gerilese bile yerel olmayan adrese gidilmez.
        Assert.Contains("Results.LocalRedirect(YetkiYonlendirme.GuvenliDonus(", uc, StringComparison.Ordinal);
        Assert.Contains("YetkiYonlendirme.HataliGirisHedefi(", uc, StringComparison.Ordinal);
        Assert.DoesNotContain("Redirect(\"/login?hata=1\")", uc, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_cs_401_kararini_saf_fonksiyona_yontemle_birlikte_verir()
    {
        var program = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/Program.cs"));

        // Yöntem verilmezse "yalnız GET" kuralı uygulanamaz; eski hali yalnız yolu veriyordu.
        Assert.Contains("YetkiYonlendirme.GirisYonlendirmesi(", program, StringComparison.Ordinal);
        Assert.Contains("ctx.Request.Method", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Redirect(YetkiYonlendirme.GirisHedefi(ctx.Request.Path))", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_sayfasi_donusu_gizli_alanda_ve_suzulmus_tasir()
    {
        var sayfa = File.ReadAllText(Path.Combine(RepoKok(), "src/RentACar.Web/Components/Pages/Login.razor"));

        Assert.Contains("type=\"hidden\" name=\"@RentACar.Web.Identity.YetkiYonlendirme.DonusParametresi\"",
            sayfa, StringComparison.Ordinal);
        // Ham ReturnUrl sayfaya basılmaz; yalnız GuvenliDonus'tan geçmiş hali.
        Assert.Contains("YetkiYonlendirme.GuvenliDonus(ReturnUrl)", sayfa, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"@ReturnUrl\"", sayfa, StringComparison.Ordinal);
    }
}
