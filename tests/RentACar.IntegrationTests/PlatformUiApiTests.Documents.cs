using System.Net;
using System.Text;

namespace RentACar.IntegrationTests;

public sealed partial class PlatformUiApiTests
{
    private static byte[] Pdf(string marker) => Encoding.ASCII.GetBytes("%PDF-1.4\n% " + marker + "\n%%EOF\n");

    [Fact]
    public async Task Document_center_upload_publish_version_download_delete()
    {
        var s = await PlatformLoginAsync();
        var (target, _) = await CreateTenantAsync(s);
        var title = "Kılavuz " + Guid.NewGuid().ToString("N")[..6];
        var v1 = Pdf("v1");

        var up = await Send(s, HttpMethod.Post, P + "/belgeler", FileForm("dosya", v1, "Kılavuz Şube.pdf", "application/pdf",
            ("baslik", title), ("aciklama", "Açıklama"), ("yalnizYoneticiler", "true"), ("hedef", target.ToString())));
        Assert.True(up.StatusCode == HttpStatusCode.Created, await up.Content.ReadAsStringAsync());
        var doc = await Json(up);
        var id = doc.GetProperty("id").GetGuid();
        Assert.Equal("Taslak", doc.GetProperty("durum").GetString()); // uploads start as DRAFT
        Assert.Equal(1, doc.GetProperty("surum").GetInt32());
        Assert.True(doc.GetProperty("yalnizYoneticiler").GetBoolean());
        Assert.Equal("kilavuz-sube.pdf", doc.GetProperty("dosyaAdi").GetString());
        Assert.Single(doc.GetProperty("hedefKodlar").EnumerateArray());

        var pub = await Send(s, HttpMethod.Post, P + $"/belgeler/{id}/durum", new { durum = "Yayinda" });
        Assert.Equal("Yayinda", (await Json(pub)).GetProperty("durum").GetString());
        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/belgeler/{id}/durum", new { durum = "gizli" }),
            HttpStatusCode.BadRequest, "dogrulama", "durum");

        var v2 = Pdf("v2");
        var ver = await Send(s, HttpMethod.Post, P + $"/belgeler/{id}/surum", FileForm("dosya", v2, "yeni.pdf", "application/pdf"));
        Assert.Equal(2, (await Json(ver)).GetProperty("surum").GetInt32());

        var content = await s.C.GetAsync(P + $"/belgeler/{id}/icerik");
        Assert.Equal("application/pdf", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(v2, await content.Content.ReadAsByteArrayAsync());

        var list = await Json(await s.C.GetAsync(P + "/belgeler"));
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(s, HttpMethod.Delete, P + $"/belgeler/{id}")).StatusCode);
        await ExpectProblem(await s.C.GetAsync(P + $"/belgeler/{id}/icerik"), HttpStatusCode.NotFound, null);
        await ExpectProblem(await Send(s, HttpMethod.Delete, P + $"/belgeler/{id}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Document_upload_refuses_non_pdf_bad_targets_and_missing_title()
    {
        var s = await PlatformLoginAsync();
        var exe = Encoding.ASCII.GetBytes("MZ fake executable");
        await ExpectProblem(await Send(s, HttpMethod.Post, P + "/belgeler",
            FileForm("dosya", exe, "x.pdf", "application/pdf", ("baslik", "X"))), HttpStatusCode.BadRequest, "dogrulama", "dosya");
        await ExpectProblem(await Send(s, HttpMethod.Post, P + "/belgeler",
            FileForm("dosya", Pdf("x"), "x.pdf", "application/pdf", ("baslik", "X"), ("hedef", Guid.NewGuid().ToString()))),
            HttpStatusCode.BadRequest, "dogrulama", "hedef");
        await ExpectProblem(await Send(s, HttpMethod.Post, P + "/belgeler",
            FileForm("dosya", Pdf("x"), "x.pdf", "application/pdf", ("baslik", "X"), ("hedef", "degil-guid"))),
            HttpStatusCode.BadRequest, "dogrulama", "hedef");
        await ExpectProblem(await Send(s, HttpMethod.Post, P + "/belgeler",
            FileForm("dosya", Pdf("x"), "x.pdf", "application/pdf", ("baslik", "  "))),
            HttpStatusCode.BadRequest, "dogrulama", "baslik");
        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/belgeler/{Guid.NewGuid()}/durum", new { durum = "Yayinda" }),
            HttpStatusCode.NotFound, null);
    }
}
