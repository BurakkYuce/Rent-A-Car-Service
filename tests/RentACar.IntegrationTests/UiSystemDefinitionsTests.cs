using System.Net;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.1b — tanım uçları (ikinci yarı) GERÇEK Web boru hattında (cookie + CSRF + pilot, <c>racar_app</c>).
/// BAĞIMSIZ ORACLE: beklenen değerler elle kurulan senaryodan (girilen kod/ad/tutar) yazılır, servis kodundan
/// türetilmez. Kullanıcılar çalışma anında rastgele parolayla üretilir.
/// </summary>
[Collection("web")]
public sealed partial class UiSystemDefinitionsTests(WebFixture fx)
{
    private readonly SystemApiTestKit _kit = new(fx);

    private static string Code(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();

    private static string Surum(JsonElement e) => e.GetProperty("surum").GetString()!;

    private static List<string> Codes(JsonElement page)
        => page.GetProperty("kayitlar").EnumerateArray().Select(x => x.GetProperty("kod").GetString()!).ToList();

    [Fact]
    public async Task Insurance_crud_uniqueness_version_and_permissions()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/sigorta-sirketleri";

        var created = await Json(await Send(admin, HttpMethod.Post, path, new { kod = "axa", ad = "AXA Sigorta", telefon = "02120000000" }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("AXA", created.GetProperty("kod").GetString()); // kod büyük harfe normalize
        Assert.Equal("AXA Sigorta", created.GetProperty("ad").GetString());
        Assert.False(string.IsNullOrEmpty(Surum(created)));

        // benzersizlik → 400 errors[kod]
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "AXA", ad = "Başka" }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        // zorunlu ad → errors[ad]
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "ALZ", ad = "" }), HttpStatusCode.BadRequest, "dogrulama", "ad");

        // PUT: surum eksik 400, doğru surum 200, bayat surum 409 cakisma
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "AXA", ad = "AXA 2" }), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var s1 = Surum(created);
        var updated = await Json(await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "AXA", ad = "AXA Hayat", aktif = false, surum = s1 }));
        Assert.Equal("AXA Hayat", updated.GetProperty("ad").GetString());
        Assert.False(updated.GetProperty("aktif").GetBoolean());
        Assert.NotEqual(s1, Surum(updated));
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "AXA", ad = "Bayat", surum = s1 }), HttpStatusCode.Conflict, "cakisma");

        // liste: arama + aktif süzgeci
        await Json(await Send(admin, HttpMethod.Post, path, new { kod = "ANA", ad = "Anadolu" }), HttpStatusCode.Created);
        Assert.Equal(["ANA", "AXA"], Codes(await Json(await admin.C.GetAsync($"{path}?sirala=kod"))));
        Assert.Equal(["ANA"], Codes(await Json(await admin.C.GetAsync($"{path}?aktif=true"))));
        Assert.Equal(["AXA"], Codes(await Json(await admin.C.GetAsync($"{path}?ara=hayat"))));

        // izinsiz rol (Muhasebe: OperationsWrite yok) → 403
        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await acc.C.GetAsync(path), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(acc, HttpMethod.Post, path, new { kod = "X", ad = "Y" }), HttpStatusCode.Forbidden, "yetki_yok");

        // başka kiracı → 404 (RLS)
        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await otherAdmin.C.GetAsync($"{path}/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Put, $"{path}/{id}", new { kod = "AXA", ad = "Ele geçir", surum = Surum(updated) }), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Delete, $"{path}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Empty(Codes(await Json(await otherAdmin.C.GetAsync(path))));

        // silme
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{path}/{id}")).StatusCode);
        await Problem(await admin.C.GetAsync($"{path}/{id}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Kdv_rate_crud_range_and_version()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/kdv-oranlari";

        var created = await Json(await Send(admin, HttpMethod.Post, path, new { kod = "k20", ad = "Genel", oran = 0.20m }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(0.20m, created.GetProperty("oran").GetDecimal());

        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "K99", ad = "Hatalı", oran = 20m }), HttpStatusCode.BadRequest, "dogrulama", "oran");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "K00", ad = "Oransız" }), HttpStatusCode.BadRequest, "dogrulama", "oran");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "K20", ad = "Tekrar", oran = 0.1m }), HttpStatusCode.BadRequest, "dogrulama", "kod");

        var s1 = Surum(created);
        var upd = await Json(await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "K20", ad = "Genel", oran = 0.18m, surum = s1 }));
        Assert.Equal(0.18m, upd.GetProperty("oran").GetDecimal());
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "K20", ad = "Genel", oran = 0.10m, surum = s1 }), HttpStatusCode.Conflict, "cakisma");
        // bayat PUT hiçbir şey yazmadı
        Assert.Equal(0.18m, (await Json(await admin.C.GetAsync($"{path}/{id}"))).GetProperty("oran").GetDecimal());

        var manager = await _kit.LoginAsync(e, Who.Accounting);
        await Problem(await manager.C.GetAsync($"{path}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
    }

    [Fact]
    public async Task Penalty_type_crud_amount_limits_and_other_tenant()
    {
        var e = await _kit.SetupAsync();
        var op = await _kit.LoginAsync(e, Who.OperatorA); // OperationsWrite yeter (Blazor paritesi)
        const string path = V1 + "/ceza-turleri";

        var created = await Json(await Send(op, HttpMethod.Post, path, new { kod = "hiz", ad = "Hız ihlali", varsayilanTutar = 1250.50m }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(1250.50m, created.GetProperty("varsayilanTutar").GetDecimal());

        await Problem(await Send(op, HttpMethod.Post, path, new { kod = "NEG", ad = "Negatif", varsayilanTutar = -1m }), HttpStatusCode.BadRequest, "dogrulama", "varsayilanTutar");
        await Problem(await Send(op, HttpMethod.Post, path, new { kod = "BUY", ad = "Taşma", varsayilanTutar = 1e17m }), HttpStatusCode.BadRequest, "dogrulama", "varsayilanTutar");
        await Problem(await Send(op, HttpMethod.Post, path, new { kod = "UZN", ad = new string('x', 129) }), HttpStatusCode.BadRequest, "dogrulama", "ad");

        var upd = await Json(await Send(op, HttpMethod.Put, $"{path}/{id}", new { kod = "HIZ", ad = "Hız", varsayilanTutar = (decimal?)null, surum = Surum(created) }));
        Assert.Equal(JsonValueKind.Null, upd.GetProperty("varsayilanTutar").ValueKind);

        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await otherAdmin.C.GetAsync($"{path}/{id}"), HttpStatusCode.NotFound, null);
    }
}
