using System.Net;
using System.Text.Json;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

public sealed partial class UiTanimTests
{
    /// <summary>One definition under the generic contract: route + a body builder (kod, ad, aktif, surum).</summary>
    public sealed record DefinitionCase(string Path, Func<string, string, bool, string?, object> Body,
        string RawCode = " ab1 ", string Code = "AB1", string RawCode2 = "zz9", string Code2 = "ZZ9", int CodeMax = 32);

    private static object KodAd(string kod, string ad, bool aktif, string? surum) => new { kod, ad, aktif, surum };

    /// <summary>Table of definitions covered by the shared test (F11.1a first half).</summary>
    private static readonly Dictionary<string, DefinitionCase> Cases = new()
    {
        ["markalar"] = new("/markalar", KodAd),
        ["iptal-sebepleri"] = new("/iptal-sebepleri", KodAd),
        ["ulkeler"] = new("/ulkeler", KodAd),
        ["musteri-gruplari"] = new("/musteri-gruplari", KodAd),
        ["departmanlar"] = new("/departmanlar", KodAd),
        ["aksesuarlar"] = new("/aksesuarlar", (k, a, ak, s) => new { kod = k, ad = a, aciklama = "Açıklama", aktif = ak, surum = s }),
        ["bankalar"] = new("/bankalar", KodAd),
        ["dovizler"] = new("/dovizler", (k, a, ak, s) => new { kod = k, ad = a, sembol = "$", ulke = "ABD", aktif = ak, surum = s },
            RawCode: " usd ", Code: "USD", RawCode2: "zzz", Code2: "ZZZ", CodeMax: 3),
        ["ozel-kodlar"] = new("/ozel-kodlar", (k, a, ak, s) => new { kod = k, ad = a, aciklama = "Not", turu = "Sınıf", aktif = ak, surum = s }),
        ["gider-turleri"] = new("/gider-turleri", (k, a, ak, s) => new { kod = k, ad = a, tur = "Araç", aktif = ak, surum = s }),
        ["hesaplar"] = new("/hesaplar", (k, a, ak, s) => new { kod = k, ad = a, tur = "Kasa", doviz = "try", banka = "Ziraat", aktif = ak, surum = s }),
    };

    public static TheoryData<string> CaseKeys => new(Cases.Keys);

    [Theory]
    [MemberData(nameof(CaseKeys))]
    public async Task Definition_crud_uniqueness_version_permission_and_isolation(string key)
    {
        var c = Cases[key];
        var root = V1 + c.Path;
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);

        // Create: code normalized (trim + upper), active by default, version present, Location set.
        var createdResponse = await Send(op, HttpMethod.Post, root, c.Body(c.RawCode, "Birinci", true, null));
        var a = await Json(createdResponse, HttpStatusCode.Created);
        var id = a.GetProperty("id").GetGuid();
        Assert.Equal(c.Code, a.GetProperty("kod").GetString());
        Assert.True(a.GetProperty("aktif").GetBoolean());
        Assert.False(string.IsNullOrEmpty(a.GetProperty("surum").GetString()));
        Assert.EndsWith($"{c.Path}/{id}", createdResponse.Headers.Location!.ToString());
        var b = await Json(await Send(op, HttpMethod.Post, root, c.Body(c.RawCode2, "İkinci", false, null)), HttpStatusCode.Created);
        var id2 = b.GetProperty("id").GetGuid();

        // Uniqueness (tenant, code) and column limits → 400 with the field.
        await ExpectProblem(await Send(op, HttpMethod.Post, root, c.Body(c.Code, "Kopya", true, null)), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, c.Body("QQ1", "", true, null)), HttpStatusCode.BadRequest, "dogrulama", "ad");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, c.Body("QQ2", new string('a', 129), true, null)), HttpStatusCode.BadRequest, "dogrulama", "ad");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, c.Body(new string('K', c.CodeMax + 1), "Uzun", true, null)), HttpStatusCode.BadRequest, "dogrulama", "kod");

        // List: array, every row carries its version; order/filter/search.
        var list = await Json(await Send(op, HttpMethod.Get, root));
        Assert.Equal([c.Code, c.Code2], Values(list, "kod"));
        Assert.All(list.EnumerateArray(), r => Assert.False(string.IsNullOrEmpty(r.GetProperty("surum").GetString())));
        var single = await Json(await Send(op, HttpMethod.Get, $"{root}/{id}"));
        Assert.Equal(single.GetProperty("surum").GetString(), list[0].GetProperty("surum").GetString());
        Assert.Equal([c.Code2, c.Code], Values(await Json(await Send(op, HttpMethod.Get, root + "?sirala=-kod")), "kod"));
        Assert.Equal([c.Code], Values(await Json(await Send(op, HttpMethod.Get, root + "?aktif=true")), "kod"));
        Assert.Equal([c.Code2], Values(await Json(await Send(op, HttpMethod.Get, root + "?aktif=false")), "kod"));
        Assert.Equal([c.Code2], Values(await Json(await Send(op, HttpMethod.Get, root + "?q=ikin")), "kod"));
        await ExpectProblem(await Send(op, HttpMethod.Get, root + "?sirala=yok"), HttpStatusCode.BadRequest, "dogrulama", "sirala");
        var page = await Json(await Send(op, HttpMethod.Get, root + "/sayfa?sayfa=2&boyut=1&sirala=kod"));
        Assert.Equal(2, page.GetProperty("toplam").GetInt32());
        Assert.Equal([c.Code2], Values(page.GetProperty("kayitlar"), "kod"));

        // Full PUT: version required; fresh version wins; the old one → 409 cakisma; duplicate code → 400 kod.
        var version = a.GetProperty("surum").GetString();
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{root}/{id}", c.Body(c.Code, "Yeni", false, null)), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var updated = await Json(await Send(op, HttpMethod.Put, $"{root}/{id}", c.Body(c.Code, "Yeni", false, version)));
        Assert.Equal("Yeni", updated.GetProperty("ad").GetString());
        Assert.False(updated.GetProperty("aktif").GetBoolean());
        Assert.NotEqual(version, updated.GetProperty("surum").GetString());
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{root}/{id}", c.Body(c.Code, "Bayat", true, version)), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal("Yeni", (await Json(await Send(op, HttpMethod.Get, $"{root}/{id}"))).GetProperty("ad").GetString());
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{root}/{id}",
            c.Body(c.Code2, "Yeni", false, updated.GetProperty("surum").GetString())), HttpStatusCode.BadRequest, "dogrulama", "kod");

        // Permission: Muhasebe has no OperationsWrite (Blazor page policy) → 403 on read and write.
        var acc = await LoginAsync(env, Who.Muhasebe);
        await ExpectProblem(await Send(acc, HttpMethod.Get, root), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Send(acc, HttpMethod.Post, root, c.Body("MM1", "Muh", true, null)), HttpStatusCode.Forbidden, "yetki_yok");

        // Tenant isolation (RLS, racar_app): another tenant sees nothing and gets 404 on the id.
        var other = await LoginAsync(await SetUpAsync(), Who.Admin);
        Assert.Empty((await Json(await Send(other, HttpMethod.Get, root))).EnumerateArray());
        await ExpectProblem(await Send(other, HttpMethod.Get, $"{root}/{id}"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(other, HttpMethod.Put, $"{root}/{id}", c.Body(c.Code, "Sızma", true, updated.GetProperty("surum").GetString())), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(other, HttpMethod.Delete, $"{root}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal("Yeni", (await Json(await Send(op, HttpMethod.Get, $"{root}/{id}"))).GetProperty("ad").GetString());

        // Delete: 204, then 404; the other row stays.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Delete, $"{root}/{id}")).StatusCode);
        await ExpectProblem(await Send(op, HttpMethod.Get, $"{root}/{id}"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(op, HttpMethod.Delete, $"{root}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal([id2], (await Json(await Send(op, HttpMethod.Get, root))).EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());
    }

    [Fact]
    public async Task Brand_in_use_by_a_vehicle_cannot_be_deleted_but_can_be_deactivated()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        var brand = await Json(await Send(op, HttpMethod.Post, V1 + "/markalar", new { kod = "FIAT", ad = "Fiat" }), HttpStatusCode.Created);
        var id = brand.GetProperty("id").GetGuid();
        await WriteAsync(env.TenantId, db => db.Vehicles.Add(new Vehicle { Plaka = "34TNM" + Guid.NewGuid().ToString("N")[..3], Marka = "Fiat", Sube = "SubeA" }));

        var refused = await ExpectProblem(await Send(op, HttpMethod.Delete, $"{V1}/markalar/{id}"), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Contains("pasife", refused.GetProperty("detail").GetString());
        var passive = await Json(await Send(op, HttpMethod.Put, $"{V1}/markalar/{id}",
            new { kod = "FIAT", ad = "Fiat", aktif = false, surum = brand.GetProperty("surum").GetString() }));
        Assert.False(passive.GetProperty("aktif").GetBoolean());

        // An unused brand deletes normally.
        var free = await Json(await Send(op, HttpMethod.Post, V1 + "/markalar", new { kod = "OPEL", ad = "Opel" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Delete, $"{V1}/markalar/{free.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Bank_custom_code_and_expense_category_in_use_cannot_be_deleted()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        var bank = (await Json(await Send(op, HttpMethod.Post, V1 + "/bankalar", new { kod = "ZRT", ad = "Ziraat" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var code = (await Json(await Send(op, HttpMethod.Post, V1 + "/ozel-kodlar", new { kod = "VIP", ad = "VIP Müşteri" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var cat = (await Json(await Send(op, HttpMethod.Post, V1 + "/gider-turleri", new { kod = "YKT", ad = "Yakıt" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        await WriteAsync(env.TenantId, db =>
        {
            db.FinancialAccounts.Add(new FinancialAccount { Kod = "BNK1", Ad = "Ziraat TL", Banka = "Ziraat" });
            db.Customers.Add(new Customer { Tip = RentACar.Domain.Enums.CariType.Bireysel, Ad = "Ece", Soyad = "Kaya", OzelKod = "VIP" });
        });
        await ExpectProblem(await Send(op, HttpMethod.Delete, $"{V1}/bankalar/{bank}"), HttpStatusCode.BadRequest, "dogrulama");
        await ExpectProblem(await Send(op, HttpMethod.Delete, $"{V1}/ozel-kodlar/{code}"), HttpStatusCode.BadRequest, "dogrulama");
        // Unused category deletes; the rows above are still there.
        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Delete, $"{V1}/gider-turleri/{cat}")).StatusCode);
        Assert.Single((await Json(await Send(op, HttpMethod.Get, V1 + "/bankalar"))).EnumerateArray());
        Assert.Single((await Json(await Send(op, HttpMethod.Get, V1 + "/ozel-kodlar"))).EnumerateArray());
    }
}
