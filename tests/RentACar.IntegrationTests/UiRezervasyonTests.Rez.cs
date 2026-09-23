using System.Net;
using System.Text.Json;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

public sealed partial class UiRezervasyonTests
{
    [Fact]
    public async Task Rezervasyon_olustur_detay_liste_guncelle_onayla_kirayaCevir()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var id = await RezAcAsync(s, o, arac, Yarin(), gun: 3);

        // ORACLE: KDV dahil günlük 100, 3 gün → tutar 300.
        var d = await Json(await s.C.GetAsync($"{Rez}/{id}"));
        var r = d.GetProperty("rezervasyon");
        Assert.Equal("Rezerv", r.GetProperty("durum").GetString());
        Assert.Equal(3, r.GetProperty("gun").GetInt32());
        Assert.Equal(300m, r.GetProperty("tutar").GetDecimal());
        Assert.Equal("Ece Kaya", d.GetProperty("musteriAd").GetString());
        Assert.False(d.GetProperty("yetkiler").GetProperty("iptal").GetBoolean()); // operatörde OperationsDelete yok
        var surum = r.GetProperty("surum").GetString();
        Assert.False(string.IsNullOrEmpty(surum));
        Assert.DoesNotContain("tcKimlik", d.ToString(), StringComparison.OrdinalIgnoreCase);

        var l = await Json(await s.C.GetAsync($"{Rez}?q=Ece&durum=Rezerv&sirala=-basTar"));
        Assert.Equal(1, l.GetProperty("toplam").GetInt32());
        Assert.Equal("05321112233", l.GetProperty("kayitlar")[0].GetProperty("cepTel").GetString());

        // PUT: yalnız açıklama değişir → fiyat YENİDEN HESAPLANMAZ (300 kalır).
        var g = RezGovde(o, arac, r.GetProperty("basTar").GetDateTimeOffset(), 3);
        g["aciklama"] = "not"; g["surum"] = surum;
        var u = await Json(await Gonder(s, HttpMethod.Put, $"{Rez}/{id}", g));
        Assert.Equal("not", u.GetProperty("rezervasyon").GetProperty("aciklama").GetString());
        Assert.Equal(300m, u.GetProperty("rezervasyon").GetProperty("tutar").GetDecimal());

        // Bayat sürüm → 409 cakisma; sürümsüz → 400 errors.surum.
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Rez}/{id}", g), HttpStatusCode.Conflict, "cakisma");
        g.Remove("surum");
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Rez}/{id}", g), HttpStatusCode.BadRequest, "dogrulama", "surum");

        var on = await Json(await Gonder(s, HttpMethod.Post, $"{Rez}/{id}/onayla"));
        Assert.Equal("Onayli", on.GetProperty("rezervasyon").GetProperty("durum").GetString());
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Rez}/{id}/onayla"), HttpStatusCode.BadRequest, "dogrulama");

        var k = await Json(await Gonder(s, HttpMethod.Post, $"{Rez}/{id}/kiraya-cevir"));
        var kiraId = k.GetProperty("kiraId").GetGuid();
        var kira = await Json(await s.C.GetAsync($"{V1}/kiralar/{kiraId}"));
        Assert.Equal(300m, kira.GetProperty("kira").GetProperty("tutar").GetDecimal()); // fiyat taahhüdü taşındı
        // İkinci çevirme: durum makinesi → 400, ikinci kira AÇILMAZ.
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Rez}/{id}/kiraya-cevir"), HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Rezervasyon_kapsam_izin_ve_kiraci_yalitimi()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var a = await GirisAsync(o, Kim.OperatorA);
        var id = await RezAcAsync(a, o, arac, Yarin());
        var admin = await GirisAsync(o, Kim.Admin);
        await Json(await Gonder(admin, HttpMethod.Post, $"{Rez}/{id}/iptal"));

        var b = await GirisAsync(o, Kim.OperatorB);
        await ProblemBekle(await b.C.GetAsync($"{Rez}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        // Kapsam DURUMDAN önce: iptal edilmiş kaydı onaylamak B'de 403 (400 değil — durum sızmaz).
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Rez}/{id}/onayla"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Rez))).GetProperty("toplam").GetInt32());
        // Başka şubenin ofisiyle oluşturma 403; ofissiz 400 errors.cikisOfisi.
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Rez, RezGovde(o, arac, Yarin(5), ofis: "SubeA")), HttpStatusCode.Forbidden, "yetki_yok");
        var ofissiz = RezGovde(o, arac, Yarin(5)); ofissiz["cikisOfisi"] = null;
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Rez, ofissiz), HttpStatusCode.BadRequest, "dogrulama", "cikisOfisi");
        // İzinsiz rol (Muhasebe: OperationsWrite yok) ve operatörün iptal denemesi.
        var mh = await GirisAsync(o, Kim.Muhasebe);
        await ProblemBekle(await mh.C.GetAsync(Rez), HttpStatusCode.Forbidden, "yetki_yok");
        var id2 = await RezAcAsync(a, o, arac, Yarin(10));
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(a, HttpMethod.Post, $"{Rez}/{id2}/iptal")).StatusCode);

        // Başka kiracı: 404 (RLS).
        var o2 = await OrtamKurAsync();
        var x = await GirisAsync(o2, Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Rez}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(x, HttpMethod.Post, $"{Rez}/{id}/onayla")).StatusCode);
        // Başka kiracının müşterisiyle kayıt: 400 errors.musteriId.
        var yabanci = RezGovde(o2, await AracAsync(o2), Yarin(3), ofis: "SubeA"); yabanci["musteriId"] = o.MusteriId;
        await ProblemBekle(await Gonder(x, HttpMethod.Post, Rez, yabanci), HttpStatusCode.BadRequest, "dogrulama", "musteriId");
    }

    [Fact]
    public async Task Rezervasyon_uc_sinirlari_ve_anonim_musteri()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);
        var g = RezGovde(o, arac, Yarin()); g["gunlukUcret"] = 1e12m;
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Rez, g), HttpStatusCode.BadRequest, "dogrulama", "gunlukUcret");
        g = RezGovde(o, arac, Yarin()); g["aciklama"] = new string('x', 1100);
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Rez, g), HttpStatusCode.BadRequest, "dogrulama", "aciklama");
        g = RezGovde(o, arac, Yarin()); g["bitTar"] = Yarin().AddYears(10);
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Rez, g), HttpStatusCode.BadRequest, "dogrulama", "bitTar");
        await ProblemBekle(await s.C.GetAsync($"{Rez}?durum=Yok"), HttpStatusCode.BadRequest, "dogrulama", "durum");
        await ProblemBekle(await s.C.GetAsync($"{Rez}?sirala=hack"), HttpStatusCode.BadRequest, "dogrulama", "sirala");

        // KVKK: AnonimAd + AnonimTelefon → listede sabit etiket, telefon yok.
        var anonim = new Customer { Tip = RentACar.Domain.Enums.CariType.Bireysel, Ad = "Gizli", Soyad = "Kişi", CepTel = "05329998877", AnonimAd = true, AnonimTelefon = true };
        await VeriYazAsync(o.TenantId, db => db.Customers.Add(anonim));
        g = RezGovde(o, arac, Yarin()); g["musteriId"] = anonim.Id;
        await Json(await Gonder(s, HttpMethod.Post, Rez, g), HttpStatusCode.Created);
        var l = await Json(await s.C.GetAsync(Rez));
        var satir = l.GetProperty("kayitlar")[0];
        Assert.Equal("Anonim müşteri", satir.GetProperty("musteriAd").GetString());
        Assert.Equal(JsonValueKind.Null, satir.GetProperty("cepTel").ValueKind);
        Assert.DoesNotContain("05329998877", l.ToString());
    }
}
