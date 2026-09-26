using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.1a — generic definition endpoints (<c>Api/Tanim/</c>) on the REAL Web pipeline (cookie + CSRF + pilot,
/// <c>racar_app</c>). INDEPENDENT ORACLE: every expected value (normalized code, row count, order) is written from
/// the hand-built scenario, never computed by the service. Users get random passwords at run time.
/// </summary>
[Collection("web")]
public sealed partial class UiTanimTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    private enum Who { Admin, OperatorA, Muhasebe }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
    }

    private static string Random(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    private async Task<Env> SetUpAsync()
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = Random("f111a"), Password = WebFixture.RandomPassword(),
            Users = Enum.GetValues<Who>().ToDictionary(k => k, _ => Random("u")),
        };
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = e.TenantId, Code = e.Code, Name = e.Code, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (who, name) in e.Users)
            {
                var (role, branch) = who switch
                {
                    Who.Admin => (UserRole.Admin, (string?)null),
                    Who.OperatorA => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = e.TenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, e.Password);
                db.Users.Add(u);
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(e.TenantId, true);
        return e;
    }

    private async Task WriteAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    private sealed record Session(HttpClient C, string Xsrf);

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
            if (v.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(v[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Session> LoginAsync(Env e, Who who)
    {
        var c = fx.Web.Client();
        var before = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }) };
        req.Headers.Add("X-XSRF-TOKEN", before);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"login failed: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is HttpContent content) req.Content = content;
        else if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {(int)expected}, got {(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Expected {(int)status}, got {(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement.Clone();
        if (code is not null) Assert.Equal(code, root.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(field, out _), $"errors[{field}] missing: {text}");
        return root;
    }

    private static List<string> Values(JsonElement array, string field)
        => array.EnumerateArray().Select(x => x.GetProperty(field).GetString()!).ToList();}
