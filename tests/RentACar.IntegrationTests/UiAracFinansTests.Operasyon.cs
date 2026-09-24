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
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var govde = new { tedarikci = "Bayi A", adet = 2, birimFiyat = 750_000m };
        var k = Anahtar();
        var c = await Json(await Gonder(s, HttpMethod.Post, Siparis, govde, k), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        var m = await ProblemBekle(await Gonder(s, HttpMethod.Post, Siparis, govde, k), HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(1_500_000m, m.GetProperty("mevcut").GetProperty("tutar").GetDecimal()); // 2 × 750.000
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Siparis, new { tedarikci = "B", birimFiyat = 1m, kur = 3m }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Siparis, new { tedarikci = "B", birimFiyat = 1m, krediId = Guid.NewGuid() }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "krediId");

        var d = await Json(await s.C.GetAsync($"{Siparis}/{id}"));
        Assert.Equal(1_500_000m, d.GetProperty("toplam").GetDecimal());
        var surum = d.GetProperty("surum").GetString();
        var u = await Json(await Gonder(s, HttpMethod.Put, $"{Siparis}/{id}", new { tedarikci = "Bayi A2", adet = 1, birimFiyat = 10m, surum }));
        Assert.Equal("Bayi A2", u.GetProperty("tedarikci").GetString());
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Siparis}/{id}", new { tedarikci = "X", birimFiyat = 1m, surum }),
            HttpStatusCode.Conflict, "cakisma");

        // Eşzamanlı iptal + onayla: iptal terminal — son durum iptal ise onay geri getiremez.
        await Task.WhenAll(Gonder(s, HttpMethod.Post, $"{Siparis}/{id}/iptal"), Gonder(s, HttpMethod.Post, $"{Siparis}/{id}/onayla"));
        Assert.Equal("Iptal", (await Json(await Gonder(s, HttpMethod.Post, $"{Siparis}/{id}/iptal"))).GetProperty("durum").GetString());
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Siparis}/{id}/onayla"), HttpStatusCode.Conflict, "cakisma"); // M1: izinsiz geçiş
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Siparis}/{id}", new { tedarikci = "Y", birimFiyat = 1m,
            surum = (await Json(await s.C.GetAsync($"{Siparis}/{id}"))).GetProperty("surum").GetString() }), HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Siparis_listesi_bilgi_alanlarini_tasir()
    {
        // F6.2b: Blazor liste sütunları (renk, kaynak/satış tipi, bilgi fiyatları, imza, TSB) liste satırında.
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var imza = new DateTimeOffset(TestZaman.GunSonra(-3).UtcDateTime.Date, TimeSpan.Zero);
        var govde = new
        {
            tedarikci = "Liste Bayi " + Guid.NewGuid().ToString("N")[..6], adet = 3, birimFiyat = 100m, versiyon = "1.4 Urban",
            renk = "Beyaz", icRenk = "Siyah", kaynakTip = "Filo", satisTipi = "Sıfır", piyasaFiyat = 120m, opsFiyat = 110m,
            filoFiyat = 95.5m, imzaTarih = imza, tsbKayitNo = "TSB-9",
        };
        await Json(await Gonder(s, HttpMethod.Post, Siparis, govde, Anahtar()), HttpStatusCode.Created);
        var liste = await Json(await s.C.GetAsync($"{Siparis}?ara={Uri.EscapeDataString(govde.tedarikci)}"));
        var r = Assert.Single(liste.GetProperty("kayitlar").EnumerateArray());
        Assert.Equal(300m, r.GetProperty("toplam").GetDecimal()); // 3 × 100 — bilgi fiyatları toplama girmez
        Assert.Equal("1.4 Urban", r.GetProperty("versiyon").GetString());
        Assert.Equal("Beyaz", r.GetProperty("renk").GetString());
        Assert.Equal("Siyah", r.GetProperty("icRenk").GetString());
        Assert.Equal("Filo", r.GetProperty("kaynakTip").GetString());
        Assert.Equal("Sıfır", r.GetProperty("satisTipi").GetString());
        Assert.Equal(120m, r.GetProperty("piyasaFiyat").GetDecimal());
        Assert.Equal(110m, r.GetProperty("opsFiyat").GetDecimal());
        Assert.Equal(95.5m, r.GetProperty("filoFiyat").GetDecimal());
        Assert.Equal(imza, r.GetProperty("imzaTarih").GetDateTimeOffset());
        Assert.Equal("TSB-9", r.GetProperty("tsbKayitNo").GetString());
        // Satır düğmeleri detayla aynı geçiş tablosundan: Bekliyor → onay/teslim/iptal açık, düzenleme açık.
        var y = r.GetProperty("yetkiler");
        Assert.True(y.GetProperty("duzenle").GetBoolean());
        Assert.True(y.GetProperty("onayla").GetBoolean());
        Assert.True(y.GetProperty("teslimAl").GetBoolean());
        Assert.True(y.GetProperty("iptal").GetBoolean());

        var id = r.GetProperty("id").GetGuid();
        await Json(await Gonder(s, HttpMethod.Post, $"{Siparis}/{id}/iptal"));
        var sonra = Assert.Single((await Json(await s.C.GetAsync($"{Siparis}?ara={Uri.EscapeDataString(govde.tedarikci)}")))
            .GetProperty("kayitlar").EnumerateArray()).GetProperty("yetkiler");
        Assert.False(sonra.GetProperty("duzenle").GetBoolean()); // iptal terminal: hiçbir düğme
        Assert.False(sonra.GetProperty("onayla").GetBoolean());
        Assert.False(sonra.GetProperty("teslimAl").GetBoolean());
        Assert.False(sonra.GetProperty("iptal").GetBoolean());
    }

    [Fact]
    public async Task Baf_ve_hasar_kapsam_ve_kilitli_gecis()
    {
        var o = await OrtamKurAsync();
        var aracA = await AracAsync(o, "SubeA");
        var personel = new Domain.Entities.Personel { Kod = "P1", Ad = "Ali", Soyad = "Can" };
        await VeriYazAsync(o.TenantId, db => db.Personeller.Add(personel));
        var a = await GirisAsync(o, Kim.OperatorA);
        var b = await GirisAsync(o, Kim.OperatorB);

        // BAF: B şubesi kullanıcısı A aracına / A şubesine tahsis yazamaz; teslim kilitli, ikinci teslim 400.
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Baf, new { personelId = personel.Id, vehicleId = aracA, cikisKm = 100, sube = "SubeB" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        var baf = (await Json(await Gonder(a, HttpMethod.Post, Baf, new { personelId = personel.Id, vehicleId = aracA, cikisKm = 100, sube = "SubeA" }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProblemBekle(await b.C.GetAsync($"{Baf}/{baf}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(a, HttpMethod.Post, $"{Baf}/{baf}/teslim-al", new { donusKm = 50 }), HttpStatusCode.BadRequest, "dogrulama", "donusKm");
        var teslim = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Gonder(a, HttpMethod.Post, $"{Baf}/{baf}/teslim-al", new { donusKm = 150 })));
        Assert.Equal(1, teslim.Count(r => r.StatusCode == HttpStatusCode.OK));

        // Hasar: B şubesi 403 (durumdan önce); onayla + reddet yarışı TEK kazanır.
        var h = (await Json(await Gonder(a, HttpMethod.Post, Hasar, new { vehicleId = aracA, tahminiTutar = 2500m }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Hasar}/{h}/onaya-gonder"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Hasar))).GetProperty("toplam").GetInt32());
        await Json(await Gonder(a, HttpMethod.Post, $"{Hasar}/{h}/onaya-gonder"));
        var yaris = await Task.WhenAll(Gonder(a, HttpMethod.Post, $"{Hasar}/{h}/onayla", new { not = "ok" }),
            Gonder(a, HttpMethod.Post, $"{Hasar}/{h}/reddet", new { not = "red" }));
        Assert.Equal(1, yaris.Count(r => r.StatusCode == HttpStatusCode.OK));
        await ProblemBekle(await Gonder(a, HttpMethod.Post, Hasar, new { vehicleId = aracA, rentalId = Guid.NewGuid() }),
            HttpStatusCode.BadRequest, "dogrulama", "rentalId");
    }

    [Fact]
    public async Task FiloPlan_eszamanli_artir_kayipsiz_ve_surum()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        var c = await Json(await Gonder(s, HttpMethod.Post, Plan, new { aracGrupAdi = "C", hedefAdet = 5 }), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Plan, new { aracGrupAdi = "C", hedefAdet = 1 }), HttpStatusCode.BadRequest, "dogrulama", "donem");
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Gonder(s, HttpMethod.Post, $"{Plan}/{id}/delta", new { yon = "artir" })));
        var d = await Json(await s.C.GetAsync($"{Plan}/{id}"));
        Assert.Equal(15, d.GetProperty("hedefAdet").GetInt32()); // 5 + 10 — kayıp artış yok
        var surum = d.GetProperty("surum").GetString();
        Assert.Equal(7, (await Json(await Gonder(s, HttpMethod.Put, $"{Plan}/{id}", new { aracGrupAdi = "C", hedefAdet = 7, surum }))).GetProperty("hedefAdet").GetInt32());
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Plan}/{id}", new { aracGrupAdi = "C", hedefAdet = 8, surum }), HttpStatusCode.Conflict, "cakisma");
    }

    [Fact]
    public void Izin_haritasi()
    {
        string[] onekler = [Kredi, Taksit, Siparis, Baf, Hasar, Plan];
        var uclar = fx.Web.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => onekler.Any(p => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith(p, StringComparison.Ordinal)))
            .ToList();
        Assert.Equal(41, uclar.Count); // kredi 7 + taksit 9 + sipariş 7 + BAF 5 + hasar 7 + filo plan 6
        Assert.All(uclar, e => Assert.True(e.Metadata.GetMetadata<IzinMetadata>() is not null
            || e.Metadata.GetMetadata<IzinlerdenBiriMetadata>() is not null, e.RoutePattern.RawText));
        string Izin(string yontem, string yol) => uclar.Single(e => e.RoutePattern.RawText!.Trim('/') == yol.Trim('/')
            && e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(yontem)).Metadata.GetMetadata<IzinMetadata>()!.Izin.ToString();
        Assert.Equal("FinanceWrite", Izin("POST", $"{Kredi}/{{id:guid}}/taksit-ode"));
        Assert.Equal("OperationsDelete", Izin("POST", $"{Kredi}/{{id:guid}}/iptal"));
        Assert.Equal("OperationsDelete", Izin("POST", $"{Baf}/{{id:guid}}/iptal"));
        Assert.Equal("FinanceWrite", Izin("POST", $"{Taksit}/plan"));
    }
}
