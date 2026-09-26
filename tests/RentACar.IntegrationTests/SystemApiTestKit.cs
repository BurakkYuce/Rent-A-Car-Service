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
/// F11.1b — sistem/web sitesi/tanım uç testlerinin ortak ortamı: GERÇEK Web boru hattı (cookie + CSRF + pilot,
/// <c>racar_app</c>). Her çağrı yeni bir firma kurar; kullanıcılar çalışma anında rastgele parolayla üretilir
/// (depoda kimlik bilgisi yok).
/// </summary>
public sealed class SystemApiTestKit(WebFixture fx)
{
    public const string V1 = "/api/ui/v1";

    public enum Who { Admin, Admin2, Manager, OperatorA, Accounting }

    public sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
        public required Dictionary<Who, Guid> UserIds { get; init; }
    }

    public sealed record Session(HttpClient C, string Xsrf);

    public WebFixture Fx => fx;

    public static string Random(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    public async Task<Env> SetupAsync(bool pilot = true)
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = Random("f11b"), Password = WebFixture.RandomPassword(),
            Users = Enum.GetValues<Who>().ToDictionary(k => k, _ => Random("u")),
            UserIds = [],
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
                    Who.Admin or Who.Admin2 => (UserRole.Admin, (string?)null),
                    Who.Manager => (UserRole.Yonetici, null),
                    Who.OperatorA => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = e.TenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, e.Password);
                db.Users.Add(u);
                e.UserIds[who] = u.Id;
            }
            await db.SaveChangesAsync();
        }
        if (pilot) await fx.MakePilotAsync(e.TenantId, true);
        return e;
    }

    /// <summary>Tenant bağlamında (racar_app, RLS) doğrudan veri yazar.</summary>
    public async Task WriteAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    /// <summary>Tenant bağlamında (racar_app, RLS) okur.</summary>
    public async Task<T> ReadAsync<T>(Guid tenantId, Func<AppDbContext, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await read(db);
    }

    public static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    public async Task<Session> LoginAsync(Env e, Who who, string? password = null)
    {
        var c = fx.Web.Client();
        var before = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = password ?? e.Password }) };
        req.Headers.Add("X-XSRF-TOKEN", before);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    public static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is HttpContent content) req.Content = content;
        else if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    public static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<JsonElement> Problem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement.Clone();
        if (code is not null) Assert.Equal(code, root.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(field, out _), $"errors[{field}] yok: {text}");
        return root;
    }
}
