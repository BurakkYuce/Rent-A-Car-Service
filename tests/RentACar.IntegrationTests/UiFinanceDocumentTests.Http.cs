using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceDocumentTests
{
    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
            if (v.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(v[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Session> LoginAsync(Env e, Who who)
    {
        var c = fx.Web.Istemci();
        var before = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", before);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({who}): {await r.Content.ReadAsStringAsync()}");
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> SendAsync(Session s, HttpMethod method, string path, object? body, string? key)
    {
        var req = new HttpRequestMessage(method, V1 + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return s.C.SendAsync(req);
    }

    private static Task<HttpResponseMessage> PostAsync(Session s, string path, object? body, string? key = null)
        => SendAsync(s, HttpMethod.Post, path, body, key);

    private static Task<HttpResponseMessage> GetAsync(Session s, string path) => s.C.GetAsync(V1 + path);

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<Guid> IdOf(HttpResponseMessage r) => (await Ok(r)).GetProperty("id").GetGuid();

    /// <summary>ProblemDetails + kod; <paramref name="field"/> verilirse <c>errors[field]</c> dolu olmalı.</summary>
    private static async Task<JsonElement> Problem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        var j = JsonDocument.Parse(text).RootElement.Clone();
        if (code is not null) Assert.Equal(code, j.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(j.TryGetProperty("errors", out var er) && er.TryGetProperty(field, out var m) && m.GetArrayLength() > 0,
                $"errors[{field}] yok: {text}");
        return j;
    }

    /// <summary>Sayfa yanıtındaki kayıt kimlikleri.</summary>
    private static async Task<List<Guid>> PageIdsAsync(HttpResponseMessage r, string idField = "id")
        => (await Ok(r)).GetProperty("kayitlar").EnumerateArray().Select(x => x.GetProperty(idField).GetGuid()).ToList();
}
