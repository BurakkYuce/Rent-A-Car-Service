using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F12.1 — <c>/api/ui/v1/platform/*</c> on the REAL Web pipeline (cookie + CSRF + authorization + isolation).
/// INDEPENDENT ORACLE: expected status codes, <c>kod</c> values and field names are hand-written literals; tenants
/// and users are created per test with random codes/passwords (no fixed credentials in the repo).
/// </summary>
[Collection("web")]
public sealed partial class PlatformUiApiTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string P = V1 + "/platform";

    private sealed record Session(HttpClient C, string Xsrf);

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
            if (v.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(v[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static async Task<string> XsrfAsync(HttpClient c)
        => CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")
           ?? throw new Xunit.Sdk.XunitException("XSRF-TOKEN not issued");

    private static Task<HttpResponseMessage> Send(HttpClient c, string? xsrf, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is HttpContent content) req.Content = content;
        else if (body is not null) req.Content = JsonContent.Create(body);
        return c.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null)
        => Send(s.C, s.Xsrf, m, url, body);

    private async Task<Session> PlatformLoginAsync()
    {
        var c = fx.Web.Client();
        var before = await XsrfAsync(c);
        var r = await Send(c, before, HttpMethod.Post, P + "/oturum/giris",
            new { kullanici = fx.Platform.Kullanici, sifre = fx.Platform.Sifre });
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return new Session(c, CookieValue(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("no XSRF after login"));
    }

    private async Task<HttpResponseMessage> TenantLoginRawAsync(HttpClient c, TestKimlik k)
        => await Send(c, await XsrfAsync(c), HttpMethod.Post, V1 + "/oturum/giris",
            new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre });

    private async Task<Session> TenantLoginAsync(TestKimlik k)
    {
        var c = fx.Web.Client();
        var r = await TenantLoginRawAsync(c, k);
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"expected {(int)status}, got {(int)r.StatusCode}: {text}");
        Assert.Null(r.Headers.Location); // never a redirect
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(text).RootElement;
        if (code is not null) Assert.Equal(code, j.GetProperty("kod").GetString());
        if (field is not null) Assert.True(j.GetProperty("errors").TryGetProperty(field, out _), text);
    }

    private sealed record AuditRow(string EntityName, int Action, string? UserName, string? OldValues, string? NewValues);

    /// <summary>Tenant's audit rows (AuditLogs is FORCE-RLS: owner + tx-local tenant GUC, the documented read path).</summary>
    private async Task<List<AuditRow>> AuditRowsAsync(Guid tenantId)
    {
        await using var c = new NpgsqlConnection(fx.Pg.OwnerConnectionString);
        await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", c, tx))
        {
            set.Parameters.AddWithValue("t", tenantId.ToString());
            await set.ExecuteScalarAsync();
        }
        var rows = new List<AuditRow>();
        await using (var q = new NpgsqlCommand(
            "SELECT \"EntityName\", \"Action\", \"UserName\", \"OldValues\"::text, \"NewValues\"::text FROM \"AuditLogs\" " +
            "WHERE \"TenantId\" = @t AND \"UserName\" LIKE 'platform:%' ORDER BY \"TimestampUtc\"", c, tx))
        {
            q.Parameters.AddWithValue("t", tenantId);
            await using var rd = await q.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                rows.Add(new AuditRow(rd.GetString(0), rd.GetInt32(1), rd.IsDBNull(2) ? null : rd.GetString(2),
                    rd.IsDBNull(3) ? null : rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4)));
        }
        await tx.CommitAsync();
        return rows;
    }

    // ------------------------------------------------------------ authority domain separation

    [Fact]
    public async Task Anonymous_gets_401_json_without_redirect()
    {
        var c = fx.Web.Client();
        await ExpectProblem(await c.GetAsync(P + "/kiracilar"), HttpStatusCode.Unauthorized, "oturum_yok");
        await ExpectProblem(await c.GetAsync(P + "/oturum/ben"), HttpStatusCode.Unauthorized, "oturum_yok");
        await ExpectProblem(await c.GetAsync(P + "/ozet"), HttpStatusCode.Unauthorized, "oturum_yok");
    }

    [Fact]
    public async Task Tenant_admin_is_forbidden_on_every_platform_endpoint_and_changes_nothing()
    {
        var admin = await TenantLoginAsync(fx.PilotAdmin); // pilot firm, role Admin
        var id = fx.PilotCompanyId;
        foreach (var url in new[] { P + "/kiracilar", P + "/kiracilar/secim", P + "/ozet", P + "/oturum/ben",
                     P + $"/kiracilar/{id}", P + $"/kiracilar/{id}/logo", P + "/belgeler" })
            await ExpectProblem(await admin.C.GetAsync(url), HttpStatusCode.Forbidden, "yetki_yok");

        await ExpectProblem(await Send(admin, HttpMethod.Post, P + $"/kiracilar/{id}/durum", new { durum = "Pasif" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Send(admin, HttpMethod.Post, P + $"/kiracilar/{id}/yeni-arayuz-pilot", new { aktif = false }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Send(admin, HttpMethod.Post, P + "/kiracilar",
            new { kod = "x" + Guid.NewGuid().ToString("N")[..8], ad = "X", adminKullanici = "a", adminSifre = WebFixture.RandomPassword() }),
            HttpStatusCode.Forbidden, "yetki_yok");

        // Nothing happened: the tenant session still works on its own API (still active, still pilot).
        Assert.Equal(HttpStatusCode.OK, (await admin.C.GetAsync(V1 + "/oturum/ben")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.C.GetAsync(V1 + "/araclar?boyut=1")).StatusCode);
    }

    [Fact]
    public async Task Platform_session_cannot_use_tenant_api()
    {
        var s = await PlatformLoginAsync();
        await ExpectProblem(await s.C.GetAsync(V1 + "/araclar"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await s.C.GetAsync(V1 + "/kiralar"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await s.C.GetAsync(V1 + "/menu"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await s.C.GetAsync(V1 + "/oturum/ben"), HttpStatusCode.Unauthorized, "oturum_yok");
        // Prefix look-alike is NOT the platform area (segment boundary).
        await ExpectProblem(await s.C.GetAsync(V1 + "/platformx"), HttpStatusCode.Forbidden, "yetki_yok");
        // ...while its own area works.
        Assert.Equal(HttpStatusCode.OK, (await s.C.GetAsync(P + "/ozet")).StatusCode);
    }
}
