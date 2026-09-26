using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceHubApiTests
{
    private const string Root = "/api/ui/v1";

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
        var c = fx.Web.Client();
        var first = CookieValue(await c.GetAsync(Root + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, Root + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", first);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({who}): {await r.Content.ReadAsStringAsync()}");
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> SendAsync(Session s, HttpMethod method, string path, object? body, string? key)
    {
        var req = new HttpRequestMessage(method, (path.StartsWith("/api", StringComparison.Ordinal) ? "" : V1) + path);
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

    /// <summary>Kiracının TÜM defter kümeleri dengeli: her (SourceType, SourceId) için Σ borç(baz) == Σ alacak(baz).</summary>
    private async Task AllLedgerBalancedAsync(Env e)
    {
        var rows = await DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking().ToListAsync());
        Assert.NotEmpty(rows);
        foreach (var set in rows.GroupBy(x => (x.SourceType, x.SourceId)))
        {
            var debit = set.Where(x => x.Direction == LedgerDirection.Debit).Sum(x => x.Amount.Amount * x.Amount.Rate);
            var credit = set.Where(x => x.Direction == LedgerDirection.Credit).Sum(x => x.Amount.Amount * x.Amount.Rate);
            Assert.True(debit == credit, $"Dengesiz küme {set.Key}: borç {debit} ≠ alacak {credit}");
        }
    }

    private Task<List<AccountLedgerEntry>> LedgerAsync(Env e, Guid sourceId)
        => DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking().Where(x => x.SourceId == sourceId).ToListAsync());

    private Task<int> CashCountAsync(Env e)
        => DbAsync(e, db => db.CashTransactions.AsNoTracking().CountAsync());
}
