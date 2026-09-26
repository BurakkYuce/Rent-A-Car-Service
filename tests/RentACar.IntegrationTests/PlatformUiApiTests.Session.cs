using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class PlatformUiApiTests
{
    [Fact]
    public async Task Login_me_logout_round_trip()
    {
        var s = await PlatformLoginAsync();
        var me = await s.C.GetAsync(P + "/oturum/ben");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(fx.Platform.Kullanici, (await Json(me)).GetProperty("kullanici").GetString());
        Assert.True(me.Headers.CacheControl?.NoStore == true);

        var logout = await Send(s, HttpMethod.Post, P + "/oturum/cikis");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await ExpectProblem(await s.C.GetAsync(P + "/oturum/ben"), HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Wrong_password_is_a_form_error_and_sets_no_session()
    {
        var c = fx.Web.Client();
        var r = await Send(c, await XsrfAsync(c), HttpMethod.Post, P + "/oturum/giris",
            new { kullanici = fx.Platform.Kullanici, sifre = WebFixture.RandomPassword() });
        await ExpectProblem(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Null(CookieValue(r, "racar.session"));

        // A tenant user's credentials are not platform credentials.
        var t = await Send(c, await XsrfAsync(c), HttpMethod.Post, P + "/oturum/giris",
            new { kullanici = fx.PilotAdmin.Kullanici, sifre = fx.PilotAdmin.Sifre });
        await ExpectProblem(t, HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Unsafe_requests_need_the_xsrf_header_and_a_post_login_token()
    {
        var c = fx.Web.Client();
        var before = await XsrfAsync(c);
        // Login itself needs the header.
        await ExpectProblem(await Send(c, null, HttpMethod.Post, P + "/oturum/giris",
            new { kullanici = fx.Platform.Kullanici, sifre = fx.Platform.Sifre }), HttpStatusCode.BadRequest, "xsrf_gecersiz");

        var ok = await Send(c, before, HttpMethod.Post, P + "/oturum/giris",
            new { kullanici = fx.Platform.Kullanici, sifre = fx.Platform.Sifre });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var id = fx.PilotCompanyId;

        await ExpectProblem(await Send(c, null, HttpMethod.Post, P + $"/kiracilar/{id}/yeni-arayuz-pilot", new { aktif = true }),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        // The token issued BEFORE login is bound to the anonymous identity → rejected.
        await ExpectProblem(await Send(c, before, HttpMethod.Post, P + $"/kiracilar/{id}/yeni-arayuz-pilot", new { aktif = true }),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
    }
}
