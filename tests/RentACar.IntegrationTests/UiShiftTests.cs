using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Personnel;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F10.3 — <c>/api/ui/v1/vardiyalar</c> (personel çalışma yazma uçları) GERÇEK Web boru hattında (cookie + CSRF +
/// pilot, <c>racar_app</c>).
/// BAĞIMSIZ ORACLE: süreler elle hesaplanır ("08:00–18:30 = 10 sa 30 dk = 630 dk", "22:00–06:00 = 2 sa + 6 sa =
/// 480 dk"); <c>VardiyaZaman</c>'dan türetilmez. Kapsam vardiyanın ŞUBESİNE göre: SubeA operatörü SubeB vardiyasına
/// 403 alır (sürüm ya da alan hatasından ÖNCE), başka kiracının vardiyası 404.
/// </summary>
[Collection("web")]
public sealed class UiShiftTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Shifts = V1 + "/vardiyalar";

    private enum Who { Admin, Accounting, OperatorA }

    private sealed class Env
    {
        public required Guid TenantId { get; init; }
        public required string Code { get; init; }
        public required string Password { get; init; }
        public required Dictionary<Who, string> Users { get; init; }
        public Guid StaffA { get; set; }
        public Guid StaffB { get; set; }
    }

    private sealed record Session(HttpClient C, string Xsrf);

    private static string Random(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Test günü: takvimden bağımsız (sabit tarih yok).</summary>
    private static readonly DateOnly Day = DateOnly.FromDateTime(TestZaman.GunSonra(3).UtcDateTime);

    private async Task<Env> SetupAsync()
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = Random("f103"), Password = WebFixture.RastgeleParola(),
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
        await fx.PilotYapAsync(e.TenantId, true);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId);
        var sp = scope.ServiceProvider;
        var branches = sp.GetRequiredService<BranchService>();
        await branches.CreateAsync(new BranchInput { Kod = "SA", Ad = "SubeA" });
        await branches.CreateAsync(new BranchInput { Kod = "SB", Ad = "SubeB" });
        var staff = sp.GetRequiredService<PersonnelService>();
        e.StaffA = await staff.CreateAsync(new PersonelInput { Kod = "P1", Ad = "Ali", Soyad = "Veli", Sube = "SubeA" });
        e.StaffB = await staff.CreateAsync(new PersonelInput { Kod = "P2", Ad = "Ayşe", Soyad = "Kara", Sube = "SubeB" });
        return e;
    }

    private static string? Cookie(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
            if (v.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(v[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Session> LoginAsync(Env e, Who who)
    {
        var c = fx.Web.Istemci();
        var first = Cookie(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        { Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }) };
        req.Headers.Add("X-XSRF-TOKEN", first);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, Cookie(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task Problem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var root = await Json(r, status);
        if (code is not null) Assert.Equal(code, root.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(root.GetProperty("errors").TryGetProperty(field, out _), $"errors[{field}] yok: {root}");
    }

    private static object Body(Guid staff, DateOnly day, string from, string to, string? branch, string? note = null,
        string? surum = null)
        => new
        {
            personelId = staff, tarih = day.ToString("yyyy-MM-dd"), baslangicSaat = from, bitisSaat = to,
            sube = branch, aciklama = note, surum,
        };

    private static async Task<JsonElement> CreateAsync(Session s, object body)
        => await Json(await Send(s, HttpMethod.Post, Shifts, body), HttpStatusCode.Created);

    [Fact]
    public async Task Create_update_delete_round_trip_with_version_and_hand_computed_durations()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);

        var created = await CreateAsync(s, Body(e.StaffA, Day, "08:00", "18:30", " SubeA ", "  destek  "));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(630, created.GetProperty("sureDk").GetInt32());          // elle: 10 sa 30 dk
        Assert.Equal("08:00", created.GetProperty("baslangicSaat").GetString());
        Assert.Equal("18:30", created.GetProperty("bitisSaat").GetString());
        Assert.Equal("SubeA", created.GetProperty("sube").GetString());       // kırpılır
        Assert.Equal("destek", created.GetProperty("aciklama").GetString());
        Assert.Equal("Ali Veli", created.GetProperty("personelAd").GetString());
        var v1 = created.GetProperty("surum").GetString();
        Assert.False(string.IsNullOrWhiteSpace(v1));

        var got = await Json(await Send(s, HttpMethod.Get, $"{Shifts}/{id}"), HttpStatusCode.OK);
        Assert.Equal(v1, got.GetProperty("surum").GetString());
        Assert.Equal(Day.ToString("yyyy-MM-dd"), got.GetProperty("tarih").GetString());

        // Gece vardiyası: 22:00 → ertesi gün 06:00 = 2 sa + 6 sa = 480 dk.
        var put = await Json(await Send(s, HttpMethod.Put, $"{Shifts}/{id}",
            Body(e.StaffA, Day, "22:00", "06:00", "SubeA", null, v1)), HttpStatusCode.OK);
        Assert.Equal(480, put.GetProperty("sureDk").GetInt32());
        Assert.Equal("22:00-06:00 (+1)", put.GetProperty("aralik").GetString());
        Assert.Equal(JsonValueKind.Null, put.GetProperty("aciklama").ValueKind);
        var v2 = put.GetProperty("surum").GetString();
        Assert.NotEqual(v1, v2);

        // Bayat sürüm → 409 cakisma, kayıt değişmez.
        await Problem(await Send(s, HttpMethod.Put, $"{Shifts}/{id}",
            Body(e.StaffA, Day, "09:00", "10:00", "SubeA", null, v1)), HttpStatusCode.Conflict, "cakisma");
        // Sürümsüz tam değiştirme reddedilir.
        await Problem(await Send(s, HttpMethod.Put, $"{Shifts}/{id}",
            Body(e.StaffA, Day, "09:00", "10:00", "SubeA")), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var after = await Json(await Send(s, HttpMethod.Get, $"{Shifts}/{id}"), HttpStatusCode.OK);
        Assert.Equal("22:00", after.GetProperty("baslangicSaat").GetString());
        Assert.Equal(v2, after.GetProperty("surum").GetString());

        // Rapor ekranı (aynı servis) yazılanı görür: toplam 480 dk.
        var report = await Json(await Send(s, HttpMethod.Get,
            $"{V1}/raporlar/personel-calisma?bas={Day:yyyy-MM-dd}&bit={Day:yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(1, report.GetProperty("toplamVardiya").GetInt32());
        Assert.Equal(480, report.GetProperty("toplamDk").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(s, HttpMethod.Delete, $"{Shifts}/{id}")).StatusCode);
        await Problem(await Send(s, HttpMethod.Get, $"{Shifts}/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(s, HttpMethod.Delete, $"{Shifts}/{id}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Validation_errors_are_field_mapped_and_limits_hold()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);

        async Task Bad(object body, string field)
            => await Problem(await Send(s, HttpMethod.Post, Shifts, body), HttpStatusCode.BadRequest, "dogrulama", field);

        await Bad(new { tarih = Day.ToString("yyyy-MM-dd"), baslangicSaat = "08:00", bitisSaat = "12:00" }, "personelId");
        await Bad(Body(Guid.NewGuid(), Day, "08:00", "12:00", null), "personelId");                  // yok
        await Bad(new { personelId = e.StaffA, baslangicSaat = "08:00", bitisSaat = "12:00" }, "tarih");
        await Bad(Body(e.StaffA, new DateOnly(1800, 1, 1), "08:00", "12:00", null), "tarih");
        await Bad(Body(e.StaffA, Day, "25:00", "12:00", null), "baslangicSaat");
        await Bad(Body(e.StaffA, Day, "08:00", "", null), "bitisSaat");
        await Bad(Body(e.StaffA, Day, "09:00", "09:00", null), "bitisSaat");                         // sıfır süre
        await Bad(Body(e.StaffA, Day, "08:00", "12:00", "Olmayan Sube"), "sube");
        await Bad(Body(e.StaffA, Day, "08:00", "12:00", new string('x', 129)), "sube");
        await Bad(Body(e.StaffA, Day, "08:00", "12:00", null, new string('x', 513)), "aciklama");

        // Bölünmüş mesai serbest (08–12 + 12–18 çakışmaz: yarı açık aralık); kesişen aralık reddedilir.
        await CreateAsync(s, Body(e.StaffA, Day, "08:00", "12:00", "SubeA"));
        await CreateAsync(s, Body(e.StaffA, Day, "12:00", "18:00", "SubeA"));
        await Bad(Body(e.StaffA, Day, "11:00", "13:00", "SubeA"), "baslangicSaat");
        // Gece vardiyası ertesi sabahla çakışır (22:00–06:00 ↔ ertesi gün 05:00–09:00).
        await CreateAsync(s, Body(e.StaffB, Day, "22:00", "06:00", "SubeB"));
        await Bad(Body(e.StaffB, Day.AddDays(1), "05:00", "09:00", "SubeB"), "baslangicSaat");
        await CreateAsync(s, Body(e.StaffB, Day.AddDays(1), "06:00", "09:00", "SubeB"));
    }

    [Fact]
    public async Task Branch_scope_permission_and_tenant_isolation()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var op = await LoginAsync(e, Who.OperatorA);
        var acc = await LoginAsync(e, Who.Accounting);

        var inB = await CreateAsync(admin, Body(e.StaffB, Day, "08:00", "16:00", "SubeB"));
        var idB = inB.GetProperty("id").GetGuid();
        var vB = inB.GetProperty("surum").GetString();

        // Operatör yalnız kendi şubesine ve kadrosu kendi şubesinde olan personele yazar. 2026-09-25: kadrosu B'de olan
        // personele A'da bile yazamaz — çakışma kontrolü B'deki vardiya saatleri için kehanet olurdu (403, çakışmadan önce).
        await Problem(await Send(op, HttpMethod.Post, Shifts, Body(e.StaffB, Day, "09:00", "10:00", "SubeA")),
            HttpStatusCode.Forbidden, "yetki_yok");                                   // B'deki 08–16 ile çakışırdı
        await Problem(await Send(op, HttpMethod.Post, Shifts, Body(e.StaffB, Day.AddDays(1), "08:00", "16:00", "SubeA")),
            HttpStatusCode.Forbidden, "yetki_yok");                                   // çakışmıyor: aynı yanıt
        var own = await CreateAsync(op, Body(e.StaffA, Day.AddDays(1), "08:00", "16:00", "SubeA"));
        Assert.Equal("SubeA", own.GetProperty("sube").GetString());
        await Problem(await Send(op, HttpMethod.Post, Shifts, Body(e.StaffA, Day, "08:00", "16:00", "SubeB")),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Post, Shifts, Body(e.StaffA, Day, "08:00", "16:00", null)),
            HttpStatusCode.Forbidden, "yetki_yok");

        // Başka şubenin kaydı: 403 — sürümsüz ve geçersiz gövdede bile (kapsam, doğrulamadan ÖNCE).
        await Problem(await Send(op, HttpMethod.Get, $"{Shifts}/{idB}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Put, $"{Shifts}/{idB}", Body(e.StaffA, Day, "x", "y", null)),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Put, $"{Shifts}/{idB}",
            Body(e.StaffB, Day, "08:00", "12:00", "SubeA", null, vB)), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Delete, $"{Shifts}/{idB}"), HttpStatusCode.Forbidden, "yetki_yok");
        var still = await Json(await Send(admin, HttpMethod.Get, $"{Shifts}/{idB}"), HttpStatusCode.OK);
        Assert.Equal(vB, still.GetProperty("surum").GetString());   // hiçbir şey yazılmadı

        // Muhasebe: ViewReports var, OperationsWrite yok → okur, yazamaz.
        await Json(await Send(acc, HttpMethod.Get, $"{Shifts}/{idB}"), HttpStatusCode.OK);
        await Problem(await Send(acc, HttpMethod.Post, Shifts, Body(e.StaffA, Day.AddDays(2), "08:00", "16:00", "SubeA")),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(acc, HttpMethod.Delete, $"{Shifts}/{idB}"), HttpStatusCode.Forbidden, "yetki_yok");

        // Başka kiracı: kaydı yok sayılır (404), personeli bu kiracıda bulunamaz.
        var other = await SetupAsync();
        var otherAdmin = await LoginAsync(other, Who.Admin);
        await Problem(await Send(otherAdmin, HttpMethod.Get, $"{Shifts}/{idB}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Put, $"{Shifts}/{idB}",
            Body(other.StaffA, Day, "08:00", "12:00", "SubeA", null, vB)), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Delete, $"{Shifts}/{idB}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Post, Shifts, Body(e.StaffA, Day, "08:00", "12:00", "SubeA")),
            HttpStatusCode.BadRequest, "dogrulama", "personelId");
        var unchanged = await Json(await Send(admin, HttpMethod.Get, $"{Shifts}/{idB}"), HttpStatusCode.OK);
        Assert.Equal("08:00", unchanged.GetProperty("baslangicSaat").GetString());
    }
}
