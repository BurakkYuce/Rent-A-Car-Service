using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class PlatformUiApiTests
{
    private static string NewCode() => "p" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Creates a tenant THROUGH the API; returns (id, first admin credentials).</summary>
    private async Task<(Guid Id, TestKimlik Admin)> CreateTenantAsync(Session s, string? name = null)
    {
        var admin = new TestKimlik(NewCode(), "admin" + Guid.NewGuid().ToString("N")[..6], WebFixture.RastgeleParola(), "");
        var r = await Send(s, HttpMethod.Post, P + "/kiracilar",
            new { kod = admin.Firma, ad = name ?? "Platform API Firması", adminKullanici = admin.Kullanici, adminSifre = admin.Sifre });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var j = await Json(r);
        Assert.Equal(admin.Firma, j.GetProperty("kod").GetString());
        Assert.Equal("Aktif", j.GetProperty("durum").GetString());
        Assert.Equal(P + "/kiracilar/" + j.GetProperty("id").GetGuid(), r.Headers.Location?.OriginalString);
        return (j.GetProperty("id").GetGuid(), admin);
    }

    [Fact]
    public async Task Create_validates_code_format_and_uniqueness_and_is_audited_without_password()
    {
        var s = await PlatformLoginAsync();
        var (id, admin) = await CreateTenantAsync(s);

        // Same code → 409 cakisma on the field; different case → format error (codes are lowercase only).
        var dup = await Send(s, HttpMethod.Post, P + "/kiracilar",
            new { kod = admin.Firma, ad = "Kopya", adminKullanici = "a", adminSifre = WebFixture.RastgeleParola() });
        await ExpectProblem(dup, HttpStatusCode.Conflict, "cakisma", "kod");
        foreach (var bad in new[] { admin.Firma.ToUpperInvariant(), "a", "-abc", "ab c", "abc-", "çiçek", new string('a', 65) })
            await ExpectProblem(await Send(s, HttpMethod.Post, P + "/kiracilar",
                new { kod = bad, ad = "X", adminKullanici = "a", adminSifre = WebFixture.RastgeleParola() }),
                HttpStatusCode.BadRequest, "dogrulama", "kod");
        await ExpectProblem(await Send(s, HttpMethod.Post, P + "/kiracilar",
            new { kod = NewCode(), ad = "X", adminKullanici = "a", adminSifre = "12345" }),
            HttpStatusCode.BadRequest, "dogrulama", "adminSifre");

        // The first admin can log in to the tenant UI (login is pilot-exempt).
        var login = await TenantLoginRawAsync(fx.Web.Istemci(), admin);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var audit = await AuditRowsAsync(id);
        var create = Assert.Single(audit, a => a.Action == 0); // AuditAction.Create
        Assert.Equal("Tenant", create.EntityName);
        Assert.Equal("platform:" + fx.Platform.Kullanici, create.UserName);
        Assert.Contains(admin.Kullanici, create.NewValues);
        Assert.DoesNotContain(admin.Sifre, create.NewValues);
    }

    [Fact]
    public async Task Status_passive_and_closed_refuse_login_and_cut_open_sessions_then_reopen()
    {
        var s = await PlatformLoginAsync();
        var (id, admin) = await CreateTenantAsync(s);
        var open = await TenantLoginAsync(admin);

        // Pasif: open session drops on its next request, new login is refused.
        var r = await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Pasif" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Pasif", (await Json(r)).GetProperty("durum").GetString());
        await ExpectProblem(await open.C.GetAsync(V1 + "/oturum/ben"), HttpStatusCode.Unauthorized, "kiraci_kapali");
        await ExpectProblem(await TenantLoginRawAsync(fx.Web.Istemci(), admin), HttpStatusCode.BadRequest, "dogrulama");

        // Pasif → Aktif: login works again.
        Assert.Equal(HttpStatusCode.OK, (await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Aktif" })).StatusCode);
        var again = await TenantLoginAsync(admin);

        // Kapali needs the typed code; a wrong one changes nothing.
        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Kapali", onayKod = "yanlis" }),
            HttpStatusCode.BadRequest, "dogrulama", "onayKod");
        Assert.Equal(HttpStatusCode.OK, (await again.C.GetAsync(V1 + "/oturum/ben")).StatusCode);

        var close = await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Kapali", onayKod = admin.Firma });
        Assert.Equal("Kapali", (await Json(close)).GetProperty("durum").GetString());
        await ExpectProblem(await again.C.GetAsync(V1 + "/oturum/ben"), HttpStatusCode.Unauthorized, "kiraci_kapali");
        await ExpectProblem(await TenantLoginRawAsync(fx.Web.Istemci(), admin), HttpStatusCode.BadRequest, "dogrulama");
        // Kapali → Pasif is refused (reopen first).
        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Pasif" }),
            HttpStatusCode.BadRequest, "dogrulama", "durum");

        // Kapali → Aktif = reopen.
        var reopen = await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Aktif" });
        Assert.Equal("Aktif", (await Json(reopen)).GetProperty("durum").GetString());
        Assert.Equal(HttpStatusCode.OK, (await TenantLoginRawAsync(fx.Web.Istemci(), admin)).StatusCode);

        // Same-state request: idempotent, no extra audit row.
        Assert.Equal(HttpStatusCode.OK, (await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Aktif" })).StatusCode);
        // Audit: create + Pasif + Aktif + Kapali + reopen = 1 create, 4 updates on Tenant.
        var audit = await AuditRowsAsync(id);
        Assert.Equal(4, audit.Count(a => a.Action == 1 && a.EntityName == "Tenant"));
        Assert.All(audit, a => Assert.Equal("platform:" + fx.Platform.Kullanici, a.UserName));

        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/kiracilar/{Guid.NewGuid()}/durum", new { durum = "Pasif" }),
            HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Silindi" }),
            HttpStatusCode.BadRequest, "dogrulama", "durum");
    }

    [Fact]
    public async Task Pilot_switch_opens_and_closes_the_tenant_ui_api_and_is_audited()
    {
        var s = await PlatformLoginAsync();
        var (id, admin) = await CreateTenantAsync(s);
        var t = await TenantLoginAsync(admin);
        await ExpectProblem(await t.C.GetAsync(V1 + "/araclar?boyut=1"), HttpStatusCode.Forbidden, "pilot_degil");

        var on = await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/yeni-arayuz-pilot", new { aktif = true });
        Assert.True((await Json(on)).GetProperty("yeniArayuzPilot").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await t.C.GetAsync(V1 + "/araclar?boyut=1")).StatusCode);

        var off = await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/yeni-arayuz-pilot", new { aktif = false });
        Assert.False((await Json(off)).GetProperty("yeniArayuzPilot").GetBoolean());
        await ExpectProblem(await t.C.GetAsync(V1 + "/araclar?boyut=1"), HttpStatusCode.Forbidden, "pilot_degil");

        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/yeni-arayuz-pilot", new { }),
            HttpStatusCode.BadRequest, "dogrulama", "aktif");
        var audit = await AuditRowsAsync(id);
        Assert.Equal(2, audit.Count(a => a.EntityName == "TenantSettings" && (a.NewValues ?? "").Contains("YeniArayuzPilot")));
    }
}
