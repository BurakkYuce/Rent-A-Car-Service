using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiCustomerApiTests
{
    private static Dictionary<string, object?> CustomerBody(string nationalId, string surname = "Kaya") => new()
    {
        ["tip"] = "Bireysel", ["ad"] = "Ece", ["soyad"] = surname, ["tcKimlik"] = nationalId, ["cepTel"] = "05321112233",
        ["email"] = "ece@example.com", ["il"] = "İstanbul", ["ilce"] = "Kadıköy", ["ehliyetNo"] = "AB12345678",
        ["pasaportNo"] = "U12345", ["vadeGun"] = 30, ["riskLimiti"] = 2500.50m,
        ["kisiler"] = new[] { new { adSoyad = "Ali Veli", telefon = "0212", mail = (string?)null, gorev = "Satın alma" } },
    };

    [Fact]
    public async Task Customer_create_update_version_and_tc_never_returned()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var nationalId = RandomNationalId();

        var (created, raw) = await Json(await Send(admin, HttpMethod.Post, Customers, CustomerBody(nationalId)), HttpStatusCode.Created);
        Assert.DoesNotContain(nationalId, raw);
        var id = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("tcKimlikVar").GetBoolean());
        Assert.False(created.TryGetProperty("tcKimlik", out _));
        Assert.Equal("******5678", created.GetProperty("ehliyetNoMaske").GetString()); // 10 hane → son 4 açık
        Assert.Equal("****45", created.GetProperty("pasaportNoMaske").GetString());   // 6 hane → son 2 açık
        Assert.Equal(2500.50m, created.GetProperty("riskLimiti").GetDecimal());
        Assert.Equal("Ali Veli", created.GetProperty("kisiler")[0].GetProperty("adSoyad").GetString());
        var v1 = created.GetProperty("surum").GetString()!;

        // Liste: TC tam 11 hane → blind-index eşleşmesi (1 kayıt); kısmi TC → hiç (kısmi arama bilinçli YOK).
        var (full, fullRaw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={nationalId}"));
        Assert.Equal(id, Assert.Single(Records(full)).GetProperty("id").GetGuid());
        Assert.DoesNotContain(nationalId, fullRaw);
        var (partial, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={nationalId[..6]}"));
        Assert.Empty(Records(partial));

        // Aynı TC ikinci kez → 400 errors[tcKimlik], mesajda TC yok; geçersiz TC → 400.
        var dup = await Problem(await Send(admin, HttpMethod.Post, Customers, CustomerBody(nationalId, "Başka")), HttpStatusCode.BadRequest, "dogrulama", "tcKimlik");
        Assert.DoesNotContain(nationalId, dup);
        var bad = nationalId[..10] + ((nationalId[10] - '0' + 1) % 10);
        await Problem(await Send(admin, HttpMethod.Post, Customers, CustomerBody(bad)), HttpStatusCode.BadRequest, "dogrulama", "tcKimlik");

        // PUT: surum yok → 400; TC/ehliyet gönderilmez (kart göstermiyor) → KORUNUR; eski surum → 409.
        var update = CustomerBody(nationalId);
        update.Remove("tcKimlik");
        update.Remove("ehliyetNo");
        update["cepTel"] = "05329998877";
        await Problem(await Send(admin, HttpMethod.Put, $"{Customers}/{id}", update), HttpStatusCode.BadRequest, "dogrulama", "surum");
        update["surum"] = v1;
        var (updated, updatedRaw) = await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{id}", update));
        Assert.DoesNotContain(nationalId, updatedRaw);
        Assert.Equal("05329998877", updated.GetProperty("cepTel").GetString());
        Assert.True(updated.GetProperty("tcKimlikVar").GetBoolean());
        Assert.Equal("******5678", updated.GetProperty("ehliyetNoMaske").GetString());
        Assert.NotEqual(v1, updated.GetProperty("surum").GetString());
        var (stillFound, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={nationalId}"));
        Assert.Single(Records(stillFound));
        await Problem(await Send(admin, HttpMethod.Put, $"{Customers}/{id}", update), HttpStatusCode.Conflict, "cakisma");

        // "" = temizle: TC boşaltılınca TC araması artık bulmaz.
        update["surum"] = updated.GetProperty("surum").GetString();
        update["tcKimlik"] = "";
        var (cleared, _) = await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{id}", update));
        Assert.False(cleared.GetProperty("tcKimlikVar").GetBoolean());
        var (gone, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={nationalId}"));
        Assert.Empty(Records(gone));

        // Sınırlar: uzun metin ve taşan tutar 400 + alan.
        var tooLong = CustomerBody(RandomNationalId());
        tooLong["aciklama"] = new string('x', 1025);
        await Problem(await Send(admin, HttpMethod.Post, Customers, tooLong), HttpStatusCode.BadRequest, "dogrulama", "aciklama");
        var tooBig = CustomerBody(RandomNationalId());
        tooBig["riskLimiti"] = 1_000_000_000_000m;
        await Problem(await Send(admin, HttpMethod.Post, Customers, tooBig), HttpStatusCode.BadRequest, "dogrulama", "riskLimiti");
        var badType = CustomerBody(RandomNationalId());
        badType["tip"] = "Uzaylı";
        await Problem(await Send(admin, HttpMethod.Post, Customers, badType), HttpStatusCode.BadRequest, "dogrulama", "tip");
    }

    [Fact]
    public async Task Customer_anonymized_groups_are_hidden_and_preserved()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var nationalId = RandomNationalId();
        var body = CustomerBody(nationalId, "Gizlioglu");
        body["anonimAd"] = true;
        body["anonimTelefon"] = true;
        body["anonimBelge"] = true;
        var (card, raw) = await Json(await Send(admin, HttpMethod.Post, Customers, body), HttpStatusCode.Created);
        var id = card.GetProperty("id").GetGuid();
        foreach (var hidden in new[] { nationalId, "Gizlioglu", "05321112233" }) Assert.DoesNotContain(hidden, raw);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("ad").ValueKind);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("ehliyetNoMaske").ValueKind);
        Assert.Equal("ece@example.com", card.GetProperty("email").GetString()); // AnonimMail yok → görünür

        var (list, listRaw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q=Anonim"));
        var row = Assert.Single(Records(list));
        Assert.Equal("Anonim müşteri", row.GetProperty("ad").GetString());
        Assert.True(row.GetProperty("anonim").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("cepTel").ValueKind);
        Assert.DoesNotContain("Gizlioglu", listRaw);

        var (detail, detailRaw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{id}/detay"));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("musteri").GetProperty("ad").ValueKind);
        Assert.DoesNotContain("Gizlioglu", detailRaw);

        // Kart olduğu gibi (gizli alanlar null) geri gönderilir → saklı değerler KORUNUR; bayrak kalkınca geri görünür.
        var put = new Dictionary<string, object?>
        {
            ["tip"] = "Bireysel", ["email"] = "ece@example.com", ["anonimAd"] = false, ["anonimTelefon"] = false,
            ["anonimBelge"] = false, ["surum"] = card.GetProperty("surum").GetString(),
        };
        var (after, _) = await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{id}", put));
        Assert.Equal("Ece", after.GetProperty("ad").GetString());
        Assert.Equal("Gizlioglu", after.GetProperty("soyad").GetString());
        Assert.Equal("05321112233", after.GetProperty("cepTel").GetString());
        Assert.Equal("******5678", after.GetProperty("ehliyetNoMaske").GetString());
        Assert.True(after.GetProperty("tcKimlikVar").GetBoolean());
    }

    [Fact]
    public async Task Customer_permissions_delete_guard_and_tenant_isolation()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        var acc = await LoginAsync(e, Who.Accounting);
        var (c, _) = await Json(await Send(admin, HttpMethod.Post, Customers, CustomerBody(RandomNationalId())), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();

        // Muhasebe okur ama yazamaz; operatör silemez (OperationsDelete yok).
        await Json(await Send(acc, HttpMethod.Get, Customers));
        await Problem(await Send(acc, HttpMethod.Post, Customers, CustomerBody(RandomNationalId())), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Delete, $"{Customers}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");

        // Kirada kullanılan cari silinemez (400), kira yoksa 204 ve sonra 404.
        await RentalAsync(e, id, "SubeA");
        await Problem(await Send(admin, HttpMethod.Delete, $"{Customers}/{id}"), HttpStatusCode.BadRequest, "dogrulama");
        var (free, _) = await Json(await Send(admin, HttpMethod.Post, Customers, CustomerBody(RandomNationalId())), HttpStatusCode.Created);
        var freeId = free.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{Customers}/{freeId}")).StatusCode);
        await Problem(await Send(admin, HttpMethod.Get, $"{Customers}/{freeId}"), HttpStatusCode.NotFound, null);

        // Başka kiracının carisi: kart/detay/PUT/DELETE hepsi 404 (RLS) — ve kayıt değişmez.
        var other = await SetupAsync();
        var otherAdmin = await LoginAsync(other, Who.Admin);
        var (foreign, _) = await Json(await Send(otherAdmin, HttpMethod.Post, Customers, CustomerBody(RandomNationalId(), "Yabanci")), HttpStatusCode.Created);
        var fid = foreign.GetProperty("id").GetGuid();
        await Problem(await Send(admin, HttpMethod.Get, $"{Customers}/{fid}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(admin, HttpMethod.Get, $"{Customers}/{fid}/detay"), HttpStatusCode.NotFound, null);
        var put = CustomerBody(RandomNationalId(), "Ele");
        put["surum"] = foreign.GetProperty("surum").GetString();
        await Problem(await Send(admin, HttpMethod.Put, $"{Customers}/{fid}", put), HttpStatusCode.NotFound, null);
        await Problem(await Send(admin, HttpMethod.Delete, $"{Customers}/{fid}"), HttpStatusCode.NotFound, null);
        var surname = await ReadAsync(other.TenantId, db => db.Customers.Where(x => x.Id == fid).Select(x => x.Soyad).SingleAsync());
        Assert.Equal("Yabanci", surname);
    }

    [Fact]
    public async Task Customer_detail_filters_rentals_by_branch_and_hides_ledger_from_operator()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var (c, _) = await Json(await Send(admin, HttpMethod.Post, Customers, CustomerBody(RandomNationalId())), HttpStatusCode.Created);
        var id = c.GetProperty("id").GetGuid();
        var a = await RentalAsync(e, id, "SubeA");
        var b = await RentalAsync(e, id, "SubeB");

        var (all, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{id}/detay"));
        Assert.Equal(2, all.GetProperty("kiralar").GetArrayLength());
        Assert.Equal(JsonValueKind.Number, all.GetProperty("bakiye").ValueKind);

        var opA = await LoginAsync(e, Who.OperatorA);
        var (mine, rawA) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}/{id}/detay"));
        Assert.Equal(a.ContractNo, Assert.Single(mine.GetProperty("kiralar").EnumerateArray()).GetProperty("sozlesmeNo").GetString());
        Assert.DoesNotContain(b.ContractNo, rawA);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("bakiye").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("hareketler").ValueKind);

        // Liste sıralaması (beyaz liste) ve bilinmeyen alan 400.
        await Problem(await Send(admin, HttpMethod.Get, $"{Customers}?sirala=tcKimlik"), HttpStatusCode.BadRequest, "dogrulama", "sirala");
        await Json(await Send(admin, HttpMethod.Get, $"{Customers}?sirala=-olusturma&boyut=5"));
    }
}
