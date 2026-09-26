using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;
using RentACar.Web.Api.Kira;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.3 adversarial (SPA) düzeltmeleri — GERÇEK Web boru hattında (cookie + CSRF + pilot):
/// <list type="bullet">
/// <item><b>F2 (HIGH):</b> <c>PUT /kiralar/{id}</c> iyimser eşzamanlılık. Senaryo R2: oturum 1 kirayı açık tutar,
/// oturum 2 drop ücretini 300 yapar; oturum 1 bayat anlık görüntüyle yalnız açıklamayı kaydederse 409
/// <c>cakisma</c> ve HİÇBİR ŞEY yazılmaz (drop ücreti 300 kalır). İşlemler (teslim) de sürümü değiştirir.</item>
/// <item><b>F4 (MEDIUM):</b> kirada (müşteri ya da kayıtlı 2. sürücü) kullanılan cari silinemez.</item>
/// </list>
/// Beklenen değerler elle kurulur (300 drop, açıklama metni); servis kodundan türetilmez.
/// </summary>
[Collection("web")]
public sealed class UiKiraPanelSurumTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Rental = V1 + "/kiralar";

    private sealed record Ortam(Guid TenantId, string Kod, string Kullanici, string Sifre, Guid MusteriId);
    private sealed record Oturum(HttpClient C, string Xsrf);

    private static readonly string[] UpdateFields = typeof(KiraGuncelleIstegi)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToArray();

    private static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> SetUpEnvironmentAsync()
    {
        var tenantId = Guid.NewGuid();
        var code = "f43" + Guid.NewGuid().ToString("N")[..10];
        var user = "u" + Guid.NewGuid().ToString("N")[..10];
        var password = WebFixture.RandomPassword();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = code, Name = code, IsActive = true });
            var u = new User { TenantId = tenantId, UserName = user, DisplayName = user, Rol = UserRole.Admin, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, password);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenantId, true);
        var customer = new Customer { Tip = CustomerType.Bireysel, Ad = "Deniz", Soyad = "Yılmaz" };
        await WriteDataAsync(tenantId, db => db.Customers.Add(customer));
        return new Ortam(tenantId, code, user, password, customer.Id);
    }

    private async Task WriteDataAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> VehicleAsync(Ortam o)
    {
        var v = new Vehicle { Plaka = "34 F43 " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = "SubeA", Durum = VehicleStatus.Musait, Km = 1000 };
        await WriteDataAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Oturum> LoginAsync(Ortam o)
    {
        var c = fx.Web.Client();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanici, sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", CookieValue(x, "XSRF-TOKEN")!);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Beklenen {(int)expected}, gelen {(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(code, root.GetProperty("kod").GetString());
        return root;
    }

    private async Task<Guid> OpenRentalAsync(Oturum s, Ortam o, DateTimeOffset start)
    {
        var j = await Json(await Gonder(s, HttpMethod.Post, Rental, new
        {
            musteriId = o.MusteriId, vehicleId = await VehicleAsync(o), basTar = start, bitTar = start.AddDays(3),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi = "SubeA", donusOfisi = "SubeA",
        }), HttpStatusCode.Created);
        return j.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ReadRentalAsync(Oturum s, Guid id)
        => (await Json(await s.C.GetAsync($"{Rental}/{id}"))).GetProperty("kira");

    /// <summary>Detaydaki sözleşmeden TAM PUT gövdesi (SPA'nın yaptığı gibi; sürüm dahil).</summary>
    private static JsonObject Body(JsonElement rental)
    {
        var g = new JsonObject();
        foreach (var alan in UpdateFields) g[alan] = JsonNode.Parse(rental.GetProperty(alan).GetRawText());
        return g;
    }

    [Fact]
    public async Task F2_R2_Bayat_surumlu_PUT_409_cakisma_hicbir_sey_yazilmaz_islem_surumu_degistirir()
    {
        var o = await SetUpEnvironmentAsync();
        var s1 = await LoginAsync(o);
        var s2 = await LoginAsync(o);
        var id = await OpenRentalAsync(s1, o, Now().AddHours(10));

        var stale = await ReadRentalAsync(s1, id); // sekme 1 kirayı açık tutuyor
        Assert.False(string.IsNullOrWhiteSpace(stale.GetProperty("surum").GetString()));

        // Oturum 2: drop ücreti 300 (güncel sürümle) → 200.
        var g2 = Body(await ReadRentalAsync(s2, id));
        g2["dropUcreti"] = 300m;
        var k2 = await Json(await Gonder(s2, HttpMethod.Put, $"{Rental}/{id}", g2));
        Assert.Equal(300m, k2.GetProperty("dropUcreti").GetDecimal());
        var after2 = await ReadRentalAsync(s1, id);
        Assert.NotEqual(stale.GetProperty("surum").GetString(), after2.GetProperty("surum").GetString());

        // Oturum 1: bayat anlık görüntüyle YALNIZ açıklama → 409 cakisma; hiçbir alan yazılmaz.
        var g1 = Body(stale);
        g1["aciklama"] = "bayat sekme";
        var p = await ExpectProblem(await Gonder(s1, HttpMethod.Put, $"{Rental}/{id}", g1), HttpStatusCode.Conflict, UiError.ConflictCode);
        Assert.Equal(ConcurrentModificationException.RentalMessage, p.GetProperty("detail").GetString());
        var db = await ReadRentalAsync(s1, id);
        Assert.Equal(300m, db.GetProperty("dropUcreti").GetDecimal());
        Assert.Equal(JsonValueKind.Null, db.GetProperty("aciklama").ValueKind);
        Assert.Equal(after2.GetProperty("genelToplam").GetDecimal(), db.GetProperty("genelToplam").GetDecimal());
        Assert.Equal(after2.GetProperty("surum").GetString(), db.GetProperty("surum").GetString());

        // Güncel sürümle yeniden → yazılır; oturum 2'nin drop ücreti KORUNUR.
        var g3 = Body(db);
        g3["aciklama"] = "güncel";
        var k3 = await Json(await Gonder(s1, HttpMethod.Put, $"{Rental}/{id}", g3));
        Assert.Equal("güncel", k3.GetProperty("aciklama").GetString());
        Assert.Equal(300m, k3.GetProperty("dropUcreti").GetDecimal());

        // İşlem (teslim) sürümü değiştirir: işlem öncesi sürümle PUT 409.
        var t = await Json(await Gonder(s1, HttpMethod.Post, $"{Rental}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        Assert.NotEqual(k3.GetProperty("surum").GetString(), t.GetProperty("surum").GetString());
        var g4 = Body(k3);
        await ExpectProblem(await Gonder(s1, HttpMethod.Put, $"{Rental}/{id}", g4), HttpStatusCode.Conflict, UiError.ConflictCode);

        // Sürümsüz tam değiştirme yapılamaz → 400 errors[surum].
        g4["surum"] = null;
        var e = await ExpectProblem(await Gonder(s1, HttpMethod.Put, $"{Rental}/{id}", g4), HttpStatusCode.BadRequest, UiError.Validation);
        Assert.True(e.GetProperty("errors").TryGetProperty("surum", out _), e.ToString());
    }

    [Fact]
    public async Task F4_Kirada_musteri_ya_da_ikinci_surucu_olarak_kullanilan_cari_silinemez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o);
        var second = new Customer { Tip = CustomerType.Bireysel, Ad = "İkinci", Soyad = "Sürücü" };
        var free = new Customer { Tip = CustomerType.Bireysel, Ad = "Serbest", Soyad = "Cari" };
        await WriteDataAsync(o.TenantId, db => { db.Customers.Add(second); db.Customers.Add(free); });
        var id = await OpenRentalAsync(s, o, Now().AddHours(12));
        var g = Body(await ReadRentalAsync(s, id));
        g["ikinciSurucuId"] = second.Id;
        await Json(await Gonder(s, HttpMethod.Put, $"{Rental}/{id}", g));

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId);
        var customers = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => customers.DeleteAsync(second.Id));
        Assert.Contains("kira sözleşmesinde", ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => customers.DeleteAsync(o.MusteriId));
        Assert.True(await customers.DeleteAsync(free.Id)); // kirada kullanılmayan cari silinir

        var rental = await ReadRentalAsync(s, id);
        Assert.Equal(second.Id, rental.GetProperty("ikinciSurucuId").GetGuid());
    }
}
