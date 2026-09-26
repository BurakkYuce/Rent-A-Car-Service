using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    [Fact]
    public async Task Siparis_idempotent_surum_ve_iptal_terminal()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var body = new { tedarikci = "Bayi A", adet = 2, birimFiyat = 750_000m };
        var k = Key();
        var c = await Json(await Gonder(s, HttpMethod.Post, Order, body, k), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        var m = await ExpectProblem(await Gonder(s, HttpMethod.Post, Order, body, k), HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(1_500_000m, m.GetProperty("mevcut").GetProperty("tutar").GetDecimal()); // 2 × 750.000
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Order, new { tedarikci = "B", birimFiyat = 1m, kur = 3m }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Order, new { tedarikci = "B", birimFiyat = 1m, krediId = Guid.NewGuid() }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "krediId");

        var d = await Json(await s.C.GetAsync($"{Order}/{id}"));
        Assert.Equal(1_500_000m, d.GetProperty("toplam").GetDecimal());
        var version = d.GetProperty("surum").GetString();
        var u = await Json(await Gonder(s, HttpMethod.Put, $"{Order}/{id}", new { tedarikci = "Bayi A2", adet = 1, birimFiyat = 10m, surum = version }));
        Assert.Equal("Bayi A2", u.GetProperty("tedarikci").GetString());
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Order}/{id}", new { tedarikci = "X", birimFiyat = 1m, surum = version }),
            HttpStatusCode.Conflict, "cakisma");

        // Eşzamanlı iptal + onayla: iptal terminal — son durum iptal ise onay geri getiremez.
        await Task.WhenAll(Gonder(s, HttpMethod.Post, $"{Order}/{id}/iptal"), Gonder(s, HttpMethod.Post, $"{Order}/{id}/onayla"));
        Assert.Equal("Iptal", (await Json(await Gonder(s, HttpMethod.Post, $"{Order}/{id}/iptal"))).GetProperty("durum").GetString());
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Order}/{id}/onayla"), HttpStatusCode.Conflict, "cakisma"); // M1: izinsiz geçiş
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Order}/{id}", new { tedarikci = "Y", birimFiyat = 1m,
            surum = (await Json(await s.C.GetAsync($"{Order}/{id}"))).GetProperty("surum").GetString() }), HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Siparis_listesi_bilgi_alanlarini_tasir()
    {
        // F6.2b: Blazor liste sütunları (renk, kaynak/satış tipi, bilgi fiyatları, imza, TSB) liste satırında.
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var signature = new DateTimeOffset(TestZaman.DaysLater(-3).UtcDateTime.Date, TimeSpan.Zero);
        var body = new
        {
            tedarikci = "Liste Bayi " + Guid.NewGuid().ToString("N")[..6], adet = 3, birimFiyat = 100m, versiyon = "1.4 Urban",
            renk = "Beyaz", icRenk = "Siyah", kaynakTip = "Filo", satisTipi = "Sıfır", piyasaFiyat = 120m, opsFiyat = 110m,
            filoFiyat = 95.5m, imzaTarih = signature, tsbKayitNo = "TSB-9",
        };
        await Json(await Gonder(s, HttpMethod.Post, Order, body, Key()), HttpStatusCode.Created);
        var list = await Json(await s.C.GetAsync($"{Order}?ara={Uri.EscapeDataString(body.tedarikci)}"));
        var r = Assert.Single(list.GetProperty("kayitlar").EnumerateArray());
        Assert.Equal(300m, r.GetProperty("toplam").GetDecimal()); // 3 × 100 — bilgi fiyatları toplama girmez
        Assert.Equal("1.4 Urban", r.GetProperty("versiyon").GetString());
        Assert.Equal("Beyaz", r.GetProperty("renk").GetString());
        Assert.Equal("Siyah", r.GetProperty("icRenk").GetString());
        Assert.Equal("Filo", r.GetProperty("kaynakTip").GetString());
        Assert.Equal("Sıfır", r.GetProperty("satisTipi").GetString());
        Assert.Equal(120m, r.GetProperty("piyasaFiyat").GetDecimal());
        Assert.Equal(110m, r.GetProperty("opsFiyat").GetDecimal());
        Assert.Equal(95.5m, r.GetProperty("filoFiyat").GetDecimal());
        Assert.Equal(signature, r.GetProperty("imzaTarih").GetDateTimeOffset());
        Assert.Equal("TSB-9", r.GetProperty("tsbKayitNo").GetString());
        // Satır düğmeleri detayla aynı geçiş tablosundan: Bekliyor → onay/teslim/iptal açık, düzenleme açık.
        var y = r.GetProperty("yetkiler");
        Assert.True(y.GetProperty("duzenle").GetBoolean());
        Assert.True(y.GetProperty("onayla").GetBoolean());
        Assert.True(y.GetProperty("teslimAl").GetBoolean());
        Assert.True(y.GetProperty("iptal").GetBoolean());

        var id = r.GetProperty("id").GetGuid();
        await Json(await Gonder(s, HttpMethod.Post, $"{Order}/{id}/iptal"));
        var after = Assert.Single((await Json(await s.C.GetAsync($"{Order}?ara={Uri.EscapeDataString(body.tedarikci)}")))
            .GetProperty("kayitlar").EnumerateArray()).GetProperty("yetkiler");
        Assert.False(after.GetProperty("duzenle").GetBoolean()); // iptal terminal: hiçbir düğme
        Assert.False(after.GetProperty("onayla").GetBoolean());
        Assert.False(after.GetProperty("teslimAl").GetBoolean());
        Assert.False(after.GetProperty("iptal").GetBoolean());
    }

    [Fact]
    public async Task Baf_ve_hasar_kapsam_ve_kilitli_gecis()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicleA = await VehicleAsync(o, "SubeA");
        var staff = new Domain.Entities.Personel { Kod = "P1", Ad = "Ali", Soyad = "Can" };
        await WriteDataAsync(o.TenantId, db => db.Personeller.Add(staff));
        var a = await LoginAsync(o, Kim.OperatorA);
        var b = await LoginAsync(o, Kim.OperatorB);

        // BAF: B şubesi kullanıcısı A aracına / A şubesine tahsis yazamaz; teslim kilitli, ikinci teslim 400.
        await ExpectProblem(await Gonder(b, HttpMethod.Post, Baf, new { personelId = staff.Id, vehicleId = vehicleA, cikisKm = 100, sube = "SubeB" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        var baf = (await Json(await Gonder(a, HttpMethod.Post, Baf, new { personelId = staff.Id, vehicleId = vehicleA, cikisKm = 100, sube = "SubeA" }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ExpectProblem(await b.C.GetAsync($"{Baf}/{baf}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(a, HttpMethod.Post, $"{Baf}/{baf}/teslim-al", new { donusKm = 50 }), HttpStatusCode.BadRequest, "dogrulama", "donusKm");
        var delivery = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Gonder(a, HttpMethod.Post, $"{Baf}/{baf}/teslim-al", new { donusKm = 150 })));
        Assert.Equal(1, delivery.Count(r => r.StatusCode == HttpStatusCode.OK));

        // Hasar: B şubesi 403 (durumdan önce); onayla + reddet yarışı TEK kazanır.
        var h = (await Json(await Gonder(a, HttpMethod.Post, Damage, new { vehicleId = vehicleA, tahminiTutar = 2500m }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{Damage}/{h}/onaya-gonder"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Damage))).GetProperty("toplam").GetInt32());
        await Json(await Gonder(a, HttpMethod.Post, $"{Damage}/{h}/onaya-gonder"));
        var race = await Task.WhenAll(Gonder(a, HttpMethod.Post, $"{Damage}/{h}/onayla", new { not = "ok" }),
            Gonder(a, HttpMethod.Post, $"{Damage}/{h}/reddet", new { not = "red" }));
        Assert.Equal(1, race.Count(r => r.StatusCode == HttpStatusCode.OK));
        await ExpectProblem(await Gonder(a, HttpMethod.Post, Damage, new { vehicleId = vehicleA, rentalId = Guid.NewGuid() }),
            HttpStatusCode.BadRequest, "dogrulama", "rentalId");
    }

    [Fact]
    public async Task FiloPlan_eszamanli_artir_kayipsiz_ve_surum()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        var c = await Json(await Gonder(s, HttpMethod.Post, Plan, new { aracGrupAdi = "C", hedefAdet = 5 }), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Plan, new { aracGrupAdi = "C", hedefAdet = 1 }), HttpStatusCode.BadRequest, "dogrulama", "donem");
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Gonder(s, HttpMethod.Post, $"{Plan}/{id}/delta", new { yon = "artir" })));
        var d = await Json(await s.C.GetAsync($"{Plan}/{id}"));
        Assert.Equal(15, d.GetProperty("hedefAdet").GetInt32()); // 5 + 10 — kayıp artış yok
        var version = d.GetProperty("surum").GetString();
        Assert.Equal(7, (await Json(await Gonder(s, HttpMethod.Put, $"{Plan}/{id}", new { aracGrupAdi = "C", hedefAdet = 7, surum = version }))).GetProperty("hedefAdet").GetInt32());
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Plan}/{id}", new { aracGrupAdi = "C", hedefAdet = 8, surum = version }), HttpStatusCode.Conflict, "cakisma");
    }

    [Fact]
    public void Izin_haritasi()
    {
        string[] prefixes = [Loan, Installment, Order, Baf, Damage, Plan];
        var endpoints = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => prefixes.Any(p => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith(p, StringComparison.Ordinal)))
            .ToList();
        Assert.Equal(41, endpoints.Count); // kredi 7 + taksit 9 + sipariş 7 + BAF 5 + hasar 7 + filo plan 6
        Assert.All(endpoints, e => Assert.True(e.Metadata.GetMetadata<IzinMetadata>() is not null
            || e.Metadata.GetMetadata<IzinlerdenBiriMetadata>() is not null, e.RoutePattern.RawText));
        string Permission(string method, string path) => endpoints.Single(e => e.RoutePattern.RawText!.Trim('/') == path.Trim('/')
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method)).Metadata.GetMetadata<IzinMetadata>()!.Izin.ToString();
        Assert.Equal("FinanceWrite", Permission("POST", $"{Loan}/{{id:guid}}/taksit-ode"));
        Assert.Equal("OperationsDelete", Permission("POST", $"{Loan}/{{id:guid}}/iptal"));
        Assert.Equal("OperationsDelete", Permission("POST", $"{Baf}/{{id:guid}}/iptal"));
        Assert.Equal("FinanceWrite", Permission("POST", $"{Installment}/plan"));
    }
}
