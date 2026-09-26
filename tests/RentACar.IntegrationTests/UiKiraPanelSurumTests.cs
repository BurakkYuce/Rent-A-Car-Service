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
    private const string Kira = V1 + "/kiralar";

    private sealed record Ortam(Guid TenantId, string Kod, string Kullanici, string Sifre, Guid MusteriId);
    private sealed record Oturum(HttpClient C, string Xsrf);

    private static readonly string[] GuncelleAlanlari = typeof(KiraGuncelleIstegi)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToArray();

    private static DateTimeOffset Simdi() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> OrtamKurAsync()
    {
        var tenantId = Guid.NewGuid();
        var kod = "f43" + Guid.NewGuid().ToString("N")[..10];
        var kullanici = "u" + Guid.NewGuid().ToString("N")[..10];
        var sifre = WebFixture.RastgeleParola();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = kod, Name = kod, IsActive = true });
            var u = new User { TenantId = tenantId, UserName = kullanici, DisplayName = kullanici, Rol = UserRole.Admin, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, sifre);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(tenantId, true);
        var musteri = new Customer { Tip = CustomerType.Bireysel, Ad = "Deniz", Soyad = "Yılmaz" };
        await VeriYazAsync(tenantId, db => db.Customers.Add(musteri));
        return new Ortam(tenantId, kod, kullanici, sifre, musteri.Id);
    }

    private async Task VeriYazAsync(Guid tenantId, Action<AppDbContext> yaz)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        yaz(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AracAsync(Ortam o)
    {
        var v = new Vehicle { Plaka = "34 F43 " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = "SubeA", Durum = VehicleStatus.Musait, Km = 1000 };
        await VeriYazAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private static string? Cerez(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Oturum> GirisAsync(Ortam o)
    {
        var c = fx.Web.Istemci();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanici, sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", Cerez(x, "XSRF-TOKEN")!);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, Cerez(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? govde = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"Beklenen {(int)beklenen}, gelen {(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        var kok = JsonDocument.Parse(metin).RootElement.Clone();
        Assert.Equal(kod, kok.GetProperty("kod").GetString());
        return kok;
    }

    private async Task<Guid> KiraAcAsync(Oturum s, Ortam o, DateTimeOffset bas)
    {
        var j = await Json(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = await AracAsync(o), basTar = bas, bitTar = bas.AddDays(3),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi = "SubeA", donusOfisi = "SubeA",
        }), HttpStatusCode.Created);
        return j.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> KiraOkuAsync(Oturum s, Guid id)
        => (await Json(await s.C.GetAsync($"{Kira}/{id}"))).GetProperty("kira");

    /// <summary>Detaydaki sözleşmeden TAM PUT gövdesi (SPA'nın yaptığı gibi; sürüm dahil).</summary>
    private static JsonObject Govde(JsonElement kira)
    {
        var g = new JsonObject();
        foreach (var alan in GuncelleAlanlari) g[alan] = JsonNode.Parse(kira.GetProperty(alan).GetRawText());
        return g;
    }

    [Fact]
    public async Task F2_R2_Bayat_surumlu_PUT_409_cakisma_hicbir_sey_yazilmaz_islem_surumu_degistirir()
    {
        var o = await OrtamKurAsync();
        var s1 = await GirisAsync(o);
        var s2 = await GirisAsync(o);
        var id = await KiraAcAsync(s1, o, Simdi().AddHours(10));

        var bayat = await KiraOkuAsync(s1, id); // sekme 1 kirayı açık tutuyor
        Assert.False(string.IsNullOrWhiteSpace(bayat.GetProperty("surum").GetString()));

        // Oturum 2: drop ücreti 300 (güncel sürümle) → 200.
        var g2 = Govde(await KiraOkuAsync(s2, id));
        g2["dropUcreti"] = 300m;
        var k2 = await Json(await Gonder(s2, HttpMethod.Put, $"{Kira}/{id}", g2));
        Assert.Equal(300m, k2.GetProperty("dropUcreti").GetDecimal());
        var sonra2 = await KiraOkuAsync(s1, id);
        Assert.NotEqual(bayat.GetProperty("surum").GetString(), sonra2.GetProperty("surum").GetString());

        // Oturum 1: bayat anlık görüntüyle YALNIZ açıklama → 409 cakisma; hiçbir alan yazılmaz.
        var g1 = Govde(bayat);
        g1["aciklama"] = "bayat sekme";
        var p = await ProblemBekle(await Gonder(s1, HttpMethod.Put, $"{Kira}/{id}", g1), HttpStatusCode.Conflict, UiHata.Cakisma);
        Assert.Equal(ConcurrentModificationException.RentalMessage, p.GetProperty("detail").GetString());
        var db = await KiraOkuAsync(s1, id);
        Assert.Equal(300m, db.GetProperty("dropUcreti").GetDecimal());
        Assert.Equal(JsonValueKind.Null, db.GetProperty("aciklama").ValueKind);
        Assert.Equal(sonra2.GetProperty("genelToplam").GetDecimal(), db.GetProperty("genelToplam").GetDecimal());
        Assert.Equal(sonra2.GetProperty("surum").GetString(), db.GetProperty("surum").GetString());

        // Güncel sürümle yeniden → yazılır; oturum 2'nin drop ücreti KORUNUR.
        var g3 = Govde(db);
        g3["aciklama"] = "güncel";
        var k3 = await Json(await Gonder(s1, HttpMethod.Put, $"{Kira}/{id}", g3));
        Assert.Equal("güncel", k3.GetProperty("aciklama").GetString());
        Assert.Equal(300m, k3.GetProperty("dropUcreti").GetDecimal());

        // İşlem (teslim) sürümü değiştirir: işlem öncesi sürümle PUT 409.
        var t = await Json(await Gonder(s1, HttpMethod.Post, $"{Kira}/{id}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        Assert.NotEqual(k3.GetProperty("surum").GetString(), t.GetProperty("surum").GetString());
        var g4 = Govde(k3);
        await ProblemBekle(await Gonder(s1, HttpMethod.Put, $"{Kira}/{id}", g4), HttpStatusCode.Conflict, UiHata.Cakisma);

        // Sürümsüz tam değiştirme yapılamaz → 400 errors[surum].
        g4["surum"] = null;
        var e = await ProblemBekle(await Gonder(s1, HttpMethod.Put, $"{Kira}/{id}", g4), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.True(e.GetProperty("errors").TryGetProperty("surum", out _), e.ToString());
    }

    [Fact]
    public async Task F4_Kirada_musteri_ya_da_ikinci_surucu_olarak_kullanilan_cari_silinemez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o);
        var ikinci = new Customer { Tip = CustomerType.Bireysel, Ad = "İkinci", Soyad = "Sürücü" };
        var serbest = new Customer { Tip = CustomerType.Bireysel, Ad = "Serbest", Soyad = "Cari" };
        await VeriYazAsync(o.TenantId, db => { db.Customers.Add(ikinci); db.Customers.Add(serbest); });
        var id = await KiraAcAsync(s, o, Simdi().AddHours(12));
        var g = Govde(await KiraOkuAsync(s, id));
        g["ikinciSurucuId"] = ikinci.Id;
        await Json(await Gonder(s, HttpMethod.Put, $"{Kira}/{id}", g));

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId);
        var cariler = scope.ServiceProvider.GetRequiredService<CustomerService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => cariler.DeleteAsync(ikinci.Id));
        Assert.Contains("kira sözleşmesinde", ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => cariler.DeleteAsync(o.MusteriId));
        Assert.True(await cariler.DeleteAsync(serbest.Id)); // kirada kullanılmayan cari silinir

        var kira = await KiraOkuAsync(s, id);
        Assert.Equal(ikinci.Id, kira.GetProperty("ikinciSurucuId").GetGuid());
    }
}
