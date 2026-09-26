using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    /// <summary>Karar (3): BAF yakıtı da TEK iç ölçekte 0–12 (eskiden yüzde 0–100). 13 → 400 errors[alan].</summary>
    [Fact]
    public async Task Baf_yakit_olcegi_0_12()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o, "SubeA");
        var staff = new Domain.Entities.Personel { Kod = "PY", Ad = "Yakıt", Soyad = "Test" };
        await WriteDataAsync(o.TenantId, db => db.Personeller.Add(staff));
        var a = await LoginAsync(o, Kim.OperatorA);

        await ExpectProblem(await Gonder(a, HttpMethod.Post, Baf,
            new { personelId = staff.Id, vehicleId = vehicle, cikisKm = 100, cikisYakit = 13, sube = "SubeA" }),
            HttpStatusCode.BadRequest, "dogrulama", "cikisYakit");
        var create = await Json(await Gonder(a, HttpMethod.Post, Baf,
            new { personelId = staff.Id, vehicleId = vehicle, cikisKm = 100, cikisYakit = 12, sube = "SubeA" }), HttpStatusCode.Created);
        Assert.Equal(12, create.GetProperty("cikisYakit").GetInt32());
        var id = create.GetProperty("id").GetGuid();

        await ExpectProblem(await Gonder(a, HttpMethod.Post, $"{Baf}/{id}/teslim-al", new { donusKm = 150, donusYakit = 13 }),
            HttpStatusCode.BadRequest, "dogrulama", "donusYakit");
        var delivery = await Json(await Gonder(a, HttpMethod.Post, $"{Baf}/{id}/teslim-al", new { donusKm = 150, donusYakit = 9 }));
        Assert.Equal(9, delivery.GetProperty("donusYakit").GetInt32());
    }
}
