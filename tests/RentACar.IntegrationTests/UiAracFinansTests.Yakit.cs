using System.Net;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    /// <summary>Karar (3): BAF yakıtı da TEK iç ölçekte 0–12 (eskiden yüzde 0–100). 13 → 400 errors[alan].</summary>
    [Fact]
    public async Task Baf_yakit_olcegi_0_12()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o, "SubeA");
        var personel = new Domain.Entities.Personel { Kod = "PY", Ad = "Yakıt", Soyad = "Test" };
        await VeriYazAsync(o.TenantId, db => db.Personeller.Add(personel));
        var a = await GirisAsync(o, Kim.OperatorA);

        await ProblemBekle(await Gonder(a, HttpMethod.Post, Baf,
            new { personelId = personel.Id, vehicleId = arac, cikisKm = 100, cikisYakit = 13, sube = "SubeA" }),
            HttpStatusCode.BadRequest, "dogrulama", "cikisYakit");
        var olustur = await Json(await Gonder(a, HttpMethod.Post, Baf,
            new { personelId = personel.Id, vehicleId = arac, cikisKm = 100, cikisYakit = 12, sube = "SubeA" }), HttpStatusCode.Created);
        Assert.Equal(12, olustur.GetProperty("cikisYakit").GetInt32());
        var id = olustur.GetProperty("id").GetGuid();

        await ProblemBekle(await Gonder(a, HttpMethod.Post, $"{Baf}/{id}/teslim-al", new { donusKm = 150, donusYakit = 13 }),
            HttpStatusCode.BadRequest, "dogrulama", "donusYakit");
        var teslim = await Json(await Gonder(a, HttpMethod.Post, $"{Baf}/{id}/teslim-al", new { donusKm = 150, donusYakit = 9 }));
        Assert.Equal(9, teslim.GetProperty("donusYakit").GetInt32());
    }
}
