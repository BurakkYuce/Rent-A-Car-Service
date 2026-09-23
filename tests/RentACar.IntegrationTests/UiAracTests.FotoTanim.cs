using System.Net;
using System.Net.Http.Headers;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracTests
{
    /// <summary>PNG imzalı (içerikten tür tespiti) sahte görsel; <paramref name="boyut"/> bayt.</summary>
    private static byte[] Png(int boyut = 64)
    {
        var b = new byte[boyut];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        return b;
    }

    private static MultipartFormDataContent Foto(byte[] icerik, string ad = "a.png")
    {
        var f = new ByteArrayContent(icerik);
        f.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { f, "foto", ad } };
    }

    [Fact]
    public async Task Foto_yukle_sirala_sil_sinirlar_ve_kapsam()
    {
        var o = await OrtamKurAsync();
        var id = await AracAsync(o, Plaka("34F"));
        var opA = await GirisAsync(o, Kim.OperatorA);
        var yol = $"{Arac}/{id}/fotograflar";

        var f1 = (await Json(await Gonder(opA, HttpMethod.Post, yol, Foto(Png())), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var f2 = (await Json(await Gonder(opA, HttpMethod.Post, yol, Foto(Png(128))), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // Tür içerikten: metin dosyası 400; 2 MB üstü (istek sınırının altında) 400.
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, yol, Foto("merhaba"u8.ToArray(), "a.txt")), HttpStatusCode.BadRequest, "dogrulama", "foto");
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, yol, Foto(Png(2 * 1024 * 1024 + 10))), HttpStatusCode.BadRequest, "dogrulama", "foto");

        var liste = await Json(await Gonder(opA, HttpMethod.Get, yol));
        Assert.Equal([f1, f2], liste.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());
        // f2 yukarı → sıra [f2, f1].
        var yeni = await Json(await Gonder(opA, HttpMethod.Post, $"{yol}/{f2}/yukari"));
        Assert.Equal([f2, f1], yeni.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());

        var icerik = await Gonder(opA, HttpMethod.Get, $"{yol}/{f1}");
        Assert.Equal(HttpStatusCode.OK, icerik.StatusCode);
        Assert.Equal("image/png", icerik.Content.Headers.ContentType?.MediaType);
        Assert.Equal(64, (await icerik.Content.ReadAsByteArrayAsync()).Length);

        // Başka aracın foto kimliği bu araç yolunda 404; başka şube operatörü 403; Muhasebe yükleyemez 403.
        var diger = await AracAsync(o, Plaka("34G"));
        await ProblemBekle(await Gonder(opA, HttpMethod.Delete, $"{Arac}/{diger}/fotograflar/{f1}"), HttpStatusCode.NotFound, null);
        var opB = await GirisAsync(o, Kim.OperatorB);
        await ProblemBekle(await Gonder(opB, HttpMethod.Get, yol), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(opB, HttpMethod.Delete, $"{yol}/{f1}"), HttpStatusCode.Forbidden, "yetki_yok");
        var muh = await GirisAsync(o, Kim.Muhasebe);
        await ProblemBekle(await Gonder(muh, HttpMethod.Post, yol, Foto(Png())), HttpStatusCode.Forbidden, "yetki_yok");

        // CSRF başlığı yoksa yükleme reddedilir (form bağlama antiforgery'si kapalı; grup filtresi korur).
        var cplak = new HttpRequestMessage(HttpMethod.Post, yol) { Content = Foto(Png()) };
        await ProblemBekle(await opA.C.SendAsync(cplak), HttpStatusCode.BadRequest, "xsrf_gecersiz");

        var sonra = await Json(await Gonder(opA, HttpMethod.Delete, $"{yol}/{f1}"));
        Assert.Equal([f2], sonra.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList());
    }

    /// <summary>
    /// GuvenliDonusTests dosya-ucu gözden geçirmesinin kanıtı: fotoğraf içeriği İNDİRME değildir (Content-Disposition
    /// yok → tarayıcı belgeyi değiştirir, "giriş ekranında kalma" tuzağı oluşmaz) ve oturumsuz istek login'e
    /// YÖNLENDİRİLMEZ (401 JSON, Location yok) → sunucu bu uçlar için hiçbir zaman ReturnUrl üretmez.
    /// </summary>
    [Fact]
    public async Task Photo_content_is_inline_and_401_has_no_redirect()
    {
        var o = await OrtamKurAsync();
        var vehicleId = await AracAsync(o, Plaka("34P"));
        var op = await GirisAsync(o, Kim.OperatorA);
        var path = $"{Arac}/{vehicleId}/fotograflar";
        var photoId = (await Json(await Gonder(op, HttpMethod.Post, path, Foto(Png())), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        foreach (var url in new[] { $"{path}/{photoId}", $"{path}/{photoId}/kucuk" })
        {
            var ok = await Gonder(op, HttpMethod.Get, url);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Null(ok.Content.Headers.ContentDisposition);

            var anonymous = await fx.Web.Istemci().GetAsync(url);
            await ProblemBekle(anonymous, HttpStatusCode.Unauthorized, "oturum_yok");
            Assert.Null(anonymous.Headers.Location);
        }
    }

    [Fact]
    public async Task Tanimlar_crud_kod_normalize_surum_ve_izin()
    {
        var o = await OrtamKurAsync();
        var opA = await GirisAsync(o, Kim.OperatorA);
        const string Yol = V1 + "/arac-sahipleri";

        var s = await Json(await Gonder(opA, HttpMethod.Post, Yol, new { kod = " bzm ", ad = "Bizim Filo", tur = "Şirket" }), HttpStatusCode.Created);
        var id = s.GetProperty("id").GetGuid();
        Assert.Equal("BZM", s.GetProperty("kod").GetString());
        var surum = s.GetProperty("surum").GetString();
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, Yol, new { kod = "BZM", ad = "İkinci" }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, Yol, new { kod = "X", ad = "" }), HttpStatusCode.BadRequest, "dogrulama", "ad");
        await ProblemBekle(await Gonder(opA, HttpMethod.Post, Yol, new { kod = "Y", ad = new string('a', 129) }), HttpStatusCode.BadRequest, "dogrulama", "ad");

        await ProblemBekle(await Gonder(opA, HttpMethod.Put, $"{Yol}/{id}", new { kod = "BZM", ad = "Yeni" }), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var g = await Json(await Gonder(opA, HttpMethod.Put, $"{Yol}/{id}", new { kod = "BZM", ad = "Yeni Ad", aktif = false, surum }));
        Assert.Equal("Yeni Ad", g.GetProperty("ad").GetString());
        Assert.False(g.GetProperty("aktif").GetBoolean());
        await ProblemBekle(await Gonder(opA, HttpMethod.Put, $"{Yol}/{id}", new { kod = "BZM", ad = "Bayat", surum }), HttpStatusCode.Conflict, "cakisma");

        // Liste + sıralama; Muhasebe 403; başka kiracı 404.
        var liste = await Json(await Gonder(opA, HttpMethod.Get, Yol + "?sirala=kod"));
        Assert.Equal(1, liste.GetProperty("toplam").GetInt32());
        var muh = await GirisAsync(o, Kim.Muhasebe);
        await ProblemBekle(await Gonder(muh, HttpMethod.Get, Yol), HttpStatusCode.Forbidden, "yetki_yok");
        var o2 = await OrtamKurAsync();
        var yabanci = await GirisAsync(o2, Kim.Admin);
        await ProblemBekle(await Gonder(yabanci, HttpMethod.Get, $"{Yol}/{id}"), HttpStatusCode.NotFound, null);
        await ProblemBekle(await Gonder(yabanci, HttpMethod.Delete, $"{Yol}/{id}"), HttpStatusCode.NotFound, null);

        // Segment + tip: aynı desen (oluştur, surum'lu güncelle, sil).
        var seg = await Json(await Gonder(opA, HttpMethod.Post, V1 + "/segmentler", new { kod = "c", ad = "C Segment" }), HttpStatusCode.Created);
        var segG = await Json(await Gonder(opA, HttpMethod.Put, $"{V1}/segmentler/{seg.GetProperty("id").GetGuid()}",
            new { kod = "C", ad = "Kompakt", aciklama = "orta", surum = seg.GetProperty("surum").GetString() }));
        Assert.Equal("Kompakt", segG.GetProperty("ad").GetString());
        var tip = await Json(await Gonder(opA, HttpMethod.Post, V1 + "/arac-tipleri", new { kod = "egea", ad = "Egea", marka = "Fiat", grup = "C" }), HttpStatusCode.Created);
        Assert.Equal("EGEA", tip.GetProperty("kod").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(opA, HttpMethod.Delete, $"{V1}/arac-tipleri/{tip.GetProperty("id").GetGuid()}")).StatusCode);

        // Seçim: tanımlı sahip (kodlu) + araç kaydında geçen serbest değer; q süzgeci; limit en çok 20.
        await AracAsync(o, Plaka("34S"));
        await VeriYazAsync(o.TenantId, db => db.Vehicles.Add(new RentACar.Domain.Entities.Vehicle
        { Plaka = Plaka("34T"), Sube = "SubeA", AracSahibi = "Dış Leasing" }));
        var sahipler = await Json(await Gonder(opA, HttpMethod.Get, Arac + "/secim/sahip"));
        var degerler = sahipler.EnumerateArray().Select(x => x.GetProperty("deger").GetString()).ToList();
        // "Yeni Ad" pasife alındı → tanımlı öneriden düşer; yalnız kayıtta geçen değer kalır.
        Assert.Equal(["Dış Leasing"], degerler);
        Assert.Single((await Json(await Gonder(opA, HttpMethod.Get, Arac + "/secim/sahip?q=leas"))).EnumerateArray());
        var markalar = await Json(await Gonder(opA, HttpMethod.Get, Arac + "/secim/marka?limit=500"));
        Assert.Contains(markalar.EnumerateArray(), x => x.GetProperty("deger").GetString() == "Fiat");
    }
}
