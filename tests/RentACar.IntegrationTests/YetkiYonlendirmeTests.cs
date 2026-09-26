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
    [Theory]
    [InlineData("/kiralar/cancel", "/yetkisiz")]      // canlı hatanın tetikleyicisi
    [InlineData("/finans/tahsilat/ters", "/yetkisiz")]
    [InlineData("/", "/yetkisiz")]
    [InlineData("/platform/tenants", "/platform/login")]   // platform ayrı kabuk, kendi login'i
    [InlineData("/platform", "/platform/login")]
    public void Yetkisiz_403_hedefi(string path, string expected)
        => Assert.Equal(expected, PermissionRedirect.UnauthorizedTarget(path));

    [Theory]
    [InlineData("/kiralar", "/login")]
    [InlineData("/platform/tenants", "/platform/login")]
    public void Kimliksiz_401_hedefi(string path, string expected)
        => Assert.Equal(expected, PermissionRedirect.LoginTarget(path));

    [Fact]
    public void Platform_benzeri_ad_platform_sayilmaz()
    {
        // "/platformlar" gibi bir yol platform alanı DEĞİLDİR; StartsWithSegments bunu ayırt eder.
        // Düz string StartsWith kullanılsaydı bu yol yanlışlıkla platform login'ine giderdi.
        Assert.Equal("/yetkisiz", PermissionRedirect.UnauthorizedTarget("/platformlar"));
        Assert.Equal("/login", PermissionRedirect.LoginTarget("/platformlar"));
    }

    [Fact]
    public void Program_cs_403u_login_e_yonlendirmiyor()
    {
        // Regresyon çiti: AccessDeniedPath tekrar "/login" olursa 403 sessizce köke düşer.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);

        var program = File.ReadAllText(Path.Combine(d!.FullName, "src/RentACar.Web/Program.cs"));
        Assert.DoesNotContain("AccessDeniedPath = \"/login\"", program, StringComparison.Ordinal);
        Assert.Contains("AccessDeniedPath = \"/yetkisiz\"", program, StringComparison.Ordinal);
    }
}
