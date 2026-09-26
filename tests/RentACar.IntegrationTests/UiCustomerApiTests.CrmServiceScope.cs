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
    [Fact]
    public async Task Crm_writes_cannot_touch_other_branch_records()
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

        // F13: Blazor form uçları kalktı; aynı saldırı /api/ui üzerinden (tam PUT şubesiz hedefle + DELETE): 403, kayıt
        // değişmez. Kapsam kontrolü sürüm/durum kontrolünden ÖNCE (DEVIR §5).
        var surum = (await Json(await Send(opB, HttpMethod.Get, $"{V1}/sikayetler/{complaint}"))).Item1.GetProperty("surum").GetString();
        await Problem(await Send(opA, HttpMethod.Put, $"{V1}/sikayetler/{complaint}", new Dictionary<string, object?>
        {
            ["cariId"] = cust, ["konu"] = "A ele geçirdi", ["rentalId"] = null, ["cikisOfisi"] = null, ["surum"] = surum,
        }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Delete, $"{V1}/sikayetler/{complaint}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Put, $"{V1}/anketler/{survey}", new Dictionary<string, object?>
        {
            ["cariId"] = cust, ["puan"] = 1, ["rentalId"] = null,
        }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Delete, $"{V1}/anketler/{survey}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Put, $"{V1}/assistans-talepleri/{assistance}", new Dictionary<string, object?>
        {
            ["mesaj"] = "A", ["rentalId"] = null,
        }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Delete, $"{V1}/assistans-talepleri/{assistance}"), HttpStatusCode.Forbidden, "yetki_yok");

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

        // Kendi şubesindeki operatör aynı uçla güncelleyebilir (kemer yetkiliyi durdurmaz).
        await Json(await Send(opB, HttpMethod.Put, $"{V1}/sikayetler/{complaint}", new Dictionary<string, object?>
        {
            ["cariId"] = cust, ["konu"] = "B güncelledi", ["rentalId"] = rentalB.RentalId, ["surum"] = surum,
        }));
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
        // Servis girişi de aynı kurala tabi (F13: Blazor oluşturma formu kalktı; kural serviste — her çağıran).
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId, Guid.NewGuid(), "opA", UserRole.Operator, "SubeA"))
            await Assert.ThrowsAnyAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<ComplaintService>()
                .CreateAsync(new SikayetInput { CariId = cust, Konu = "Servis şubesiz" }));
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
