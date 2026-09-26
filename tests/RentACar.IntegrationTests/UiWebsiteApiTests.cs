using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.1b — web sitesi yönetimi uçları (<c>/web-sitesi</c>, <c>/site-icerik</c>, <c>/blog-yonetim</c>,
/// <c>/gelen-talepler</c>) GERÇEK Web boru hattında. BAĞIMSIZ ORACLE: araçlar, talepler ve içerikler elle kurulur;
/// beklenen değerler (fiyat 1500, durumlar, sayılar) o senaryodan yazılır.
/// </summary>
[Collection("web")]
public sealed partial class UiWebsiteApiTests(WebFixture fx)
{
    private readonly SystemApiTestKit _kit = new(fx);

    /// <summary>Firma + web sitesi modülü (satın alma bayrağı) — İLK istekten önce yazılır (TenantStatusCache).</summary>
    private async Task<Env> SetupAsync(bool module = true)
    {
        var e = await _kit.SetupAsync();
        if (module)
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
            await using var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance);
            await db.Tenants.Where(t => t.Id == e.TenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.WebSitesiModulu, true));
        }
        return e;
    }

    private async Task<Guid> VehicleAsync(Env e, string branch = "SubeA", string tip = "Egea")
    {
        var v = new Vehicle
        {
            Plaka = "34W" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), Marka = "Fiat", Tip = tip, Grup = "C",
            Sube = branch, Durum = VehicleStatus.Musait, Km = 1000,
        };
        await _kit.WriteAsync(e.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    /// <summary>PNG imzalı (içerikten tür tespiti) sahte görsel.</summary>
    private static byte[] Png(int size = 64)
    {
        var b = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        return b;
    }

    private static MultipartFormDataContent Upload(byte[] content, string field, string name = "a.png")
    {
        var f = new ByteArrayContent(content);
        f.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { f, field, name } };
    }

    /// <summary>Uzantısı .png, içeriği düz metin — içerikten tür tespiti reddetmeli.</summary>
    private static byte[] FakePng() => Encoding.UTF8.GetBytes("bu bir resim degil <script>alert(1)</script>");

    /// <summary>Havuzdan tek kümeyle (beraber) taslak ilan açar.</summary>
    private static async Task<Guid> CreateListingAsync(Session s)
    {
        var pool = await Json(await Send(s, HttpMethod.Get, V1 + "/web-sitesi/havuz"));
        var sig = pool.EnumerateArray().First().GetProperty("imza").GetString();
        var created = await Json(await Send(s, HttpMethod.Post, V1 + "/web-sitesi/ilanlar",
            new { mod = "beraber", imzalar = new[] { sig } }), HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    private static async Task<string> ListingVersionAsync(Session s, Guid id)
        => (await Json(await Send(s, HttpMethod.Get, $"{V1}/web-sitesi/ilanlar/{id}"))).GetProperty("surum").GetString()!;

    // ------------------------------------------------------------------ ilanlar

    [Fact]
    public async Task Listing_wizard_price_features_photo_and_status()
    {
        var e = await SetupAsync();
        await VehicleAsync(e);
        await VehicleAsync(e); // aynı model: tek kümede iki araç
        var admin = await _kit.LoginAsync(e, Who.Admin);

        var pool = await Json(await Send(admin, HttpMethod.Get, V1 + "/web-sitesi/havuz"));
        Assert.Single(pool.EnumerateArray());
        Assert.Equal(2, pool[0].GetProperty("araclar").GetArrayLength());

        var id = await CreateListingAsync(admin);
        var detail = await Json(await Send(admin, HttpMethod.Get, $"{V1}/web-sitesi/ilanlar/{id}"));
        Assert.Equal("Taslak", detail.GetProperty("durum").GetString());
        Assert.Equal(2, detail.GetProperty("araclar").GetArrayLength());
        var v1 = detail.GetProperty("surum").GetString()!;

        // Havuz boşaldı (iki araç ilana bağlandı); özet elle kurulan senaryodan: 1 ilan, 0 yayında, 0 ilansız.
        Assert.Empty((await Json(await Send(admin, HttpMethod.Get, V1 + "/web-sitesi/havuz"))).EnumerateArray());
        var summary = await Json(await Send(admin, HttpMethod.Get, V1 + "/web-sitesi/ozet"));
        Assert.Equal(0, summary.GetProperty("ilansizAracSayisi").GetInt32());
        Assert.Equal(1, summary.GetProperty("ilanSayisi").GetInt32());

        // fiyat: sürüm zorunlu → 400 errors[surum]; doğru sürüm → 200; aynı (bayat) sürüm tekrar → 409 cakisma
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/fiyat",
            new { gunlukFiyat = 1500m, kdvDahil = true }), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var priced = await Json(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/fiyat",
            new { gunlukFiyat = 1500m, haftalikToplam = 9000m, kdvDahil = true, surum = v1 }));
        Assert.Equal(1500m, priced.GetProperty("ilan").GetProperty("gunlukFiyat").GetDecimal());
        Assert.Equal(9000m, priced.GetProperty("ilan").GetProperty("haftalikToplam").GetDecimal());
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/fiyat",
            new { gunlukFiyat = 1700m, kdvDahil = true, surum = v1 }), HttpStatusCode.Conflict, "cakisma");
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/fiyat",
            new { gunlukFiyat = 0m, kdvDahil = true, surum = await ListingVersionAsync(admin, id) }),
            HttpStatusCode.BadRequest, "dogrulama", "gunlukFiyat");
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/fiyat",
            new { gunlukFiyat = 1e12m, kdvDahil = true, surum = await ListingVersionAsync(admin, id) }),
            HttpStatusCode.BadRequest, "dogrulama", "gunlukFiyat");

        // özellikler fotoğrafsız → kaydedilir ama taslakta kalır (yayinda=false)
        var rows = new[] { new { etiket = "Vites", deger = "Otomatik", gorunur = true } };
        var feat = await Json(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/ozellikler",
            new { satirlar = rows, surum = await ListingVersionAsync(admin, id) }));
        Assert.False(feat.GetProperty("yayinda").GetBoolean());
        Assert.Equal("Taslak", feat.GetProperty("ilan").GetProperty("durum").GetString());
        Assert.Equal("Otomatik", feat.GetProperty("ilan").GetProperty("ozellikler")[0].GetProperty("deger").GetString());
        // özellik PUT'u da ilan sürümünü ilerletir: eski sürümle ikinci özellik yazımı 409
        await Problem(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/ozellikler",
            new { satirlar = rows, surum = feat.GetProperty("ilan").GetProperty("surum").GetString() + "0" }),
            HttpStatusCode.Conflict, "cakisma");

        // fotoğraf: .png uzantılı metin reddedilir (içerikten tespit), gerçek PNG kabul
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/web-sitesi/ilanlar/{id}/fotograflar", Upload(FakePng(), "foto")),
            HttpStatusCode.BadRequest, "dogrulama", "foto");
        var photos = await Json(await Send(admin, HttpMethod.Post, $"{V1}/web-sitesi/ilanlar/{id}/fotograflar",
            Upload(Png(), "foto")), HttpStatusCode.Created);
        Assert.Single(photos.EnumerateArray());

        feat = await Json(await Send(admin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/ozellikler",
            new { satirlar = rows, surum = await ListingVersionAsync(admin, id) }));
        Assert.True(feat.GetProperty("yayinda").GetBoolean());
        Assert.Equal("Yayinda", feat.GetProperty("ilan").GetProperty("durum").GetString());

        // durum: taslağa geri alınamaz (400 errors[durum]); pasif olur
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/web-sitesi/ilanlar/{id}/durum", new { durum = "Taslak" }),
            HttpStatusCode.BadRequest, "dogrulama", "durum");
        var passive = await Json(await Send(admin, HttpMethod.Post, $"{V1}/web-sitesi/ilanlar/{id}/durum", new { durum = "Pasif" }));
        Assert.Equal("Pasif", passive.GetProperty("durum").GetString());

        // fotoğraf sil: yanlış (araç, foto) çifti 404; doğrusu 200 ve liste boş
        var photo = photos[0];
        var (vehicleId, photoId) = (photo.GetProperty("aracId").GetGuid(), photo.GetProperty("fotoId").GetGuid());
        await Problem(await Send(admin, HttpMethod.Delete, $"{V1}/web-sitesi/ilanlar/{id}/fotograflar/{vehicleId}/{Guid.NewGuid()}"),
            HttpStatusCode.NotFound, null);
        Assert.Empty((await Json(await Send(admin, HttpMethod.Delete,
            $"{V1}/web-sitesi/ilanlar/{id}/fotograflar/{vehicleId}/{photoId}"))).EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{V1}/web-sitesi/ilanlar/{id}")).StatusCode);
        await Problem(await Send(admin, HttpMethod.Get, $"{V1}/web-sitesi/ilanlar/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal(2, (await Json(await Send(admin, HttpMethod.Get, V1 + "/web-sitesi/havuz")))[0].GetProperty("araclar").GetArrayLength());
    }

    [Fact]
    public async Task Listing_gates_permission_module_and_tenant()
    {
        var e = await SetupAsync();
        await VehicleAsync(e);
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var id = await CreateListingAsync(admin);

        // izinsiz rol (Muhasebe: OperationsWrite yok) → 403
        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await Send(acc, HttpMethod.Get, V1 + "/web-sitesi/ilanlar"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(acc, HttpMethod.Delete, $"{V1}/web-sitesi/ilanlar/{id}"), HttpStatusCode.Forbidden, "yetki_yok");

        // başka kiracı → 404 (okuma, yazma, foto)
        var other = await SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await Send(otherAdmin, HttpMethod.Get, $"{V1}/web-sitesi/ilanlar/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Put, $"{V1}/web-sitesi/ilanlar/{id}/fiyat",
            new { gunlukFiyat = 1m, kdvDahil = true, surum = "1" }), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Post, $"{V1}/web-sitesi/ilanlar/{id}/fotograflar", Upload(Png(), "foto")),
            HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Delete, $"{V1}/web-sitesi/ilanlar/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal(0, (await Json(await Send(otherAdmin, HttpMethod.Get, V1 + "/web-sitesi/ilanlar"))).GetProperty("toplam").GetInt32());

        // modülü olmayan firma → 404 (satın alma kapısı, Blazor'la aynı)
        var noModule = await SetupAsync(module: false);
        var nm = await _kit.LoginAsync(noModule, Who.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(nm, HttpMethod.Get, V1 + "/web-sitesi/ilanlar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(nm, HttpMethod.Get, V1 + "/site-icerik/sayfalar")).StatusCode);

        // operatör yalnız kendi şubesindeki aracı havuzda görür
        await VehicleAsync(e, branch: "SubeB", tip: "Clio");
        await VehicleAsync(e, branch: "SubeA", tip: "Corolla");
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        var pool = await Json(await Send(op, HttpMethod.Get, V1 + "/web-sitesi/havuz"));
        var plates = pool.EnumerateArray().SelectMany(k => k.GetProperty("araclar").EnumerateArray())
            .Select(v => v.GetProperty("sube").GetString()).ToList();
        Assert.Equal(["SubeA"], plates);
    }
}
