using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiTanimTests
{
    [Fact]
    public async Task Drop_definition_crud_required_active_limits_version_and_isolation()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        const string Root = V1 + "/drop-tanimlari";

        var d = await Json(await Send(op, HttpMethod.Post, Root,
            new { lokasyon = " Havalimanı ", sube = "Merkez", ucret = 250.5m, minGun = 2, aktif = true }), HttpStatusCode.Created);
        var id = d.GetProperty("id").GetGuid();
        Assert.Equal("Havalimanı", d.GetProperty("lokasyon").GetString());
        Assert.Equal(250.5m, d.GetProperty("ucret").GetDecimal());
        Assert.Equal(JsonValueKindNull, d.GetProperty("cikisLokasyon").ValueKind);

        // "aktif" missing → 400 (never silently passive); same (lokasyon, sube, NULL çıkış) → 400; limits.
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { lokasyon = "Liman", sube = "Merkez" }), HttpStatusCode.BadRequest, "dogrulama", "aktif");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { lokasyon = "Havalimanı", sube = "Merkez", aktif = true }), HttpStatusCode.BadRequest, "dogrulama", "lokasyon");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { lokasyon = "Liman", sube = "Merkez", ucret = -1m, aktif = true }), HttpStatusCode.BadRequest, "dogrulama", "ucret");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { lokasyon = "Liman", sube = "Merkez", ucret = 1_000_000_000m, aktif = true }), HttpStatusCode.BadRequest, "dogrulama", "ucret");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { lokasyon = new string('L', 151), sube = "Merkez", aktif = true }), HttpStatusCode.BadRequest, "dogrulama", "lokasyon");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { lokasyon = "Liman", sube = "", aktif = true }), HttpStatusCode.BadRequest, "dogrulama", "sube");

        // Same pair with a pick-up location is a different row.
        await Json(await Send(op, HttpMethod.Post, Root, new { lokasyon = "Havalimanı", sube = "Merkez", cikisLokasyon = "Otogar", ucret = 100m, aktif = true }), HttpStatusCode.Created);
        Assert.Equal(2, (await Json(await Send(op, HttpMethod.Get, Root))).GetArrayLength());
        Assert.Single((await Json(await Send(op, HttpMethod.Get, Root + "?q=otogar"))).EnumerateArray());

        // Full PUT with version; stale → 409 and the fee is unchanged.
        var version = d.GetProperty("surum").GetString();
        var u = await Json(await Send(op, HttpMethod.Put, $"{Root}/{id}", new { lokasyon = "Havalimanı", sube = "Merkez", ucret = 300m, aktif = false, surum = version }));
        Assert.Equal(300m, u.GetProperty("ucret").GetDecimal());
        Assert.False(u.GetProperty("aktif").GetBoolean());
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{Root}/{id}", new { lokasyon = "Havalimanı", sube = "Merkez", ucret = 1m, aktif = true, surum = version }), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal(300m, (await Json(await Send(op, HttpMethod.Get, $"{Root}/{id}"))).GetProperty("ucret").GetDecimal());

        var acc = await LoginAsync(env, Who.Muhasebe);
        await ExpectProblem(await Send(acc, HttpMethod.Get, Root), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await LoginAsync(await SetUpAsync(), Who.Admin);
        await ExpectProblem(await Send(other, HttpMethod.Delete, $"{Root}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Delete, $"{Root}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Occupancy_rule_crud_day_round_trip_limits_and_bulk_ladder()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        const string Root = V1 + "/doluluk-kurallari";
        var start = DateOnly.FromDateTime(TestZaman.DaysLater(10).DateTime);
        var end = start.AddDays(30);

        var r = await Json(await Send(op, HttpMethod.Post, Root, new
        {
            kod = "yaz-80", ad = "Yaz %80", aracGrupKod = "eko", esikYuzde = 80, carpanYuzde = 15m,
            gecerlilikBas = start.ToString("yyyy-MM-dd"), gecerlilikBit = end.ToString("yyyy-MM-dd"),
        }), HttpStatusCode.Created);
        var id = r.GetProperty("id").GetGuid();
        Assert.Equal("YAZ-80", r.GetProperty("kod").GetString());
        Assert.Equal("EKO", r.GetProperty("aracGrupKod").GetString());
        // The calendar day comes back unchanged (Istanbul day ↔ UTC instant round trip).
        Assert.Equal(start.ToString("yyyy-MM-dd"), r.GetProperty("gecerlilikBas").GetString());
        Assert.Equal(end.ToString("yyyy-MM-dd"), r.GetProperty("gecerlilikBit").GetString());

        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { kod = "C1", ad = "Çarpan", esikYuzde = 50, carpanYuzde = 60m }), HttpStatusCode.BadRequest, "dogrulama", "carpanYuzde");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { kod = "C2", ad = "Eşik", esikYuzde = 0, carpanYuzde = 5m }), HttpStatusCode.BadRequest, "dogrulama", "esikYuzde");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new
        {
            kod = "C3", ad = "Tarih", esikYuzde = 50, carpanYuzde = 5m,
            gecerlilikBas = end.ToString("yyyy-MM-dd"), gecerlilikBit = start.ToString("yyyy-MM-dd"),
        }), HttpStatusCode.BadRequest, "dogrulama", "gecerlilikBit");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, new { kod = "YAZ-80", ad = "Kopya", esikYuzde = 50, carpanYuzde = 5m }), HttpStatusCode.BadRequest, "dogrulama", "kod");

        // Bulk ladder: two steps → two rules; a clash with an existing code writes NOTHING.
        var bulk = await Json(await Send(op, HttpMethod.Post, Root + "/toplu", new
        {
            kodOnEk = "kis", adOnEk = "Kış", aracGrupKod = "eko",
            kademeler = new[] { new { esikYuzde = 85, carpanYuzde = 10m }, new { esikYuzde = 95, carpanYuzde = 20m } },
        }), HttpStatusCode.Created);
        Assert.Equal(2, bulk.GetProperty("kimlikler").GetArrayLength());
        await ExpectProblem(await Send(op, HttpMethod.Post, Root + "/toplu", new
        {
            kodOnEk = "kis", adOnEk = "Kış", kademeler = new[] { new { esikYuzde = 70, carpanYuzde = 5m }, new { esikYuzde = 85, carpanYuzde = 5m } },
        }), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(["KIS-85", "KIS-95", "YAZ-80"], Values(await Json(await Send(op, HttpMethod.Get, Root + "?sirala=kod")), "kod"));

        // Full PUT with version; stale → 409.
        var version = r.GetProperty("surum").GetString();
        var u = await Json(await Send(op, HttpMethod.Put, $"{Root}/{id}", new { kod = "YAZ-80", ad = "Yaz", esikYuzde = 75, carpanYuzde = 12.5m, surum = version }));
        Assert.Equal(75, u.GetProperty("esikYuzde").GetInt32());
        Assert.Equal(JsonValueKindNull, u.GetProperty("gecerlilikBas").ValueKind);
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{Root}/{id}", new { kod = "YAZ-80", ad = "Bayat", esikYuzde = 90, carpanYuzde = 1m, surum = version }), HttpStatusCode.Conflict, "cakisma");

        var acc = await LoginAsync(env, Who.Muhasebe);
        await ExpectProblem(await Send(acc, HttpMethod.Post, Root + "/toplu", new { kodOnEk = "x", adOnEk = "x", kademeler = new[] { new { esikYuzde = 50, carpanYuzde = 5m } } }), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await LoginAsync(await SetUpAsync(), Who.Admin);
        Assert.Empty((await Json(await Send(other, HttpMethod.Get, Root))).EnumerateArray());
        await ExpectProblem(await Send(other, HttpMethod.Get, $"{Root}/{id}"), HttpStatusCode.NotFound, null);
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;
}
