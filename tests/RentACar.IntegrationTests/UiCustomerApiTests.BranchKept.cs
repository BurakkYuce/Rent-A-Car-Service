using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// #295 L3 — şube kapsamlı kullanıcı, şubeye bağlı CRM kaydının hedefini (kira + çıkış ofisi) PUT'ta birlikte boşaltarak
/// kaydı "şubesiz" (tüm şubelere görünür) yapamaz: 403, kayıt değişmez. Kapsamsız kullanıcı boşaltabilir; zaten şubesiz
/// kayıt şubesiz kalabilir; ofis korunursa kira kaldırılabilir. Ayrıca #295 bilgi: CRM analizinde AnonimBelge işaretli
/// müşterinin doğum tarihi dönmez.
/// </summary>
public sealed partial class UiCustomerApiTests
{
    [Fact]
    public async Task Scoped_user_cannot_clear_branch_target_of_complaint()
    {
        var e = await SetupAsync();
        var cust = await CustomerAsync(e, "Hedefli");
        var rentalB = await RentalAsync(e, cust, "SubeB");
        var opB = await LoginAsync(e, Who.OperatorB);
        const string url = V1 + "/sikayetler";

        var body = new Dictionary<string, object?> { ["cariId"] = cust, ["konu"] = "Gecikme", ["rentalId"] = rentalB.RentalId };
        var (created, _) = await Json(await Send(opB, HttpMethod.Post, url, body), HttpStatusCode.Created);
        var id = created.GetProperty("sikayet").GetProperty("id").GetGuid();

        body["rentalId"] = null;
        body["cikisOfisi"] = null;
        body["surum"] = created.GetProperty("surum").GetString();
        await Problem(await Send(opB, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.Forbidden, "yetki_yok");
        var stored = await ReadAsync(e.TenantId, db => db.Sikayetler.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(rentalB.RentalId, stored.RentalId);
        // Başka şubenin operatörü göremediği için hâlâ göremez (sızıntı yok).
        var (listA, _) = await Json(await Send(await LoginAsync(e, Who.OperatorA), HttpMethod.Get, url));
        Assert.Empty(Records(listA));

        // Kira kaldırılıp ofis korunursa kayıt şubeli kalır → izinli.
        body["cikisOfisi"] = "SubeB";
        var (kept, _) = await Json(await Send(opB, HttpMethod.Put, $"{url}/{id}", body));
        Assert.Equal(JsonValueKind.Null, kept.GetProperty("sikayet").GetProperty("rentalId").ValueKind);

        // Kapsamsız kullanıcı (Admin) boşaltabilir.
        body["cikisOfisi"] = null;
        body["surum"] = kept.GetProperty("surum").GetString();
        await Json(await Send(await LoginAsync(e, Who.Admin), HttpMethod.Put, $"{url}/{id}", body));

        // Zaten şubesiz kaydı kapsamlı kullanıcı şubesiz tutarak güncelleyebilir.
        var (free, _) = await Json(await Send(opB, HttpMethod.Get, $"{url}/{id}"));
        body["konu"] = "Gecikme (güncel)";
        body["surum"] = free.GetProperty("surum").GetString();
        await Json(await Send(opB, HttpMethod.Put, $"{url}/{id}", body));

        // Başka kiracı: 404.
        var other = await LoginAsync(await SetupAsync(), Who.Admin);
        await Problem(await Send(other, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Scoped_user_cannot_clear_branch_target_of_survey_or_assistance()
    {
        var e = await SetupAsync();
        var cust = await CustomerAsync(e, "Anketli");
        var rentalA = await RentalAsync(e, cust, "SubeA");
        var opA = await LoginAsync(e, Who.OperatorA);

        var survey = new Dictionary<string, object?> { ["cariId"] = cust, ["rentalId"] = rentalA.RentalId, ["puan"] = 7 };
        var (s, _) = await Json(await Send(opA, HttpMethod.Post, V1 + "/anketler", survey), HttpStatusCode.Created);
        var surveyId = s.GetProperty("anket").GetProperty("id").GetGuid();
        survey["rentalId"] = null;
        survey["cikisOfisi"] = null; // oluşturmada kiradan "SubeA" kopyalanmıştı
        survey["surum"] = s.GetProperty("surum").GetString();
        await Problem(await Send(opA, HttpMethod.Put, $"{V1}/anketler/{surveyId}", survey), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(rentalA.RentalId,
            await ReadAsync(e.TenantId, db => db.Anketler.AsNoTracking().Where(x => x.Id == surveyId).Select(x => x.RentalId).SingleAsync()));

        var (a, _) = await Json(await Send(opA, HttpMethod.Post, V1 + "/assistans-talepleri",
            new { rentalId = rentalA.RentalId, mesaj = "Akü bitti" }), HttpStatusCode.Created);
        var assistanceId = a.GetProperty("talep").GetProperty("id").GetGuid();
        var clear = new Dictionary<string, object?> { ["rentalId"] = null, ["mesaj"] = "Akü bitti", ["surum"] = a.GetProperty("surum").GetString() };
        await Problem(await Send(opA, HttpMethod.Put, $"{V1}/assistans-talepleri/{assistanceId}", clear), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(rentalA.RentalId,
            await ReadAsync(e.TenantId, db => db.AssistansTalepleri.AsNoTracking().Where(x => x.Id == assistanceId).Select(x => x.RentalId).SingleAsync()));
    }

    [Fact]
    public async Task Crm_analysis_hides_birth_date_of_document_anonymized_customer()
    {
        var e = await SetupAsync();
        var birth = new DateTimeOffset(1980, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var hidden = new Customer { Tip = CustomerType.Bireysel, Ad = "Belge", Soyad = "Gizli", DogumTarihi = birth, AnonimBelge = true };
        var shown = new Customer { Tip = CustomerType.Bireysel, Ad = "Belge", Soyad = "Acik", DogumTarihi = birth };
        await WriteAsync(e.TenantId, db => { db.Customers.Add(hidden); db.Customers.Add(shown); });
        await RentalAsync(e, hidden.Id, "SubeA");
        await RentalAsync(e, shown.Id, "SubeA");

        var (analysis, _) = await Json(await Send(await LoginAsync(e, Who.Admin), HttpMethod.Get, $"{V1}/crm/analiz"));
        var rows = Records(analysis.GetProperty("segment")).ToDictionary(r => r.GetProperty("cariId").GetGuid());
        Assert.Equal(JsonValueKind.Null, rows[hidden.Id].GetProperty("dogumTarihi").ValueKind);
        Assert.Equal(birth, rows[shown.Id].GetProperty("dogumTarihi").GetDateTimeOffset());
        Assert.Equal("Belge Gizli", rows[hidden.Id].GetProperty("ad").GetString()); // ad grubu ayrı bayrak; dokunulmadı
    }
}
