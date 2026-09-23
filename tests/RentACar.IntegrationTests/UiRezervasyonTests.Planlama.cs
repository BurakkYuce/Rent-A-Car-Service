using System.Globalization;
using System.Net;

namespace RentACar.IntegrationTests;

public sealed partial class UiRezervasyonTests
{
    private const string Teklif = V1 + "/teklifler";

    [Fact]
    public async Task Teklif_olustur_gonder_kabul_ve_kapsam()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var a = await GirisAsync(o, Kim.OperatorA);
        var bas = Yarin(2);
        var olustur = await Json(await Gonder(a, HttpMethod.Post, Teklif, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas, bitTar = bas.AddDays(2),
            gunlukUcret = 150m, fiyatTuru = "KDV Dahil Günlük", cikisOfisi = "SubeA", gecerlilikTarihi = bas.AddDays(1),
        }), HttpStatusCode.Created);
        var id = olustur.GetProperty("id").GetGuid();

        // ORACLE: 150 × 2 gün = 300.
        var d = await Json(await a.C.GetAsync($"{Teklif}/{id}"));
        Assert.Equal(300m, d.GetProperty("teklif").GetProperty("tutar").GetDecimal());
        Assert.Equal("Taslak", d.GetProperty("teklif").GetProperty("durum").GetString());
        var l = await Json(await a.C.GetAsync(Teklif));
        Assert.Equal("Ece Kaya", l.GetProperty("kayitlar")[0].GetProperty("musteriAd").GetString());

        var b = await GirisAsync(o, Kim.OperatorB);
        await ProblemBekle(await Gonder(b, HttpMethod.Post, $"{Teklif}/{id}/gonder"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Teklif))).GetProperty("toplam").GetInt32());

        Assert.Equal("Gonderildi", (await Json(await Gonder(a, HttpMethod.Post, $"{Teklif}/{id}/gonder")))
            .GetProperty("teklif").GetProperty("durum").GetString());
        var kabul = await Json(await Gonder(a, HttpMethod.Post, $"{Teklif}/{id}/kabul"));
        var rezId = kabul.GetProperty("rezervasyonId").GetGuid();
        Assert.Equal(300m, (await Json(await a.C.GetAsync($"{Rez}/{rezId}"))).GetProperty("rezervasyon").GetProperty("tutar").GetDecimal());
        // İkinci kabul: 409 cakisma (F5.1 adversarial H1 — eşzamanlı kabulle aynı sözleşme); kabul sonrası red: durum
        // makinesi → 400; ikinci rezervasyon açılmaz.
        await ProblemBekle(await Gonder(a, HttpMethod.Post, $"{Teklif}/{id}/kabul"), HttpStatusCode.Conflict, "cakisma");
        await ProblemBekle(await Gonder(a, HttpMethod.Post, $"{Teklif}/{id}/reddet"), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(1, (await Json(await a.C.GetAsync(Rez))).GetProperty("toplam").GetInt32());
    }

    [Fact]
    public async Task Takvim_izgarasi_kira_rezervasyonu_ezer()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var bos = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);
        // İstanbul 12:00 = 09:00Z; gelecek ayın 10'u → 13'ü (3 gün) rezervasyon.
        var ilk = DateTime.UtcNow.AddMonths(1);
        var bas = new DateTimeOffset(ilk.Year, ilk.Month, 10, 9, 0, 0, TimeSpan.Zero);
        var rezId = await RezAcAsync(s, o, arac, bas, gun: 3, ofis: "SubeA");

        var ay = bas.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var t = await Json(await s.C.GetAsync($"{V1}/takvim?ay={ay}"));
        Assert.Equal(DateTime.DaysInMonth(bas.Year, bas.Month), t.GetProperty("gunSayisi").GetInt32());
        var satir = t.GetProperty("araclar").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == arac);
        var gunler = satir.GetProperty("gunler");
        // Oracle: 10, 11, 12, 13. günler dolu (13'ü 12:00'ye kadar); 9 ve 14 boş.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, gunler[8].ValueKind);
        for (var i = 9; i <= 12; i++) Assert.Equal("Rezervasyon", gunler[i].GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, gunler[13].ValueKind);
        Assert.All(t.GetProperty("araclar").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == bos)
            .GetProperty("gunler").EnumerateArray(), g => Assert.Equal(System.Text.Json.JsonValueKind.Null, g.ValueKind));

        await ProblemBekle(await s.C.GetAsync($"{V1}/takvim?ay=2026-13"), HttpStatusCode.BadRequest, "dogrulama", "ay");
        var sec = await Json(await s.C.GetAsync($"{V1}/takvim/secenekler"));
        Assert.Contains("C", sec.GetProperty("gruplar").EnumerateArray().Select(x => x.GetString()));
        _ = rezId;
    }

    [Fact]
    public async Task Musaitlik_dolu_araci_eler_ve_kira_sorgusunu_uretir()
    {
        var o = await OrtamKurAsync();
        var dolu = await AracAsync(o);
        var serbest = await AracAsync(o);
        var s = await GirisAsync(o, Kim.OperatorA);
        var gun = DateTime.UtcNow.Date.AddDays(20);
        await RezAcAsync(s, o, dolu, new DateTimeOffset(gun.AddHours(9), TimeSpan.Zero), gun: 3);

        var basGun = gun.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var m = await Json(await s.C.GetAsync($"{V1}/musaitlik?basGun={basGun}&gun=2&grup=C"));
        var idler = m.GetProperty("araclar").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(serbest, idler);
        Assert.DoesNotContain(dolu, idler);
        var sorgu = m.GetProperty("kiralaSorgusu");
        Assert.Equal(basGun, sorgu.GetProperty("vfrom").GetString());
        Assert.Equal(gun.AddDays(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), sorgu.GetProperty("vto").GetString());
        Assert.Equal("C", sorgu.GetProperty("vgrup").GetString());

        await ProblemBekle(await s.C.GetAsync($"{V1}/musaitlik?basGun={basGun}"), HttpStatusCode.BadRequest, "dogrulama", "bitGun");
        await ProblemBekle(await s.C.GetAsync($"{V1}/musaitlik"), HttpStatusCode.BadRequest, "dogrulama", "basGun");
        await ProblemBekle(await s.C.GetAsync($"{V1}/musaitlik?basGun={basGun}&gun=400"), HttpStatusCode.BadRequest, "dogrulama", "gun");
        var mh = await GirisAsync(o, Kim.Muhasebe);
        await ProblemBekle(await mh.C.GetAsync($"{V1}/musaitlik?basGun={basGun}&gun=2"), HttpStatusCode.Forbidden, "yetki_yok");
    }
}
