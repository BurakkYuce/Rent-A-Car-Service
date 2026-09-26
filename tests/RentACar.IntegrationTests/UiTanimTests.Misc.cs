using System.Net;
using System.Net.Http.Headers;

namespace RentACar.IntegrationTests;

public sealed partial class UiTanimTests
{
    private static MultipartFormDataContent Document(byte[] content, string? title, string fileName = "kilavuz.pdf")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "dosya", fileName } };
        if (title is not null) form.Add(new StringContent(title), "baslik");
        return form;
    }

    private static byte[] Pdf() => "%PDF-1.4\n%test\n"u8.ToArray();

    [Fact]
    public async Task Company_documents_upload_list_download_delete_permissions_and_isolation()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        const string Root = V1 + "/dokumanlar";

        var created = await Json(await Send(op, HttpMethod.Post, Root, Document(Pdf(), "Kullanım Kılavuzu")), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Kullanım Kılavuzu", created.GetProperty("baslik").GetString());
        Assert.Equal(Pdf().Length, created.GetProperty("boyut").GetInt64());
        Assert.Equal($"/dokumanlar/{id}/indir", created.GetProperty("indirmeYolu").GetString());

        // Content check by magic bytes, title required, file required.
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, Document("merhaba"u8.ToArray(), "Metin")), HttpStatusCode.BadRequest, "dogrulama", "dosya");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, Document(Pdf(), null)), HttpStatusCode.BadRequest, "dogrulama", "baslik");
        await ExpectProblem(await Send(op, HttpMethod.Post, Root, Document(Pdf(), new string('b', 201))), HttpStatusCode.BadRequest, "dogrulama", "baslik");

        // Accounting reads and downloads (Blazor [Authorize]) but cannot upload/delete.
        var acc = await LoginAsync(env, Who.Muhasebe);
        Assert.Single((await Json(await Send(acc, HttpMethod.Get, Root))).EnumerateArray());
        var download = await Send(acc, HttpMethod.Get, created.GetProperty("indirmeYolu").GetString()!);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf(), await download.Content.ReadAsByteArrayAsync());
        await ExpectProblem(await Send(acc, HttpMethod.Post, Root, Document(Pdf(), "Muh")), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Send(acc, HttpMethod.Delete, $"{Root}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");

        // Another tenant: empty list, delete 404.
        var other = await LoginAsync(await SetUpAsync(), Who.Admin);
        Assert.Empty((await Json(await Send(other, HttpMethod.Get, Root))).EnumerateArray());
        await ExpectProblem(await Send(other, HttpMethod.Delete, $"{Root}/{id}"), HttpStatusCode.NotFound, null);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Delete, $"{Root}/{id}")).StatusCode);
        Assert.Empty((await Json(await Send(op, HttpMethod.Get, Root))).EnumerateArray());

        // Platform documents: a new company has none; readable by any signed-in role.
        Assert.Empty((await Json(await Send(acc, HttpMethod.Get, V1 + "/firma-belgeleri"))).EnumerateArray());
    }

    [Fact]
    public async Task Calendar_link_is_personal_stable_and_regenerates()
    {
        var env = await SetUpAsync();
        var acc = await LoginAsync(env, Who.Muhasebe);
        var first = (await Json(await Send(acc, HttpMethod.Get, V1 + "/takvim-abonelik"))).GetProperty("url").GetString()!;
        Assert.Contains("/feed/calendar/", first);
        Assert.EndsWith(".ics", first);
        Assert.Equal(first, (await Json(await Send(acc, HttpMethod.Get, V1 + "/takvim-abonelik"))).GetProperty("url").GetString());

        // Each user has their own link.
        var op = await LoginAsync(env, Who.OperatorA);
        Assert.NotEqual(first, (await Json(await Send(op, HttpMethod.Get, V1 + "/takvim-abonelik"))).GetProperty("url").GetString());

        var renewed = (await Json(await Send(acc, HttpMethod.Post, V1 + "/takvim-abonelik/yenile"))).GetProperty("url").GetString();
        Assert.NotEqual(first, renewed);
        Assert.Equal(renewed, (await Json(await Send(acc, HttpMethod.Get, V1 + "/takvim-abonelik"))).GetProperty("url").GetString());
        // The old feed no longer answers.
        var oldFeed = await fx.Web.Client().GetAsync(new Uri(first).PathAndQuery);
        Assert.NotEqual(HttpStatusCode.OK, oldFeed.StatusCode);
    }
}
