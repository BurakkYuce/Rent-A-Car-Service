using System.Net;
using System.Text.Json;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

public sealed partial class UiRezervasyonTests
{
    [Fact]
    public async Task Rezervasyon_olustur_detay_liste_guncelle_onayla_kirayaCevir()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var id = await OpenReservationAsync(s, o, vehicle, Tomorrow(), day: 3);

        // ORACLE: KDV dahil günlük 100, 3 gün → tutar 300.
        var d = await Json(await s.C.GetAsync($"{TestReservation}/{id}"));
        var r = d.GetProperty("rezervasyon");
        Assert.Equal("Rezerv", r.GetProperty("durum").GetString());
        Assert.Equal(3, r.GetProperty("gun").GetInt32());
        Assert.Equal(300m, r.GetProperty("tutar").GetDecimal());
        Assert.Equal("Ece Kaya", d.GetProperty("musteriAd").GetString());
        Assert.False(d.GetProperty("yetkiler").GetProperty("iptal").GetBoolean()); // operatörde OperationsDelete yok
        var version = r.GetProperty("surum").GetString();
        Assert.False(string.IsNullOrEmpty(version));
        Assert.DoesNotContain("tcKimlik", d.ToString(), StringComparison.OrdinalIgnoreCase);

        var l = await Json(await s.C.GetAsync($"{TestReservation}?q=Ece&durum=Rezerv&sirala=-basTar"));
        Assert.Equal(1, l.GetProperty("toplam").GetInt32());
        Assert.Equal("05321112233", l.GetProperty("kayitlar")[0].GetProperty("cepTel").GetString());

        // PUT: yalnız açıklama değişir → fiyat YENİDEN HESAPLANMAZ (300 kalır).
        var g = ReservationBody(o, vehicle, r.GetProperty("basTar").GetDateTimeOffset(), 3);
        g["aciklama"] = "not"; g["surum"] = version;
        var u = await Json(await Gonder(s, HttpMethod.Put, $"{TestReservation}/{id}", g));
        Assert.Equal("not", u.GetProperty("rezervasyon").GetProperty("aciklama").GetString());
        Assert.Equal(300m, u.GetProperty("rezervasyon").GetProperty("tutar").GetDecimal());

        // Bayat sürüm → 409 cakisma; sürümsüz → 400 errors.surum.
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{TestReservation}/{id}", g), HttpStatusCode.Conflict, "cakisma");
        g.Remove("surum");
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{TestReservation}/{id}", g), HttpStatusCode.BadRequest, "dogrulama", "surum");

        var on = await Json(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{id}/onayla"));
        Assert.Equal("Onayli", on.GetProperty("rezervasyon").GetProperty("durum").GetString());
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{id}/onayla"), HttpStatusCode.BadRequest, "dogrulama");

        var k = await Json(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{id}/kiraya-cevir"));
        var rentalId = k.GetProperty("kiraId").GetGuid();
        var rental = await Json(await s.C.GetAsync($"{V1}/kiralar/{rentalId}"));
        Assert.Equal(300m, rental.GetProperty("kira").GetProperty("tutar").GetDecimal()); // fiyat taahhüdü taşındı
        // İkinci çevirme: durum makinesi → 400, ikinci kira AÇILMAZ.
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{TestReservation}/{id}/kiraya-cevir"), HttpStatusCode.BadRequest, "dogrulama");
    }

    [Fact]
    public async Task Rezervasyon_kapsam_izin_ve_kiraci_yalitimi()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var a = await LoginAsync(o, Kim.OperatorA);
        var id = await OpenReservationAsync(a, o, vehicle, Tomorrow());
        var admin = await LoginAsync(o, Kim.Admin);
        await Json(await Gonder(admin, HttpMethod.Post, $"{TestReservation}/{id}/iptal"));

        var b = await LoginAsync(o, Kim.OperatorB);
        await ExpectProblem(await b.C.GetAsync($"{TestReservation}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        // Kapsam DURUMDAN önce: iptal edilmiş kaydı onaylamak B'de 403 (400 değil — durum sızmaz).
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{TestReservation}/{id}/onayla"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(TestReservation))).GetProperty("toplam").GetInt32());
        // Başka şubenin ofisiyle oluşturma 403; ofissiz 400 errors.cikisOfisi.
        await ExpectProblem(await Gonder(b, HttpMethod.Post, TestReservation, ReservationBody(o, vehicle, Tomorrow(5), office: "SubeA")), HttpStatusCode.Forbidden, "yetki_yok");
        var withoutOffice = ReservationBody(o, vehicle, Tomorrow(5)); withoutOffice["cikisOfisi"] = null;
        await ExpectProblem(await Gonder(b, HttpMethod.Post, TestReservation, withoutOffice), HttpStatusCode.BadRequest, "dogrulama", "cikisOfisi");
        // İzinsiz rol (Muhasebe: OperationsWrite yok) ve operatörün iptal denemesi.
        var mh = await LoginAsync(o, Kim.Muhasebe);
        await ExpectProblem(await mh.C.GetAsync(TestReservation), HttpStatusCode.Forbidden, "yetki_yok");
        var id2 = await OpenReservationAsync(a, o, vehicle, Tomorrow(10));
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(a, HttpMethod.Post, $"{TestReservation}/{id2}/iptal")).StatusCode);

        // Başka kiracı: 404 (RLS).
        var o2 = await SetUpEnvironmentAsync();
        var x = await LoginAsync(o2, Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{TestReservation}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(x, HttpMethod.Post, $"{TestReservation}/{id}/onayla")).StatusCode);
        // Başka kiracının müşterisiyle kayıt: 400 errors.musteriId.
        var foreign = ReservationBody(o2, await VehicleAsync(o2), Tomorrow(3), office: "SubeA"); foreign["musteriId"] = o.MusteriId;
        await ExpectProblem(await Gonder(x, HttpMethod.Post, TestReservation, foreign), HttpStatusCode.BadRequest, "dogrulama", "musteriId");
    }

    [Fact]
    public async Task Rezervasyon_uc_sinirlari_ve_anonim_musteri()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);
        var g = ReservationBody(o, vehicle, Tomorrow()); g["gunlukUcret"] = 1e12m;
        await ExpectProblem(await Gonder(s, HttpMethod.Post, TestReservation, g), HttpStatusCode.BadRequest, "dogrulama", "gunlukUcret");
        g = ReservationBody(o, vehicle, Tomorrow()); g["aciklama"] = new string('x', 1100);
        await ExpectProblem(await Gonder(s, HttpMethod.Post, TestReservation, g), HttpStatusCode.BadRequest, "dogrulama", "aciklama");
        g = ReservationBody(o, vehicle, Tomorrow()); g["bitTar"] = Tomorrow().AddYears(10);
        await ExpectProblem(await Gonder(s, HttpMethod.Post, TestReservation, g), HttpStatusCode.BadRequest, "dogrulama", "bitTar");
        await ExpectProblem(await s.C.GetAsync($"{TestReservation}?durum=Yok"), HttpStatusCode.BadRequest, "dogrulama", "durum");
        await ExpectProblem(await s.C.GetAsync($"{TestReservation}?sirala=hack"), HttpStatusCode.BadRequest, "dogrulama", "sirala");

        // KVKK: AnonimAd + AnonimTelefon → listede sabit etiket, telefon yok.
        var anonymous = new Customer { Tip = RentACar.Domain.Enums.CustomerType.Bireysel, Ad = "Gizli", Soyad = "Kişi", CepTel = "05329998877", AnonimAd = true, AnonimTelefon = true };
        await WriteDataAsync(o.TenantId, db => db.Customers.Add(anonymous));
        g = ReservationBody(o, vehicle, Tomorrow()); g["musteriId"] = anonymous.Id;
        await Json(await Gonder(s, HttpMethod.Post, TestReservation, g), HttpStatusCode.Created);
        var l = await Json(await s.C.GetAsync(TestReservation));
        var row = l.GetProperty("kayitlar")[0];
        Assert.Equal("Anonim müşteri", row.GetProperty("musteriAd").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("cepTel").ValueKind);
        Assert.DoesNotContain("05329998877", l.ToString());
    }
}
