using System.Globalization;
using System.Net;

namespace RentACar.IntegrationTests;

public sealed partial class UiRezervasyonTests
{
    private const string TestQuotation = V1 + "/teklifler";

    [Fact]
    public async Task Teklif_olustur_gonder_kabul_ve_kapsam()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var a = await LoginAsync(o, Kim.OperatorA);
        var start = Tomorrow(2);
        var create = await Json(await Gonder(a, HttpMethod.Post, TestQuotation, new
        {
            musteriId = o.MusteriId, vehicleId = vehicle, basTar = start, bitTar = start.AddDays(2),
            gunlukUcret = 150m, fiyatTuru = "KDV Dahil Günlük", cikisOfisi = "SubeA", gecerlilikTarihi = start.AddDays(1),
        }), HttpStatusCode.Created);
        var id = create.GetProperty("id").GetGuid();

        // ORACLE: 150 × 2 gün = 300.
        var d = await Json(await a.C.GetAsync($"{TestQuotation}/{id}"));
        Assert.Equal(300m, d.GetProperty("teklif").GetProperty("tutar").GetDecimal());
        Assert.Equal("Taslak", d.GetProperty("teklif").GetProperty("durum").GetString());
        var l = await Json(await a.C.GetAsync(TestQuotation));
        Assert.Equal("Ece Kaya", l.GetProperty("kayitlar")[0].GetProperty("musteriAd").GetString());

        var b = await LoginAsync(o, Kim.OperatorB);
        await ExpectProblem(await Gonder(b, HttpMethod.Post, $"{TestQuotation}/{id}/gonder"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(TestQuotation))).GetProperty("toplam").GetInt32());

        Assert.Equal("Gonderildi", (await Json(await Gonder(a, HttpMethod.Post, $"{TestQuotation}/{id}/gonder")))
            .GetProperty("teklif").GetProperty("durum").GetString());
        var accept = await Json(await Gonder(a, HttpMethod.Post, $"{TestQuotation}/{id}/kabul"));
        var resId = accept.GetProperty("rezervasyonId").GetGuid();
        Assert.Equal(300m, (await Json(await a.C.GetAsync($"{TestReservation}/{resId}"))).GetProperty("rezervasyon").GetProperty("tutar").GetDecimal());
        // İkinci kabul: 409 cakisma (F5.1 adversarial H1 — eşzamanlı kabulle aynı sözleşme); kabul sonrası red: durum
        // makinesi → 400; ikinci rezervasyon açılmaz.
        await ExpectProblem(await Gonder(a, HttpMethod.Post, $"{TestQuotation}/{id}/kabul"), HttpStatusCode.Conflict, "cakisma");
        await ExpectProblem(await Gonder(a, HttpMethod.Post, $"{TestQuotation}/{id}/reddet"), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(1, (await Json(await a.C.GetAsync(TestReservation))).GetProperty("toplam").GetInt32());
    }

    [Fact]
    public async Task Takvim_izgarasi_kira_rezervasyonu_ezer()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var empty = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);
        // İstanbul 12:00 = 09:00Z; gelecek ayın 10'u → 13'ü (3 gün) rezervasyon.
        var first = DateTime.UtcNow.AddMonths(1);
        var start = new DateTimeOffset(first.Year, first.Month, 10, 9, 0, 0, TimeSpan.Zero);
        var resId = await OpenReservationAsync(s, o, vehicle, start, day: 3, office: "SubeA");

        var month = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var t = await Json(await s.C.GetAsync($"{V1}/takvim?ay={month}"));
        Assert.Equal(DateTime.DaysInMonth(start.Year, start.Month), t.GetProperty("gunSayisi").GetInt32());
        var row = t.GetProperty("araclar").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == vehicle);
        var gunler = row.GetProperty("gunler");
        // Oracle: 10, 11, 12, 13. günler dolu (13'ü 12:00'ye kadar); 9 ve 14 boş.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, gunler[8].ValueKind);
        for (var i = 9; i <= 12; i++) Assert.Equal("Rezervasyon", gunler[i].GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, gunler[13].ValueKind);
        Assert.All(t.GetProperty("araclar").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == empty)
            .GetProperty("gunler").EnumerateArray(), g => Assert.Equal(System.Text.Json.JsonValueKind.Null, g.ValueKind));

        await ExpectProblem(await s.C.GetAsync($"{V1}/takvim?ay=2026-13"), HttpStatusCode.BadRequest, "dogrulama", "ay");
        var select = await Json(await s.C.GetAsync($"{V1}/takvim/secenekler"));
        Assert.Contains("C", select.GetProperty("gruplar").EnumerateArray().Select(x => x.GetString()));
        _ = resId;
    }

    [Fact]
    public async Task Musaitlik_dolu_araci_eler_ve_kira_sorgusunu_uretir()
    {
        var o = await SetUpEnvironmentAsync();
        var filled = await VehicleAsync(o);
        var free = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.OperatorA);
        var day = DateTime.UtcNow.Date.AddDays(20);
        await OpenReservationAsync(s, o, filled, new DateTimeOffset(day.AddHours(9), TimeSpan.Zero), day: 3);

        var startDay = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var m = await Json(await s.C.GetAsync($"{V1}/musaitlik?basGun={startDay}&gun=2&grup=C"));
        var ids = m.GetProperty("araclar").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(free, ids);
        Assert.DoesNotContain(filled, ids);
        var query = m.GetProperty("kiralaSorgusu");
        Assert.Equal(startDay, query.GetProperty("vfrom").GetString());
        Assert.Equal(day.AddDays(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), query.GetProperty("vto").GetString());
        Assert.Equal("C", query.GetProperty("vgrup").GetString());

        await ExpectProblem(await s.C.GetAsync($"{V1}/musaitlik?basGun={startDay}"), HttpStatusCode.BadRequest, "dogrulama", "bitGun");
        await ExpectProblem(await s.C.GetAsync($"{V1}/musaitlik"), HttpStatusCode.BadRequest, "dogrulama", "basGun");
        await ExpectProblem(await s.C.GetAsync($"{V1}/musaitlik?basGun={startDay}&gun=400"), HttpStatusCode.BadRequest, "dogrulama", "gun");
        var mh = await LoginAsync(o, Kim.Muhasebe);
        await ExpectProblem(await mh.C.GetAsync($"{V1}/musaitlik?basGun={startDay}&gun=2"), HttpStatusCode.Forbidden, "yetki_yok");
    }
}
