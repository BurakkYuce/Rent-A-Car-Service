using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// 403'ün nereye gittiğini kilitler.
///
/// <para><b>Neden var (canlı hata):</b> <c>AccessDeniedPath</c> <c>/login</c> idi. Yetkisiz bir POST
/// 403 üretiyor, kullanıcı login'e yönleniyor, ama ZATEN girişli olduğu için <c>Login.razor</c>
/// (OnInitializedAsync) onu uygulamaya — <c>/</c>'a — atıyordu. Ekranda hiçbir hata görünmüyordu.
/// Kullanıcının "durduk yere ana ekrana atıyor" şikayetinin üç kök nedeninden biri buydu.</para>
///
/// <para>Somut tetikleyici: Operatör rolündeki kullanıcı kira ekranındaki "Kira İptal" düğmesine
/// basınca. Düğme <c>OperationsWrite</c>'a bakıyor ama <c>/kiralar/cancel</c> ucu
/// <c>OperationsDelete</c> istiyor ve Operatör'de o izin yok. Düğme/uç uyumsuzluğu ayrıca
/// yapısal bir çitle kapatılıyor; burada kilitlenen 403'ün NEREYE gittiği.</para>
/// </summary>
public sealed class YetkiYonlendirmeTests
{
    // F13.1b: 403 → Blazor /yetkisiz sayfası yerine yeni arayüzün Panel'i + hata bandı (kullanıcı nedenini görür);
    // 401 → yeni arayüzün girişi. Beklenenler elle yazılmış sabit (mesajın yüzde-kodlu hali).
    private const string NoPermissionTarget = "/app/panel?hata=yetki_yok"; // kod (serbest metin değil)

    [Theory]
    [InlineData("/kiralar/cancel", NoPermissionTarget)]      // canlı hatanın tetikleyicisi
    [InlineData("/listeler/export/kiralar", NoPermissionTarget)]
    [InlineData("/", NoPermissionTarget)]
    [InlineData("/platform/tenants", "/app/platform/giris")]   // platform ayrı kabuk, kendi girişi
    [InlineData("/platform", "/app/platform/giris")]
    public void Yetkisiz_403_hedefi(string path, string expected)
        => Assert.Equal(expected, PermissionRedirect.UnauthorizedTarget(path));

    [Theory]
    [InlineData("/kiralar", "/app/giris")]
    [InlineData("/platform/tenants", "/app/platform/giris")]
    public void Kimliksiz_401_hedefi(string path, string expected)
        => Assert.Equal(expected, PermissionRedirect.LoginTarget(path));

    [Fact]
    public void Platform_benzeri_ad_platform_sayilmaz()
    {
        // "/platformlar" gibi bir yol platform alanı DEĞİLDİR; StartsWithSegments bunu ayırt eder.
        // Düz string StartsWith kullanılsaydı bu yol yanlışlıkla platform girişine giderdi.
        Assert.Equal(NoPermissionTarget, PermissionRedirect.UnauthorizedTarget("/platformlar"));
        Assert.Equal("/app/giris", PermissionRedirect.LoginTarget("/platformlar"));
    }

    [Fact]
    public void Program_cs_giris_ve_yetkisiz_yolu_yeni_arayuzde()
    {
        // Regresyon çiti: AccessDeniedPath "/login" olursa 403 sessizce köke düşer (eski canlı hata); Blazor
        // sayfaları (/login formu, /yetkisiz) kalmadığı için ikisi de SPA sabitlerinden gelir.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);

        var program = File.ReadAllText(Path.Combine(d!.FullName, "src/RentACar.Web/Program.cs"));
        Assert.DoesNotContain("AccessDeniedPath = \"/login\"", program, StringComparison.Ordinal);
        Assert.Contains("options.LoginPath = RentACar.Web.Spa.Cutover.SpaLogin;", program, StringComparison.Ordinal);
        Assert.Contains("options.AccessDeniedPath = RentACar.Web.Spa.Cutover.SpaPanel;", program, StringComparison.Ordinal);
        Assert.Equal("/app/giris", RentACar.Web.Spa.Cutover.SpaLogin);
    }
}
