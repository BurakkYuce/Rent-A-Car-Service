using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiServiceInsuranceTests
{
    [Fact]
    public async Task Catalog_put_requires_fresh_version_and_tenant_isolation()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var body = new { kod = "t1", ad = "Tarife 1", grup = "C", minGun = 1, maxGun = 30, gunlukUcret = 100m, surum = (string?)null };
        var created = await Json(await Send(s, HttpMethod.Post, V1 + "/tarifeler", body), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("T1", created.GetProperty("kod").GetString());
        await Problem(await Send(s, HttpMethod.Post, V1 + "/tarifeler", body), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await Problem(await Send(s, HttpMethod.Post, V1 + "/tarifeler", body with { kod = "t2", gunlukUcret = 1.00001m }),
            HttpStatusCode.BadRequest, "dogrulama", "gunlukUcret");

        await Problem(await Send(s, HttpMethod.Put, $"{V1}/tarifeler/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var v = created.GetProperty("surum").GetString();
        var updated = await Json(await Send(s, HttpMethod.Put, $"{V1}/tarifeler/{id}", body with { gunlukUcret = 120m, surum = v }));
        Assert.Equal(120m, updated.GetProperty("gunlukUcret").GetDecimal());
        await Problem(await Send(s, HttpMethod.Put, $"{V1}/tarifeler/{id}", body with { gunlukUcret = 90m, surum = v }),
            HttpStatusCode.Conflict, "cakisma");
        Assert.Equal(120m, (await Json(await s.C.GetAsync($"{V1}/tarifeler/{id}"))).GetProperty("gunlukUcret").GetDecimal());

        var other = await SetupAsync();
        var o = await LoginAsync(other, Who.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await o.C.GetAsync($"{V1}/tarifeler/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(o, HttpMethod.Delete, $"{V1}/tarifeler/{id}")).StatusCode);
        Assert.Equal(0, (await Json(await o.C.GetAsync(V1 + "/tarifeler"))).GetProperty("toplam").GetInt32());
        var acc = await LoginAsync(e, Who.Accounting);
        Assert.Equal(HttpStatusCode.Forbidden, (await acc.C.GetAsync(V1 + "/tarifeler")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(s, HttpMethod.Delete, $"{V1}/tarifeler/{id}")).StatusCode);
    }

    [Fact]
    public async Task Rate_matrix_approval_is_server_stamped_and_quote_uses_engine_oracle()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var matrix = new
        {
            kod = "m1", ad = "Matris", aracGrupKod = "q9", gun1 = 100m, gun2 = 100m, gun3 = 100m, gun4 = 100m, gun5 = 100m,
            gun6 = 100m, gun7 = 100m, onayDurumu = "Onayli", onaylayan = "sahte", kanal = (string?)null, surum = (string?)null,
        };
        var m = await Json(await Send(s, HttpMethod.Post, V1 + "/tarife-matris", matrix), HttpStatusCode.Created);
        Assert.Equal(e.Users[Who.Admin], m.GetProperty("onaylayan").GetString()); // client value ignored
        Assert.Equal(System.Text.Json.JsonValueKind.String, JsonValueKindOf(m, "onayZaman"));
        await Json(await Send(s, HttpMethod.Post, V1 + "/sigorta-urunleri", new { kod = "scdw9", ad = "SCDW", gunlukUcret = 50m }),
            HttpStatusCode.Created);

        // ORACLE: 3 days × 100 = 300; product 3 × 50 = 150; total 450 (no rules/VAT in this tenant).
        var start = TestZaman.GunSonra(10);
        var q = await Json(await Send(s, HttpMethod.Post, V1 + "/fiyat-hesapla",
            new { aracGrupKod = "Q9", basTar = start, bitTar = start.AddDays(3), sigortaUrunKodlari = new[] { "SCDW9" } }));
        Assert.Equal(3, q.GetProperty("gun").GetInt32());
        Assert.Equal(300m, q.GetProperty("bazTutar").GetDecimal());
        Assert.Equal(150m, q.GetProperty("sigortaToplam").GetDecimal());
        Assert.Equal(450m, q.GetProperty("genelToplam").GetDecimal());
        Assert.Equal("M1", q.GetProperty("tarifeKodu").GetString());
        await Problem(await Send(s, HttpMethod.Post, V1 + "/fiyat-hesapla", new { aracGrupKod = "Q9", basTar = start, bitTar = start }),
            HttpStatusCode.BadRequest, "dogrulama", "bitTar");
        var op = await LoginAsync(e, Who.OperatorA);
        Assert.Equal(HttpStatusCode.OK, (await Send(op, HttpMethod.Post, V1 + "/fiyat-hesapla",
            new { aracGrupKod = "Q9", basTar = start, bitTar = start.AddDays(1) })).StatusCode);

        // Channel bulk delete never touches approved rows (state not taken from the client).
        await Json(await Send(s, HttpMethod.Post, V1 + "/tarife-matris", matrix with { kod = "m2", onayDurumu = "Bekliyor" }),
            HttpStatusCode.Created);
        var mid = m.GetProperty("id").GetGuid();
        var moved = await Json(await Send(s, HttpMethod.Put, $"{V1}/tarife-matris/{mid}",
            matrix with { kanal = "WEB", surum = m.GetProperty("surum").GetString() }));
        Assert.Equal(m.GetProperty("onayZaman").GetDateTimeOffset(), moved.GetProperty("onayZaman").GetDateTimeOffset()); // state kept → stamp kept
        var view = await Json(await s.C.GetAsync(V1 + "/tarife-aktar?kanal=WEB"));
        Assert.Equal(0, view.GetProperty("silinecek").GetInt32());
        Assert.Equal(0, (await Json(await Send(s, HttpMethod.Post, V1 + "/tarife-aktar/kanal-sil", new { kanal = "WEB" }))).GetProperty("silinen").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(op, HttpMethod.Post, V1 + "/tarife-aktar/kanal-sil", new { kanal = "WEB" })).StatusCode);

        // Upload: rows enter as Bekliyor even when the file says approved.
        var csv = "Kod;Ad;Kanal;Grup;Gun1;OnayDurumu\nIMP1;Imp;WEB;Q9;80;Onayli\n";
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "dosya", "tarife.csv");
        var up = new HttpRequestMessage(HttpMethod.Post, V1 + "/tarife-aktar/yukle") { Content = form };
        up.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        var result = await Json(await s.C.SendAsync(up));
        Assert.Equal(1, result.GetProperty("eklenen").GetInt32());
        var after = await Json(await s.C.GetAsync(V1 + "/tarife-aktar?kanal=WEB&durum=Bekliyor"));
        Assert.Equal(1, after.GetProperty("silinecek").GetInt32());
        Assert.Equal(1, (await Json(await Send(s, HttpMethod.Post, V1 + "/tarife-aktar/kanal-sil", new { kanal = "WEB" }))).GetProperty("silinen").GetInt32());
    }

    private static System.Text.Json.JsonValueKind JsonValueKindOf(System.Text.Json.JsonElement e, string p) => e.GetProperty(p).ValueKind;

    [Fact]
    public async Task Cost_calculation_oracle_and_offer_crud()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        // ORACLE: 120.000 − 25 % residual (30.000) = 90.000; interest 120.000 × 10 % × 12/12 = 12.000; taxes 12.000 × 20 % =
        // 2.400; kasko 6.000/yr × 12 months = 6.000 → cost 110.400; profit 10 % = 11.040 → 121.440; monthly 10.120;
        // with VAT 20 % 145.728; fleet of 2 → monthly 20.240.
        var input = new
        {
            alisBedeli = 120000m, residualYuzde = 0.25m, sureAy = 12, faizOran = 0.10m, kkdfOran = 0.15m, bsmvOran = 0.05m,
            damgaOran = 0m, karMarji = 0.10m, kdvOran = 0.20m, aracSayisi = 2, kaskoYillik = 6000m,
        };
        var r = await Json(await Send(s, HttpMethod.Post, V1 + "/maliyet-hesapla", input));
        Assert.Equal(110400m, r.GetProperty("toplamMaliyet").GetDecimal());
        Assert.Equal(10120m, r.GetProperty("teklifAylikNet").GetDecimal());
        Assert.Equal(145728m, r.GetProperty("teklifKdvli").GetDecimal());
        Assert.Equal(20240m, r.GetProperty("filoTeklifAylikNet").GetDecimal());
        await Problem(await Send(s, HttpMethod.Post, V1 + "/maliyet-hesapla", input with { alisBedeli = 0m }), HttpStatusCode.BadRequest, "dogrulama", "alisBedeli");

        var key = Key();
        var offer = new { baslik = "Filo teklifi", cariId = e.CustomerId, girdi = input, surum = (string?)null };
        var created = await Json(await Send(s, HttpMethod.Post, V1 + "/maliyet-teklifleri", offer, key), HttpStatusCode.Created);
        var id = created.GetProperty("teklif").GetProperty("id").GetGuid();
        Assert.Equal("Ece Tan", created.GetProperty("teklif").GetProperty("cariAd").GetString());
        Assert.Equal(10120m, created.GetProperty("sonuc").GetProperty("teklifAylikNet").GetDecimal());
        var dup = await Problem(await Send(s, HttpMethod.Post, V1 + "/maliyet-teklifleri", offer, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(dup.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var v = created.GetProperty("surum").GetString();
        await Json(await Send(s, HttpMethod.Put, $"{V1}/maliyet-teklifleri/{id}", offer with { baslik = "Güncel", surum = v }));
        await Problem(await Send(s, HttpMethod.Put, $"{V1}/maliyet-teklifleri/{id}", offer with { surum = v }), HttpStatusCode.Conflict, "cakisma");
        var list = await Json(await s.C.GetAsync(V1 + "/maliyet-teklifleri"));
        Assert.Equal(1, list.GetProperty("kayitlar").GetProperty("toplam").GetInt32());
        Assert.Equal(2, list.GetProperty("ozet").GetProperty("aracAdet").GetInt32());
        var op = await LoginAsync(e, Who.OperatorA);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.C.GetAsync(V1 + "/maliyet-teklifleri")).StatusCode);
    }
}
