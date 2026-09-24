using System.Net;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    // F11.2b güvenlik M1 — yönetici parola sıfırlaması kendi hesabına uygulanmaz (eski parola doğrulaması + giriş hız
    // sınırı atlanırdı). Kendi parolası yalnız /profil/sifre ile değişir. Beklenenler elle kurulmuş senaryodan: sıfırlama
    // reddedilir → eski parolayla giriş hâlâ çalışır; başkasını sıfırlama çalışmaya devam eder → yeni parolayla giriş.
    [Fact]
    public async Task Admin_and_manager_cannot_reset_own_password_but_can_reset_others()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Manager]}/istisnalar/ManageUsers", new { ver = true }));
        var manager = await _kit.LoginAsync(e, Who.Manager);
        var pw = WebFixture.RastgeleParola();

        await Problem(await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/sifre", new { sifre = pw }),
            HttpStatusCode.BadRequest, "dogrulama", "sifre");
        await Problem(await Send(manager, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Manager]}/sifre", new { sifre = pw }),
            HttpStatusCode.BadRequest, "dogrulama", "sifre");

        // Eski parolalar değişmedi.
        await _kit.LoginAsync(e, Who.Admin);
        await _kit.LoginAsync(e, Who.Manager);

        // Başkasını sıfırlama çalışır (Admin → Yönetici, istisnalı Yönetici → Operatör).
        var managerPw = WebFixture.RastgeleParola();
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Manager]}/sifre", new { sifre = managerPw })).StatusCode);
        var opPw = WebFixture.RastgeleParola();
        var manager2 = await _kit.LoginAsync(e, Who.Manager, managerPw);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(manager2, HttpMethod.Post, $"{Users}/{e.UserIds[Who.OperatorA]}/sifre", new { sifre = opPw })).StatusCode);
        await _kit.LoginAsync(e, Who.OperatorA, opPw);
    }
}
