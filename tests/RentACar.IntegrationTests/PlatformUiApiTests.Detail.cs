using System.Net;
using System.Net.Http.Headers;

namespace RentACar.IntegrationTests;

public sealed partial class PlatformUiApiTests
{
    /// <summary>Minimal PNG header with the given size (only the IHDR is read by the logo rules).</summary>
    private static byte[] Png(int width, int height)
    {
        var b = new byte[200];
        byte[] head = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
        head.CopyTo(b, 0);
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        return b;
    }

    private static MultipartFormDataContent FileForm(string field, byte[] bytes, string fileName, string contentType,
        params (string Name, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, field, fileName);
        foreach (var (n, v) in fields) form.Add(new StringContent(v), n);
        return form;
    }

    [Fact]
    public async Task Detail_exposes_only_tenant_meta_fields()
    {
        var s = await PlatformLoginAsync();
        var (id, _) = await CreateTenantAsync(s);
        var j = await Json(await s.C.GetAsync(P + $"/kiracilar/{id}"));
        var keys = j.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            "aktifKira", "ad", "aracSayisi", "domainler", "eposta", "gelir30Gun", "guncelleme", "halkaAcikSite", "id",
            "kapanisTarihi", "kod", "kullaniciSayisi", "logo", "notlar", "olusturma", "plan", "sonGiris", "surum",
            "telefon", "toplamKira", "webSitesiModulu", "yeniArayuzPilot", "yetkiliAd", "durum",
        }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), keys);
        Assert.Equal(1, j.GetProperty("kullaniciSayisi").GetInt32()); // the first admin only
        Assert.False(j.GetProperty("logo").GetProperty("var").GetBoolean());
        await ExpectProblem(await s.C.GetAsync(P + $"/kiracilar/{Guid.NewGuid()}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Update_requires_current_version_and_is_audited()
    {
        var s = await PlatformLoginAsync();
        var (id, _) = await CreateTenantAsync(s);
        var surum = (await Json(await s.C.GetAsync(P + $"/kiracilar/{id}"))).GetProperty("surum").GetString();

        await ExpectProblem(await Send(s, HttpMethod.Put, P + $"/kiracilar/{id}", new { ad = "Yeni Ad" }),
            HttpStatusCode.BadRequest, "dogrulama", "surum");
        await ExpectProblem(await Send(s, HttpMethod.Put, P + $"/kiracilar/{id}", new { ad = "Yeni Ad", eposta = "bozuk", surum }),
            HttpStatusCode.BadRequest, "dogrulama", "eposta");
        await ExpectProblem(await Send(s, HttpMethod.Put, P + $"/kiracilar/{id}", new { ad = "", surum }),
            HttpStatusCode.BadRequest, "dogrulama", "ad");

        var ok = await Send(s, HttpMethod.Put, P + $"/kiracilar/{id}",
            new { ad = "Yeni Ad", yetkiliAd = "Ayşe Demir", eposta = "a@b.co", telefon = "05550001122", plan = "Pro", surum });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var after = await Json(ok);
        Assert.Equal("Yeni Ad", after.GetProperty("ad").GetString());
        Assert.Equal("Pro", after.GetProperty("plan").GetString());
        Assert.NotEqual(surum, after.GetProperty("surum").GetString());

        // The old version is stale now → 409 cakisma, nothing written.
        await ExpectProblem(await Send(s, HttpMethod.Put, P + $"/kiracilar/{id}", new { ad = "Bayat", surum }),
            HttpStatusCode.Conflict, "cakisma");
        Assert.Equal("Yeni Ad", (await Json(await s.C.GetAsync(P + $"/kiracilar/{id}"))).GetProperty("ad").GetString());

        var upd = Assert.Single(await AuditRowsAsync(id), a => a.Action == 1);
        Assert.Contains("Platform API Firması", upd.OldValues);
        Assert.Contains("Yeni Ad", upd.NewValues);
        await ExpectProblem(await Send(s, HttpMethod.Put, P + $"/kiracilar/{Guid.NewGuid()}", new { ad = "X", surum }),
            HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task List_filters_sorts_pages_and_summary_counts()
    {
        var s = await PlatformLoginAsync();
        var marker = "Liste" + Guid.NewGuid().ToString("N")[..8];
        var (a, _) = await CreateTenantAsync(s, marker + " A");
        var (b, bAdmin) = await CreateTenantAsync(s, marker + " B");
        Assert.Equal(HttpStatusCode.OK, (await Send(s, HttpMethod.Post, P + $"/kiracilar/{b}/durum", new { durum = "Pasif" })).StatusCode);

        var all = await Json(await s.C.GetAsync(P + $"/kiracilar?q={marker}&sirala=-ad"));
        Assert.Equal(2, all.GetProperty("toplam").GetInt32());
        Assert.Equal(new[] { b, a }, all.GetProperty("kayitlar").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray());

        var passive = await Json(await s.C.GetAsync(P + $"/kiracilar?q={marker}&durum=Pasif"));
        Assert.Equal(b, Assert.Single(passive.GetProperty("kayitlar").EnumerateArray()).GetProperty("id").GetGuid());
        var byCode = await Json(await s.C.GetAsync(P + $"/kiracilar?q={bAdmin.Firma.ToUpperInvariant()}"));
        Assert.Equal(1, byCode.GetProperty("toplam").GetInt32());

        var page2 = await Json(await s.C.GetAsync(P + $"/kiracilar?q={marker}&sirala=ad&boyut=1&sayfa=2"));
        Assert.Equal(b, Assert.Single(page2.GetProperty("kayitlar").EnumerateArray()).GetProperty("id").GetGuid());

        await ExpectProblem(await s.C.GetAsync(P + "/kiracilar?sirala=gizli"), HttpStatusCode.BadRequest, "dogrulama", "sirala");
        await ExpectProblem(await s.C.GetAsync(P + "/kiracilar?durum=Silindi"), HttpStatusCode.BadRequest, "dogrulama", "durum");

        var sum = await Json(await s.C.GetAsync(P + "/ozet"));
        var total = sum.GetProperty("toplamKiraci").GetInt32();
        Assert.Equal(total, sum.GetProperty("aktif").GetInt32() + sum.GetProperty("pasif").GetInt32() + sum.GetProperty("kapali").GetInt32());
        Assert.True(sum.GetProperty("pasif").GetInt32() >= 1);

        var options = await Json(await s.C.GetAsync(P + "/kiracilar/secim"));
        Assert.Contains(options.EnumerateArray(), o => o.GetProperty("id").GetGuid() == b && o.GetProperty("durum").GetString() == "Pasif");
    }

    [Fact]
    public async Task Logo_upload_preview_delete_and_non_png_refused()
    {
        var s = await PlatformLoginAsync();
        var (id, _) = await CreateTenantAsync(s);
        var png = Png(800, 200);

        var up = await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/logo", FileForm("logo", png, "logo.png", "image/png"));
        Assert.True(up.StatusCode == HttpStatusCode.OK, await up.Content.ReadAsStringAsync());
        var logo = (await Json(up)).GetProperty("logo");
        Assert.True(logo.GetProperty("var").GetBoolean());
        Assert.Equal(800, logo.GetProperty("genislik").GetInt32());

        var content = await s.C.GetAsync(P + $"/kiracilar/{id}/logo");
        Assert.Equal("image/png", content.Content.Headers.ContentType?.MediaType);
        Assert.Null(content.Content.Headers.ContentDisposition); // inline preview
        Assert.Equal(png, await content.Content.ReadAsByteArrayAsync());

        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[100]];
        await ExpectProblem(await Send(s, HttpMethod.Post, P + $"/kiracilar/{id}/logo", FileForm("logo", jpeg, "logo.png", "image/png")),
            HttpStatusCode.BadRequest, "dogrulama", "logo");

        var del = await Send(s, HttpMethod.Delete, P + $"/kiracilar/{id}/logo");
        Assert.False((await Json(del)).GetProperty("logo").GetProperty("var").GetBoolean());
        await ExpectProblem(await s.C.GetAsync(P + $"/kiracilar/{id}/logo"), HttpStatusCode.NotFound, null);
        Assert.Equal(2, (await AuditRowsAsync(id)).Count(a => a.EntityName == "TenantSettings"));
    }
}
