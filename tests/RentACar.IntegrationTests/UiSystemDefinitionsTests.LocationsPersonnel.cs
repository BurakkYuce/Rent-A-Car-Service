using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemDefinitionsTests
{
    [Fact]
    public async Task Location_crud_weekly_hours_version_and_branch_scope()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/lokasyonlar";

        var a = await Json(await Send(admin, HttpMethod.Post, path, new
        {
            kod = "ist", ad = "İstanbul Havalimanı", sube = "SubeA", iata = "ist", teslimUcreti = 150m,
            haftalikCalismaSaatleri = new[] { new { gun = 1, acilis = "08:00", kapanis = "20:00", kapali = false } },
        }), HttpStatusCode.Created);
        var aId = a.GetProperty("id").GetGuid();
        Assert.Equal("IST", a.GetProperty("kod").GetString());
        Assert.Equal("IST", a.GetProperty("iata").GetString());
        // haftalık saatler DAİMA 7 gün; girilmeyen günler kapalı
        var hours = a.GetProperty("haftalikCalismaSaatleri").EnumerateArray().ToList();
        Assert.Equal(7, hours.Count);
        Assert.Equal("08:00", hours[0].GetProperty("acilis").GetString());
        Assert.True(hours[6].GetProperty("kapali").GetBoolean());

        var b = await Json(await Send(admin, HttpMethod.Post, path, new { kod = "ANK", ad = "Ankara", sube = "SubeB" }), HttpStatusCode.Created);
        var bId = b.GetProperty("id").GetGuid();

        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "IST", ad = "Tekrar" }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "X1", ad = "Gün", haftalikCalismaSaatleri = new[] { new { gun = 9, kapali = true } } }),
            HttpStatusCode.BadRequest, "dogrulama", "haftalikCalismaSaatleri");

        // şube kapsamı: SubeA operatörü kendi ofisini günceller; SubeB ofisine dokunamaz, ofisi SubeB'ye taşıyamaz
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        Assert.Equal(2, (await Json(await op.C.GetAsync(path))).GetProperty("toplam").GetInt32()); // okuma tüm ofisler
        var aVersion = VersionOf(await Json(await op.C.GetAsync($"{path}/{aId}")));
        await Problem(await Send(op, HttpMethod.Put, $"{path}/{aId}", new { kod = "IST", ad = "Taşı", sube = "SubeB", surum = aVersion }), HttpStatusCode.Forbidden, "yetki_yok");
        var bVersion = VersionOf(await Json(await op.C.GetAsync($"{path}/{bId}")));
        await Problem(await Send(op, HttpMethod.Put, $"{path}/{bId}", new { kod = "ANK", ad = "Ele geçir", sube = "SubeA", surum = bVersion }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Delete, $"{path}/{bId}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(op, HttpMethod.Post, path, new { kod = "IZM", ad = "İzmir", sube = "SubeB" }), HttpStatusCode.Forbidden, "yetki_yok");
        var ok = await Json(await Send(op, HttpMethod.Put, $"{path}/{aId}", new { kod = "IST", ad = "İstanbul", sube = "SubeA", surum = aVersion }));
        Assert.Equal("İstanbul", ok.GetProperty("ad").GetString());
        Assert.Equal("Ankara", (await Json(await admin.C.GetAsync($"{path}/{bId}"))).GetProperty("ad").GetString());

        // bayat sürüm
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{aId}", new { kod = "IST", ad = "Bayat", sube = "SubeA", surum = aVersion }), HttpStatusCode.Conflict, "cakisma");

        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await otherAdmin.C.GetAsync($"{path}/{aId}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Delete, $"{path}/{aId}"), HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Personnel_never_returns_tc_salary_only_in_detail_and_requires_manage_users()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/personel";
        const string nationalId = "10000000146";

        var createResp = await Send(admin, HttpMethod.Post, path, new
        {
            kod = "p1", ad = "Ali", soyad = "Veli", tcKimlik = nationalId, maas = 45000.75m, sube = "SubeA", cepTel = "05320000000",
            iseGiris = TestZaman.DaysLater(-100).ToOffset(TimeSpan.FromHours(3)),
        });
        var createText = await createResp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        Assert.DoesNotContain(nationalId, createText);
        var created = JsonDocument.Parse(createText).RootElement;
        var id = created.GetProperty("id").GetGuid();
        Assert.True(created.GetProperty("tcKimlikTanimli").GetBoolean());
        Assert.False(created.TryGetProperty("tcKimlik", out _));
        Assert.Equal(45000.75m, created.GetProperty("maas").GetDecimal());
        Assert.Equal(TimeSpan.Zero, created.GetProperty("iseGiris").GetDateTimeOffset().Offset); // UTC saklandı

        // liste: TC da maaş da yok
        var listText = await (await admin.C.GetAsync(path)).Content.ReadAsStringAsync();
        Assert.DoesNotContain(nationalId, listText);
        Assert.DoesNotContain("maas", listText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("45000", listText);

        // DB'de TC düz metin değil
        var enc = await _kit.ReadAsync(e.TenantId, db => db.Personeller.Where(p => p.Id == id).Select(p => p.TcKimlikEnc).SingleAsync());
        Assert.NotNull(enc);
        Assert.DoesNotContain(nationalId, enc!);

        // güncelle: TC/maaş boş → korunur; detayda yine yok
        var s1 = VersionOf(created);
        var updText = await (await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "P1", ad = "Ali", soyad = "Kaya", sube = "SubeA", surum = s1 })).Content.ReadAsStringAsync();
        Assert.DoesNotContain(nationalId, updText);
        var upd = JsonDocument.Parse(updText).RootElement;
        Assert.Equal("Kaya", upd.GetProperty("soyad").GetString());
        Assert.True(upd.GetProperty("tcKimlikTanimli").GetBoolean());
        Assert.Equal(45000.75m, upd.GetProperty("maas").GetDecimal());
        await Problem(await Send(admin, HttpMethod.Put, $"{path}/{id}", new { kod = "P1", ad = "Ali", soyad = "Bayat", surum = s1 }), HttpStatusCode.Conflict, "cakisma");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "P2", ad = "A", soyad = "B", tcKimlik = "123" }), HttpStatusCode.BadRequest, "dogrulama", "tcKimlik");
        await Problem(await Send(admin, HttpMethod.Post, path, new { kod = "P1", ad = "A", soyad = "B" }), HttpStatusCode.BadRequest, "dogrulama", "kod");

        // ManageUsers olmayan roller (Yönetici, Operatör, Muhasebe) → 403, maaş/PII görmez
        foreach (var who in new[] { Who.Manager, Who.OperatorA, Who.Accounting })
        {
            var s = await _kit.LoginAsync(e, who);
            await Problem(await s.C.GetAsync(path), HttpStatusCode.Forbidden, "yetki_yok");
            await Problem(await s.C.GetAsync($"{path}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        }

        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        await Problem(await otherAdmin.C.GetAsync($"{path}/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Delete, $"{path}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, $"{path}/{id}")).StatusCode);
    }
}
