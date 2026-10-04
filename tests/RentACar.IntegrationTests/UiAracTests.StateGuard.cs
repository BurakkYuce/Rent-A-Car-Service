using System.Net;
using System.Text.Json;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    private static string FirstError(JsonElement problem, string alan)
        => problem.GetProperty("errors").GetProperty(alan).EnumerateArray().First().GetString()!;

    /// <summary>Kabul bulgusu — kart PUT'unda km geri alma ve elle Kirada/Serviste reddi ProblemDetails'te
    /// errors[km] / errors[durum] altında, Türkçe mesajla; izin verilen geçiş (Pasif) 200.</summary>
    [Fact]
    public async Task Kart_km_ve_durum_korumalari_alan_hatasi_doner()
    {
        var o = await SetUpEnvironmentAsync();
        var admin = await LoginAsync(o, Kim.Admin);
        var plate = Plate("34G");
        var create = await Json(await Gonder(admin, HttpMethod.Post, SampleVehicle, CardBody(plate)), HttpStatusCode.Created);
        var id = create.GetProperty("id").GetGuid();

        var body = CardBody(plate);
        body["surum"] = create.GetProperty("surum").GetString();
        body["km"] = 9000; // kartta 12.000 → 9.000
        var km = await ExpectProblem(await Gonder(admin, HttpMethod.Put, $"{SampleVehicle}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "km");
        Assert.Equal("KM geriye gidemez (araç odometresi 12000).", FirstError(km, "km"));

        body["km"] = 12000;
        body["durum"] = "Kirada";
        var status = await ExpectProblem(await Gonder(admin, HttpMethod.Put, $"{SampleVehicle}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "durum");
        Assert.Equal("Araç durumu elle 'Kirada' yapılamaz; kira sözleşmesinde teslimle değişir.", FirstError(status, "durum"));

        body["durum"] = "Pasif";
        body["km"] = 12500;
        var ok = await Json(await Gonder(admin, HttpMethod.Put, $"{SampleVehicle}/{id}", body));
        Assert.Equal("Pasif", ok.GetProperty("durum").GetString());
        Assert.Equal(12500, ok.GetProperty("km").GetInt32());
    }
}
