using System.Net;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    private static Dictionary<string, object?> CardBody(string plate, string branch = "SubeA") => new()
    {
        ["plaka"] = plate, ["marka"] = "Toyota", ["tip"] = "Corolla", ["grup"] = "C", ["sube"] = branch,
        ["durum"] = "Musait", ["yakit"] = "Dizel", ["vites"] = "Otomatik", ["km"] = 12000, ["modelYili"] = 2024,
        ["alimBedeli"] = 950000.50m, ["aciklama"] = "ilk kayıt", ["karLastigi"] = true,
    };

    [Fact]
    public async Task Kart_olustur_guncelle_surum_ve_kapsam()
    {
        var o = await SetUpEnvironmentAsync();
        var opA = await LoginAsync(o, Kim.OperatorA);
        var plate = Plate("34 K ");
        var create = await Json(await Gonder(opA, HttpMethod.Post, SampleVehicle, CardBody(plate)), HttpStatusCode.Created);
        var id = create.GetProperty("id").GetGuid();
        Assert.Equal(plate.Replace(" ", "").ToUpperInvariant(), create.GetProperty("plaka").GetString()); // normalize
        Assert.Equal(950000.50m, create.GetProperty("alimBedeli").GetDecimal());
        Assert.Equal("Dizel", create.GetProperty("yakit").GetString());
        Assert.True(create.GetProperty("karLastigi").GetBoolean());
        var version1 = create.GetProperty("surum").GetString();
        Assert.False(string.IsNullOrEmpty(version1));

        // Aynı plaka → 400 errors[plaka]; başka şubeye araç açmak → 403.
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, SampleVehicle, CardBody(plate)), HttpStatusCode.BadRequest, "dogrulama", "plaka");
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, SampleVehicle, CardBody(Plate("34X"), "SubeB")), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, SampleVehicle, CardBody(Plate("34Y"), "")), HttpStatusCode.BadRequest, "dogrulama", "sube");

        // PUT: surum yok → 400; doğru surum → 200 ve alanlar değişir; eski surum → 409 cakisma.
        var current = CardBody(plate);
        current["km"] = 15000;
        current["aciklama"] = "güncellendi";
        await ExpectProblem(await Gonder(opA, HttpMethod.Put, $"{SampleVehicle}/{id}", current), HttpStatusCode.BadRequest, "dogrulama", "surum");
        current["surum"] = version1;
        var g = await Json(await Gonder(opA, HttpMethod.Put, $"{SampleVehicle}/{id}", current));
        Assert.Equal(15000, g.GetProperty("km").GetInt32());
        Assert.Equal("güncellendi", g.GetProperty("aciklama").GetString());
        Assert.NotEqual(version1, g.GetProperty("surum").GetString());
        await ExpectProblem(await Gonder(opA, HttpMethod.Put, $"{SampleVehicle}/{id}", current), HttpStatusCode.Conflict, "cakisma");

        // Aracı başka şubeye taşımak kapsam dışı → 403 (kayıt değişmez).
        var move = CardBody(plate, "SubeB");
        move["surum"] = g.GetProperty("surum").GetString();
        await ExpectProblem(await Gonder(opA, HttpMethod.Put, $"{SampleVehicle}/{id}", move), HttpStatusCode.Forbidden, "yetki_yok");

        // Operatör B: başka şubenin aracı 403 (kart, detay, güncelle); olmayan kimlik 404.
        var opB = await LoginAsync(o, Kim.OperatorB);
        await ExpectProblem(await Gonder(opB, HttpMethod.Get, $"{SampleVehicle}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(opB, HttpMethod.Get, $"{SampleVehicle}/{id}/detay"), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(opB, HttpMethod.Put, $"{SampleVehicle}/{id}", current), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(opB, HttpMethod.Get, $"{SampleVehicle}/{Guid.NewGuid()}"), HttpStatusCode.NotFound, null);

        // Başka kiracının aracı 404 (RLS).
        var o2 = await SetUpEnvironmentAsync();
        var foreign = await VehicleAsync(o2, Plate("35Z"));
        var admin = await LoginAsync(o, Kim.Admin);
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}/{foreign}"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Gonder(admin, HttpMethod.Delete, $"{SampleVehicle}/{foreign}"), HttpStatusCode.NotFound, null);

        // Silme: operatör (OperationsDelete yok) 403; admin 204, sonra 404.
        await ExpectProblem(await Gonder(opA, HttpMethod.Delete, $"{SampleVehicle}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(admin, HttpMethod.Delete, $"{SampleVehicle}/{id}")).StatusCode);
        await ExpectProblem(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}/{id}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Kart_sinirlari_ve_km_serisi()
    {
        var o = await SetUpEnvironmentAsync();
        var admin = await LoginAsync(o, Kim.Admin);
        async Task Red(string alan, object? value)
        {
            var body = CardBody(Plate("34S"));
            body[alan] = value;
            await ExpectProblem(await Gonder(admin, HttpMethod.Post, SampleVehicle, body), HttpStatusCode.BadRequest, "dogrulama", alan);
        }
        await Red("aciklama", new string('x', 1025));          // varchar(1024)
        await Red("marka", new string('m', 65));               // varchar(64)
        await Red("alimBedeli", 1_000_000_000_000m);           // numeric(19,4) uç sınırı 1e8
        await Red("alimBedeli", -1m);                          // servis: negatif bedel
        await Red("durum", "Uçuyor");                          // tanımsız enum adı
        await Red("km", -5);
        await Red("modelYili", 1900);
        await Red("sipp", "ABC");
        await Red("kiraMusteriId", Guid.NewGuid());            // bu kiracıda olmayan cari

        var id = (await Json(await Gonder(admin, HttpMethod.Post, SampleVehicle, CardBody(Plate("34M"))), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(admin, HttpMethod.Post, $"{SampleVehicle}/{id}/km", new { km = 13000 })).StatusCode);
        await ExpectProblem(await Gonder(admin, HttpMethod.Post, $"{SampleVehicle}/{id}/km", new { km = 12500 }), HttpStatusCode.BadRequest, "dogrulama", "km");
        await ExpectProblem(await Gonder(admin, HttpMethod.Post, $"{SampleVehicle}/{id}/km",
            new { km = 14000, tarih = DateTimeOffset.UtcNow.AddDays(2) }), HttpStatusCode.BadRequest, "dogrulama", "tarih");

        var detail = await Json(await Gonder(admin, HttpMethod.Get, $"{SampleVehicle}/{id}/detay"));
        Assert.Equal(13000, detail.GetProperty("km").GetInt32());
        var record = Assert.Single(detail.GetProperty("kmKayitlari").EnumerateArray());
        Assert.Equal(13000, record.GetProperty("km").GetInt32());
        Assert.Equal("Manuel", record.GetProperty("kaynak").GetString());
    }
}
