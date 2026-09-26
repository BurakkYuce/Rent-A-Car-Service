using System.Net;
using System.Net.Http.Headers;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    /// <summary>PNG imzalı (içerikten tür tespiti) sahte görsel; <paramref name="size"/> bayt.</summary>
    private static byte[] Png(int size = 64)
    {
        var b = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        return b;
    }

    private static MultipartFormDataContent Photo(byte[] content, string name = "a.png")
    {
        var f = new ByteArrayContent(content);
        f.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { f, "foto", name } };
    }

    [Fact]
    public async Task Foto_yukle_sirala_sil_sinirlar_ve_kapsam()
    {
        var o = await SetUpEnvironmentAsync();
        var id = await VehicleAsync(o, Plate("34F"));
        var opA = await LoginAsync(o, Kim.OperatorA);
        var path = $"{SampleVehicle}/{id}/fotograflar";

        var f1 = (await Json(await Gonder(opA, HttpMethod.Post, path, Photo(Png())), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var f2 = (await Json(await Gonder(opA, HttpMethod.Post, path, Photo(Png(128))), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // Tür içerikten: metin dosyası 400; 2 MB üstü (istek sınırının altında) 400.
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, path, Photo("merhaba"u8.ToArray(), "a.txt")), HttpStatusCode.BadRequest, "dogrulama", "foto");
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, path, Photo(Png(2 * 1024 * 1024 + 10))), HttpStatusCode.BadRequest, "dogrulama", "foto");

        var list = await Json(await Gonder(opA, HttpMethod.Get, path));
        Assert.Equal([f1, f2], list.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());
        // f2 yukarı → sıra [f2, f1].
        var newItem = await Json(await Gonder(opA, HttpMethod.Post, $"{path}/{f2}/yukari"));
        Assert.Equal([f2, f1], newItem.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());

        var content = await Gonder(opA, HttpMethod.Get, $"{path}/{f1}");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(64, (await content.Content.ReadAsByteArrayAsync()).Length);

        // Başka aracın foto kimliği bu araç yolunda 404; başka şube operatörü 403; Muhasebe yükleyemez 403.
        var other = await VehicleAsync(o, Plate("34G"));
        await ExpectProblem(await Gonder(opA, HttpMethod.Delete, $"{SampleVehicle}/{other}/fotograflar/{f1}"), HttpStatusCode.NotFound, null);
        var opB = await LoginAsync(o, Kim.OperatorB);
        await ExpectProblem(await Gonder(opB, HttpMethod.Get, path), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(opB, HttpMethod.Delete, $"{path}/{f1}"), HttpStatusCode.Forbidden, "yetki_yok");
        var acct = await LoginAsync(o, Kim.Muhasebe);
        await ExpectProblem(await Gonder(acct, HttpMethod.Post, path, Photo(Png())), HttpStatusCode.Forbidden, "yetki_yok");

        // CSRF başlığı yoksa yükleme reddedilir (form bağlama antiforgery'si kapalı; grup filtresi korur).
        var cplate = new HttpRequestMessage(HttpMethod.Post, path) { Content = Photo(Png()) };
        await ExpectProblem(await opA.C.SendAsync(cplate), HttpStatusCode.BadRequest, "xsrf_gecersiz");

        var after = await Json(await Gonder(opA, HttpMethod.Delete, $"{path}/{f1}"));
        Assert.Equal([f2], after.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());
    }

    /// <summary>
    /// GuvenliDonusTests dosya-ucu gözden geçirmesinin kanıtı: fotoğraf içeriği İNDİRME değildir (Content-Disposition
    /// yok → tarayıcı belgeyi değiştirir, "giriş ekranında kalma" tuzağı oluşmaz) ve oturumsuz istek login'e
    /// YÖNLENDİRİLMEZ (401 JSON, Location yok) → sunucu bu uçlar için hiçbir zaman ReturnUrl üretmez.
    /// </summary>
    [Fact]
    public async Task Photo_content_is_inline_and_401_has_no_redirect()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicleId = await VehicleAsync(o, Plate("34P"));
        var op = await LoginAsync(o, Kim.OperatorA);
        var path = $"{SampleVehicle}/{vehicleId}/fotograflar";
        var photoId = (await Json(await Gonder(op, HttpMethod.Post, path, Photo(Png())), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        foreach (var url in new[] { $"{path}/{photoId}", $"{path}/{photoId}/kucuk" })
        {
            var ok = await Gonder(op, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Null(ok.Content.Headers.ContentDisposition);

            var anonymous = await fx.Web.Client().GetAsync(url);
            await ExpectProblem(anonymous, HttpStatusCode.Unauthorized, "oturum_yok");
            Assert.Null(anonymous.Headers.Location);
        }
    }

    [Fact]
    public async Task Tanimlar_crud_kod_normalize_surum_ve_izin()
    {
        var o = await SetUpEnvironmentAsync();
        var opA = await LoginAsync(o, Kim.OperatorA);
        const string Path = V1 + "/arac-sahipleri";

        var s = await Json(await Gonder(opA, HttpMethod.Post, Path, new { kod = " bzm ", ad = "Bizim Filo", tur = "Şirket" }), HttpStatusCode.Created);
        var id = s.GetProperty("id").GetGuid();
        Assert.Equal("BZM", s.GetProperty("kod").GetString());
        var version = s.GetProperty("surum").GetString();
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, Path, new { kod = "BZM", ad = "İkinci" }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, Path, new { kod = "X", ad = "" }), HttpStatusCode.BadRequest, "dogrulama", "ad");
        await ExpectProblem(await Gonder(opA, HttpMethod.Post, Path, new { kod = "Y", ad = new string('a', 129) }), HttpStatusCode.BadRequest, "dogrulama", "ad");

        await ExpectProblem(await Gonder(opA, HttpMethod.Put, $"{Path}/{id}", new { kod = "BZM", ad = "Yeni" }), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var g = await Json(await Gonder(opA, HttpMethod.Put, $"{Path}/{id}", new { kod = "BZM", ad = "Yeni Ad", aktif = false, surum = version }));
        Assert.Equal("Yeni Ad", g.GetProperty("ad").GetString());
        Assert.False(g.GetProperty("aktif").GetBoolean());
        await ExpectProblem(await Gonder(opA, HttpMethod.Put, $"{Path}/{id}", new { kod = "BZM", ad = "Bayat", surum = version }), HttpStatusCode.Conflict, "cakisma");

        // Liste + sıralama; Muhasebe 403; başka kiracı 404.
        var list = await Json(await Gonder(opA, HttpMethod.Get, Path + "?sirala=kod"));
        Assert.Equal(1, list.GetProperty("toplam").GetInt32());
        var acct = await LoginAsync(o, Kim.Muhasebe);
        await ExpectProblem(await Gonder(acct, HttpMethod.Get, Path), HttpStatusCode.Forbidden, "yetki_yok");
        var o2 = await SetUpEnvironmentAsync();
        var foreign = await LoginAsync(o2, Kim.Admin);
        await ExpectProblem(await Gonder(foreign, HttpMethod.Get, $"{Path}/{id}"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Gonder(foreign, HttpMethod.Delete, $"{Path}/{id}"), HttpStatusCode.NotFound, null);

        // Segment + tip: aynı desen (oluştur, surum'lu güncelle, sil).
        var seg = await Json(await Gonder(opA, HttpMethod.Post, V1 + "/segmentler", new { kod = "c", ad = "C Segment" }), HttpStatusCode.Created);
        var segG = await Json(await Gonder(opA, HttpMethod.Put, $"{V1}/segmentler/{seg.GetProperty("id").GetGuid()}",
            new { kod = "C", ad = "Kompakt", aciklama = "orta", surum = seg.GetProperty("surum").GetString() }));
        Assert.Equal("Kompakt", segG.GetProperty("ad").GetString());
        var tip = await Json(await Gonder(opA, HttpMethod.Post, V1 + "/arac-tipleri", new { kod = "egea", ad = "Egea", marka = "Fiat", grup = "C" }), HttpStatusCode.Created);
        Assert.Equal("EGEA", tip.GetProperty("kod").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(opA, HttpMethod.Delete, $"{V1}/arac-tipleri/{tip.GetProperty("id").GetGuid()}")).StatusCode);

        // Seçim: tanımlı sahip (kodlu) + araç kaydında geçen serbest değer; q süzgeci; limit en çok 20.
        await VehicleAsync(o, Plate("34S"));
        await WriteDataAsync(o.TenantId, db => db.Vehicles.Add(new RentACar.Domain.Entities.Vehicle
        { Plaka = Plate("34T"), Sube = "SubeA", AracSahibi = "Dış Leasing" }));
        var owners = await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/secim/sahip"));
        var values = owners.EnumerateArray().Select(x => x.GetProperty("deger").GetString()).ToList();
        // "Yeni Ad" pasife alındı → tanımlı öneriden düşer; yalnız kayıtta geçen değer kalır.
        Assert.Equal(["Dış Leasing"], values);
        Assert.Single((await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/secim/sahip?q=leas"))).EnumerateArray());
        var brands = await Json(await Gonder(opA, HttpMethod.Get, SampleVehicle + "/secim/marka?limit=500"));
        Assert.Contains(brands.EnumerateArray(), x => x.GetProperty("deger").GetString() == "Fiat");
    }
}
