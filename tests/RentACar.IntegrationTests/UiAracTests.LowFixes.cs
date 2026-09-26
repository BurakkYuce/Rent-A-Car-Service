using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F6.2a — #278 incelemesinin Low bulguları, GERÇEK Web boru hattında. Beklenen değerler elle kurulmuş senaryodan:
/// 25 eşzamanlı yüklemede sınır 20 (sabit), sıra 0…19 (sabit küme); tarih sınırları elle seçilmiş uç günler.
/// </summary>
public sealed partial class UiAracTests
{
    [Fact]
    public async Task Query_date_out_of_year_range_is_400_with_field_not_500()
    {
        var o = await SetUpEnvironmentAsync();
        var admin = await LoginAsync(o, Kim.Admin);

        // Eskiden 0001-01-01 İstanbul ofsetiyle UTC'ye çevrilirken taşıp 500 veriyordu.
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}?tarihTuru=Tescil&tarihBas=0001-01-01"),
            HttpStatusCode.BadRequest, "dogrulama", "tarihBas");
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}?tarihTuru=Tescil&tarihBit=2101-01-01"),
            HttpStatusCode.BadRequest, "dogrulama", "tarihBit");
        // GunAraligi (rezervasyon listesi): üst sınır da alan adıyla.
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{V1}/rezervasyonlar?basMax=9999-12-31"),
            HttpStatusCode.BadRequest, "dogrulama", "basMax");
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{V1}/rezervasyonlar?basMin=1899-12-31"),
            HttpStatusCode.BadRequest, "dogrulama", "basMin");

        // Aralık içindeki uç günler kabul edilir.
        await Json(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}?tarihTuru=Tescil&tarihBas=1900-01-01&tarihBit=2100-12-31"));
        await Json(await Gonder(admin, HttpMethod.Get, $"{V1}/rezervasyonlar?basMin=1900-01-01&basMax=2100-12-31"));
    }

    [Fact]
    public async Task Enum_filter_rejects_comma_separated_flags_value()
    {
        var o = await SetUpEnvironmentAsync();
        var admin = await LoginAsync(o, Kim.Admin);

        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}?durum=Musait,Kirada"),
            HttpStatusCode.BadRequest, "dogrulama", "durum");
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}?durum=1"),
            HttpStatusCode.BadRequest, "dogrulama", "durum");
        // Birebir ad (harf duyarsız) hâlâ geçerli.
        await Json(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}?durum=musait"));
    }

    [Fact]
    public async Task Vehicle_dates_outside_reasonable_range_and_negative_costs_are_400()
    {
        var o = await SetUpEnvironmentAsync();
        var admin = await LoginAsync(o, Kim.Admin);

        await ExpectProblem(await Gonder(admin, HttpMethod.Post, SampleVehicle,
                new { plaka = Plate("34L"), sube = "SubeA", km = 0, tescilTarihi = "1949-12-31T00:00:00Z" }),
            HttpStatusCode.BadRequest, "dogrulama", "tescilTarihi");
        var tooFar = DateTimeOffset.UtcNow.AddYears(31);
        await ExpectProblem(await Gonder(admin, HttpMethod.Post, SampleVehicle,
                new { plaka = Plate("34L"), sube = "SubeA", km = 0, filoGirisTarih = tooFar }),
            HttpStatusCode.BadRequest, "dogrulama", "filoGirisTarih");
        await ExpectProblem(await Gonder(admin, HttpMethod.Post, SampleVehicle,
                new { plaka = Plate("34L"), sube = "SubeA", km = 0, alisOtv = -1m }),
            HttpStatusCode.BadRequest, "dogrulama", "alisOtv");
        await ExpectProblem(await Gonder(admin, HttpMethod.Post, SampleVehicle,
                new { plaka = Plate("34L"), sube = "SubeA", km = 0, aylikMaliyetDoviz = -0.01m }),
            HttpStatusCode.BadRequest, "dogrulama", "aylikMaliyetDoviz");

        // Km tarihi 1950 öncesi reddedilir.
        var id = await VehicleAsync(o, Plate("34K"));
        await ExpectProblem(await Gonder(admin, HttpMethod.Post, $"{SampleVehicle}/{id}/km", new { km = 1500, tarih = "1949-06-01T00:00:00Z" }),
            HttpStatusCode.BadRequest, "dogrulama", "tarih");

        // Sınır içi: 1950-01-01 ve bugün + 29 yıl, sıfır maliyet kabul edilir.
        await Json(await Gonder(admin, HttpMethod.Post, SampleVehicle, new
        {
            plaka = Plate("34L"), sube = "SubeA", km = 0, tescilTarihi = "1950-01-01T00:00:00Z",
            cikmasiPlananTarih = TestZaman.DaysLater(29 * 365), alisOtv = 0m,
        }), HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrent_photo_uploads_respect_limit_and_unique_order()
    {
        var o = await SetUpEnvironmentAsync();
        var id = await VehicleAsync(o, Plate("34R"));
        var op = await LoginAsync(o, Kim.OperatorA);
        var path = $"{SampleVehicle}/{id}/fotograflar";

        // 25 eşzamanlı yükleme: kilitsiz sayımda 20 sınırı aşılıyor ve max+1 sırası tekrar ediyordu.
        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => Gonder(op, HttpMethod.Post, path, Photo(Png()))));
        Assert.Equal(20, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest));

        var list = await Json(await Gonder(op, HttpMethod.Get, path));
        var order = list.EnumerateArray().Select(x => x.GetProperty("sira").GetInt32()).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(0, 20).ToList(), order);

        // Eşzamanlı taşımalar: sıra kümesi korunur (takas kilit altında; tekrar yok).
        var ids = list.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        await Task.WhenAll(ids.Select((f, i) => Gonder(op, HttpMethod.Post, $"{path}/{f}/{(i % 2 == 0 ? "asagi" : "yukari")}")));
        var after = await Json(await Gonder(op, HttpMethod.Get, path));
        Assert.Equal(Enumerable.Range(0, 20).ToList(),
            after.EnumerateArray().Select(x => x.GetProperty("sira").GetInt32()).OrderBy(x => x).ToList());
    }
}
