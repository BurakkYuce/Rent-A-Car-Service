using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// r317 — CRM şube kapsamı SERVİS katmanında (<see cref="CrmScopeGuard"/>): M1 Blazor form uçları ve servisin doğrudan
/// çağrısı da başka şubenin kaydını değiştiremez/silemez; L1 şube kapsamlı kullanıcı şubesiz kayıt OLUŞTURAMAZ (seçim:
/// otomatik bağlama yerine 400 <c>errors[rentalId]</c>); L2 AnonimBelge'de doğum tarihi kartta ve CRM raporunda gizli.
/// </summary>
public sealed partial class UiCustomerApiTests
{
    private static async Task<HttpResponseMessage> BlazorPostAsync(Session s, string path, Dictionary<string, string> form)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        return await s.C.SendAsync(req);
    }

    [Fact]
    public async Task Blazor_crm_forms_cannot_touch_other_branch_records()
    {
        var e = await SetupAsync();
        var cust = await CustomerAsync(e, "Formlu");
        var rentalB = await RentalAsync(e, cust, "SubeB");
        var opA = await LoginAsync(e, Who.OperatorA);
        var opB = await LoginAsync(e, Who.OperatorB);

        var (c, _) = await Json(await Send(opB, HttpMethod.Post, V1 + "/sikayetler",
            new Dictionary<string, object?> { ["cariId"] = cust, ["konu"] = "B'nin", ["rentalId"] = rentalB.RentalId }), HttpStatusCode.Created);
        var complaint = c.GetProperty("sikayet").GetProperty("id").GetGuid();
        var (s, _) = await Json(await Send(opB, HttpMethod.Post, V1 + "/anketler",
            new Dictionary<string, object?> { ["cariId"] = cust, ["rentalId"] = rentalB.RentalId, ["puan"] = 5 }), HttpStatusCode.Created);
        var survey = s.GetProperty("anket").GetProperty("id").GetGuid();
        var (a, _) = await Json(await Send(opB, HttpMethod.Post, V1 + "/assistans-talepleri",
            new { rentalId = rentalB.RentalId, mesaj = "Akü" }), HttpStatusCode.Created);
        var assistance = a.GetProperty("talep").GetProperty("id").GetGuid();

        var upd = await BlazorPostAsync(opA, "/sikayetler/update", new()
        {
            ["id"] = complaint.ToString(), ["cariId"] = cust.ToString(), ["konu"] = "A ele geçirdi", ["rentalId"] = "", ["cikisOfisi"] = "",
        });
        Assert.Contains("kapsam", Uri.UnescapeDataString(upd.Headers.Location?.ToString() ?? ""));
        await BlazorPostAsync(opA, "/sikayetler/delete", new() { ["id"] = complaint.ToString() });
        await BlazorPostAsync(opA, "/anketler/update", new() { ["id"] = survey.ToString(), ["puan"] = "1", ["rentalId"] = "" });
        await BlazorPostAsync(opA, "/anketler/delete", new() { ["id"] = survey.ToString() });
        await BlazorPostAsync(opA, "/assistans/update", new() { ["id"] = assistance.ToString(), ["mesaj"] = "A", ["rentalId"] = "" });
        await BlazorPostAsync(opA, "/assistans/delete", new() { ["id"] = assistance.ToString() });

        var sk = await ReadAsync(e.TenantId, db => db.Sikayetler.AsNoTracking().SingleAsync(x => x.Id == complaint));
        Assert.Equal(("B'nin", (Guid?)rentalB.RentalId), (sk.Konu, sk.RentalId));
        var an = await ReadAsync(e.TenantId, db => db.Anketler.AsNoTracking().SingleAsync(x => x.Id == survey));
        Assert.Equal((5, (Guid?)rentalB.RentalId), (an.Puan, an.RentalId));
        var at = await ReadAsync(e.TenantId, db => db.AssistansTalepleri.AsNoTracking().SingleAsync(x => x.Id == assistance));
        Assert.Equal(("Akü", (Guid?)rentalB.RentalId), (at.Mesaj, at.RentalId));

        // Servis doğrudan (Blazor sayfa kodu ve ileride eklenecek her çağıran): kapsam dışı 403, kayıt değişmez.
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId, Guid.NewGuid(), "opA", UserRole.Operator, "SubeA");
        var sp = scope.ServiceProvider;
        await Assert.ThrowsAsync<NoPermissionException>(() => sp.GetRequiredService<ComplaintService>()
            .UpdateAsync(complaint, new SikayetInput { CariId = cust, Konu = "X", RentalId = rentalB.RentalId }));
        await Assert.ThrowsAsync<NoPermissionException>(() => sp.GetRequiredService<ComplaintService>().DeleteAsync(complaint));
        await Assert.ThrowsAsync<NoPermissionException>(() => sp.GetRequiredService<SurveyService>().DeleteAsync(survey));
        await Assert.ThrowsAsync<NoPermissionException>(() => sp.GetRequiredService<AssistanceRequestService>().DeleteAsync(assistance));
        Assert.True(await ReadAsync(e.TenantId, db => db.Sikayetler.AnyAsync(x => x.Id == complaint)));

        // Kendi şubesindeki operatör aynı Blazor ucuyla güncelleyebilir (kemer yetkiliyi durdurmaz).
        await BlazorPostAsync(opB, "/sikayetler/update", new()
        {
            ["id"] = complaint.ToString(), ["cariId"] = cust.ToString(), ["konu"] = "B güncelledi", ["rentalId"] = rentalB.RentalId.ToString(),
        });
        Assert.Equal("B güncelledi", (await ReadAsync(e.TenantId, db => db.Sikayetler.AsNoTracking().SingleAsync(x => x.Id == complaint))).Konu);
    }

    [Fact]
    public async Task Scoped_user_cannot_create_branchless_crm_records()
    {
        var e = await SetupAsync();
        var cust = await CustomerAsync(e, "Subesiz");
        var opA = await LoginAsync(e, Who.OperatorA);
        await Problem(await Send(opA, HttpMethod.Post, V1 + "/sikayetler",
            new Dictionary<string, object?> { ["cariId"] = cust, ["konu"] = "Şubesiz" }), HttpStatusCode.BadRequest, "dogrulama", "rentalId");
        await Problem(await Send(opA, HttpMethod.Post, V1 + "/anketler",
            new Dictionary<string, object?> { ["cariId"] = cust, ["puan"] = 5 }), HttpStatusCode.BadRequest, "dogrulama", "rentalId");
        await Problem(await Send(opA, HttpMethod.Post, V1 + "/assistans-talepleri",
            new { mesaj = "Şubesiz" }), HttpStatusCode.BadRequest, "dogrulama", "rentalId");
        // Blazor oluşturma formu da aynı kurala tabi.
        await BlazorPostAsync(opA, "/sikayetler/create", new() { ["cariId"] = cust.ToString(), ["konu"] = "Blazor şubesiz" });
        Assert.Equal(0, await ReadAsync(e.TenantId, db => db.Sikayetler.CountAsync()));

        // Kendi şubesinin ofisiyle izinli; kapsamsız kullanıcı şubesiz açabilir.
        await Json(await Send(opA, HttpMethod.Post, V1 + "/sikayetler",
            new Dictionary<string, object?> { ["cariId"] = cust, ["konu"] = "Ofisli", ["cikisOfisi"] = "SubeA" }), HttpStatusCode.Created);
        await Json(await Send(await LoginAsync(e, Who.Admin), HttpMethod.Post, V1 + "/sikayetler",
            new Dictionary<string, object?> { ["cariId"] = cust, ["konu"] = "Firma geneli" }), HttpStatusCode.Created);
        Assert.Equal(2, await ReadAsync(e.TenantId, db => db.Sikayetler.CountAsync()));
    }

    [Fact]
    public async Task Document_anonymized_birth_date_hidden_on_card_kept_on_put_and_in_report()
    {
        var e = await SetupAsync();
        var birth = new DateTimeOffset(1980, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var c = new Customer { Tip = CustomerType.Bireysel, Ad = "Belge", Soyad = "Gizli", DogumTarihi = birth, AnonimBelge = true };
        await WriteAsync(e.TenantId, db => db.Customers.Add(c));
        await RentalAsync(e, c.Id, "SubeA");

        var admin = await LoginAsync(e, Who.Admin);
        var (card, raw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{c.Id}"));
        Assert.Equal(JsonValueKind.Null, card.GetProperty("dogumTarihi").ValueKind);
        Assert.DoesNotContain("1980-05-17", raw);

        // Tam PUT gizli (null) değeri geri gönderir → kayıtlı doğum tarihi SİLİNMEZ.
        await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{c.Id}", new Dictionary<string, object?>
        {
            ["tip"] = "Bireysel", ["ad"] = "Belge", ["soyad"] = "Gizli", ["dogumTarihi"] = null, ["anonimBelge"] = true,
            ["surum"] = card.GetProperty("surum").GetString(),
        }));
        Assert.Equal(birth, await ReadAsync(e.TenantId, db => db.Customers.Where(x => x.Id == c.Id).Select(x => x.DogumTarihi).SingleAsync()));

        // Blazor CRM analizi aynı rapor kaynağından okur: maske kaynakta.
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId);
        var rows = await scope.ServiceProvider.GetRequiredService<ReportService>().GetCustomerSegmentAsync(new MusteriSegmentFilter());
        Assert.Null(Assert.Single(rows, r => r.CariId == c.Id).DogumTarihi);
    }
}
