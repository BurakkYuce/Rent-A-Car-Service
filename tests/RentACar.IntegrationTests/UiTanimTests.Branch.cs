using System.Net;

namespace RentACar.IntegrationTests;

public sealed partial class UiTanimTests
{
    [Fact]
    public async Task Branch_crud_limits_linked_accounts_free_services_and_merge()
    {
        var env = await SetUpAsync();
        var admin = await LoginAsync(env, Who.Admin);
        const string Root = V1 + "/subeler";

        var a = await Json(await Send(admin, HttpMethod.Post, Root,
            new { kod = " ist ", ad = "İstanbul", il = "İstanbul", komisyonOran = 0.1m, enlem = 41.0082m, boylam = 28.9784m }), HttpStatusCode.Created);
        var idA = a.GetProperty("id").GetGuid();
        Assert.Equal("IST", a.GetProperty("kod").GetString());
        Assert.Equal(0.1m, a.GetProperty("komisyonOran").GetDecimal());
        var b = await Json(await Send(admin, HttpMethod.Post, Root, new { kod = "ANK", ad = "Ankara" }), HttpStatusCode.Created);
        var idB = b.GetProperty("id").GetGuid();

        // Edge limits (column sizes / fraction rates) and linked account checks.
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root, new { kod = "X1", ad = "X", komisyonOran = 1.5m }), HttpStatusCode.BadRequest, "dogrulama", "komisyonOran");
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root, new { kod = "X2", ad = "X", rezervasyonRengi = "#12345678" }), HttpStatusCode.BadRequest, "dogrulama", "rezervasyonRengi");
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root, new { kod = "X3", ad = "X", enlem = 91m }), HttpStatusCode.BadRequest, "dogrulama", "enlem");
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root, new { kod = "X4", ad = "X", nakitHesapId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "dogrulama", "nakitHesapId");
        var bankAccount = (await Json(await Send(admin, HttpMethod.Post, V1 + "/hesaplar", new { kod = "BNK", ad = "Banka TL", tur = "Banka" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root, new { kod = "X5", ad = "X", nakitHesapId = bankAccount }), HttpStatusCode.BadRequest, "dogrulama", "nakitHesapId");
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root, new { kod = "IST", ad = "Kopya" }), HttpStatusCode.BadRequest, "dogrulama", "kod");

        // Full PUT with version: omitted fields are cleared (full replacement); stale → 409.
        var u = await Json(await Send(admin, HttpMethod.Put, $"{Root}/{idA}",
            new { kod = "IST", ad = "İstanbul Merkez", bankaHesapId = bankAccount, surum = a.GetProperty("surum").GetString() }));
        Assert.Equal("İstanbul Merkez", u.GetProperty("ad").GetString());
        Assert.Equal(bankAccount, u.GetProperty("bankaHesapId").GetGuid());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, u.GetProperty("komisyonOran").ValueKind);
        await ExpectProblem(await Send(admin, HttpMethod.Put, $"{Root}/{idA}",
            new { kod = "IST", ad = "Bayat", surum = a.GetProperty("surum").GetString() }), HttpStatusCode.Conflict, "cakisma");

        // Free services belong to one branch; deleting through another branch's path is 404.
        var h = await Json(await Send(admin, HttpMethod.Post, $"{Root}/{idA}/hizmetler", new { hizmetAdi = "Ücretsiz teslim", aciklama = "Havalimanı" }), HttpStatusCode.Created);
        var hid = h.GetProperty("id").GetGuid();
        await ExpectProblem(await Send(admin, HttpMethod.Post, $"{Root}/{idA}/hizmetler", new { hizmetAdi = "" }), HttpStatusCode.BadRequest, "dogrulama", "hizmetAdi");
        Assert.Single((await Json(await Send(admin, HttpMethod.Get, $"{Root}/{idA}/hizmetler"))).EnumerateArray());
        await ExpectProblem(await Send(admin, HttpMethod.Delete, $"{Root}/{idB}/hizmetler/{hid}"), HttpStatusCode.NotFound, null);

        // Merge: preview → onay required → A's service moves to B, A becomes passive.
        var preview = await Json(await Send(admin, HttpMethod.Get, $"{Root}/birlestir/onizleme?kaynakId={idA}&hedefId={idB}"));
        Assert.Equal("İstanbul Merkez", preview.GetProperty("kaynakAd").GetString());
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root + "/birlestir", new { kaynakId = idA, hedefId = idB }), HttpStatusCode.BadRequest, "dogrulama", "onay");
        await ExpectProblem(await Send(admin, HttpMethod.Post, Root + "/birlestir", new { kaynakId = idA, hedefId = idA, onay = true }), HttpStatusCode.BadRequest, "dogrulama", "hedefId");
        var merged = await Json(await Send(admin, HttpMethod.Post, Root + "/birlestir", new { kaynakId = idA, hedefId = idB, onay = true }));
        Assert.True(merged.GetProperty("tasinanKayit").GetInt32() >= 1);
        Assert.False((await Json(await Send(admin, HttpMethod.Get, $"{Root}/{idA}"))).GetProperty("aktif").GetBoolean());
        Assert.Single((await Json(await Send(admin, HttpMethod.Get, $"{Root}/{idB}/hizmetler"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{Root}/{idB}/hizmetler/{hid}")).StatusCode);

        // ManageUsers only (operator/accounting 403); another tenant 404.
        var op = await LoginAsync(env, Who.OperatorA);
        await ExpectProblem(await Send(op, HttpMethod.Get, Root), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root + "/birlestir", new { kaynakId = idA, hedefId = idB, onay = true }), HttpStatusCode.Forbidden, "yetki_yok");
        var other = await LoginAsync(await SetUpAsync(), Who.Admin);
        await ExpectProblem(await Send(other, HttpMethod.Get, $"{Root}/{idB}"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(other, HttpMethod.Get, $"{Root}/{idB}/hizmetler"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(other, HttpMethod.Post, $"{Root}/{idB}/hizmetler", new { hizmetAdi = "Sızma" }), HttpStatusCode.NotFound, null);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{Root}/{idA}")).StatusCode);
    }
}
