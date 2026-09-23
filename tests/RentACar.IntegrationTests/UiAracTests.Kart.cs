using System.Net;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    private static Dictionary<string, object?> KartGovde(string plaka, string sube = "SubeA") => new()
    {
        ["plaka"] = plaka, ["marka"] = "Toyota", ["tip"] = "Corolla", ["grup"] = "C", ["sube"] = sube,
        ["durum"] = "Musait", ["yakit"] = "Dizel", ["vites"] = "Otomatik", ["km"] = 12000, ["modelYili"] = 2024,
        ["alimBedeli"] = 950000.50m, ["aciklama"] = "ilk kayıt", ["karLastigi"] = true,
    };

    [Fact]
    public async Task Kart_olustur_guncelle_surum_ve_kapsam()
    {
        var o = await OrtamKurAsync();
        var opA = await GirisAsync(o, Kim.OperatorA);
        var plaka = Plaka("34 K ");
        var olustur = await Json(await Gonder(opA, HttpMethod.Post, Arac, KartGovde(plaka)), HttpStatusCode.Created);
        var id = olustur.GetProperty("id").GetGuid();
        Assert.Equal(plaka.Replace(" ", "").ToUpperInvariant(), olustur.GetProperty("plaka").GetString()); // normalize
        Assert.Equal(950000.50m, olustur.GetProperty("alimBedeli").GetDecimal());
        Assert.Equal("Dizel", olustur.GetProperty("yakit").GetString());
        Assert.True(olustur.GetProperty("karLastigi").GetBoolean());
        var surum1 = olustur.GetProperty("surum").GetString();
        Assert.False(string.IsNullOrEmpty(surum1));

        // Aynı plaka → 400 errors[plaka]; başka şubeye araç açmak → 403.
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, Arac, KartGovde(plaka)), HttpStatusCode.BadRequest, "dogrulama", "plaka");
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, Arac, KartGovde(Plaka("34X"), "SubeB")), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, Arac, KartGovde(Plaka("34Y"), "")), HttpStatusCode.BadRequest, "dogrulama", "sube");

        // PUT: surum yok → 400; doğru surum → 200 ve alanlar değişir; eski surum → 409 cakisma.
        var guncel = KartGovde(plaka);
        guncel["km"] = 15000;
        guncel["aciklama"] = "güncellendi";
        await ProblemBekle(await Gonder(opA, HttpMethod.Put, $"{Arac}/{id}", guncel), HttpStatusCode.BadRequest, "dogrulama", "surum");
        guncel["surum"] = surum1;
        var g = await Json(await Gonder(opA, HttpMethod.Put, $"{Arac}/{id}", guncel));
        Assert.Equal(15000, g.GetProperty("km").GetInt32());
        Assert.Equal("güncellendi", g.GetProperty("aciklama").GetString());
        Assert.NotEqual(surum1, g.GetProperty("surum").GetString());
        await ProblemBekle(await Gonder(opA, HttpMethod.Put, $"{Arac}/{id}", guncel), HttpStatusCode.Conflict, "cakisma");

        // Aracı başka şubeye taşımak kapsam dışı → 403 (kayıt değişmez).
        var tasi = KartGovde(plaka, "SubeB");
        tasi["surum"] = g.GetProperty("surum").GetString();
        await ProblemBekle(await Gonder(opA, HttpMethod.Put, $"{Arac}/{id}", tasi), HttpStatusCode.Forbidden, "yetki_yok");

        // Operatör B: başka şubenin aracı 403 (kart, detay, güncelle); olmayan kimlik 404.
        var opB = await GirisAsync(o, Kim.OperatorB);
        await ProblemBekle(await Gonder(opB, HttpMethod.Get, $"{Arac}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(opB, HttpMethod.Get, $"{Arac}/{id}/detay"), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(opB, HttpMethod.Put, $"{Arac}/{id}", guncel), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(opB, HttpMethod.Get, $"{Arac}/{Guid.NewGuid()}"), HttpStatusCode.NotFound, null);

        // Başka kiracının aracı 404 (RLS).
        var o2 = await OrtamKurAsync();
        var yabanci = await AracAsync(o2, Plaka("35Z"));
        var admin = await GirisAsync(o, Kim.Admin);
        await ProblemBekle(await Gonder(admin, HttpMethod.Get, $"{Arac}/{yabanci}"), HttpStatusCode.NotFound, null);
        await ProblemBekle(await Gonder(admin, HttpMethod.Delete, $"{Arac}/{yabanci}"), HttpStatusCode.NotFound, null);

        // Silme: operatör (OperationsDelete yok) 403; admin 204, sonra 404.
        await ProblemBekle(await Gonder(opA, HttpMethod.Delete, $"{Arac}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(admin, HttpMethod.Delete, $"{Arac}/{id}")).StatusCode);
        await ProblemBekle(await Gonder(admin, HttpMethod.Get, $"{Arac}/{id}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Kart_sinirlari_ve_km_serisi()
    {
        var o = await OrtamKurAsync();
        var admin = await GirisAsync(o, Kim.Admin);
        async Task Red(string alan, object? deger)
        {
            var govde = KartGovde(Plaka("34S"));
            govde[alan] = deger;
            await ProblemBekle(await Gonder(admin, HttpMethod.Post, Arac, govde), HttpStatusCode.BadRequest, "dogrulama", alan);
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

        var id = (await Json(await Gonder(admin, HttpMethod.Post, Arac, KartGovde(Plaka("34M"))), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(admin, HttpMethod.Post, $"{Arac}/{id}/km", new { km = 13000 })).StatusCode);
        await ProblemBekle(await Gonder(admin, HttpMethod.Post, $"{Arac}/{id}/km", new { km = 12500 }), HttpStatusCode.BadRequest, "dogrulama", "km");
        await ProblemBekle(await Gonder(admin, HttpMethod.Post, $"{Arac}/{id}/km",
            new { km = 14000, tarih = DateTimeOffset.UtcNow.AddDays(2) }), HttpStatusCode.BadRequest, "dogrulama", "tarih");

        var detay = await Json(await Gonder(admin, HttpMethod.Get, $"{Arac}/{id}/detay"));
        Assert.Equal(13000, detay.GetProperty("km").GetInt32());
        var kayit = Assert.Single(detay.GetProperty("kmKayitlari").EnumerateArray());
        Assert.Equal(13000, kayit.GetProperty("km").GetInt32());
        Assert.Equal("Manuel", kayit.GetProperty("kaynak").GetString());
    }
}
