using System.Net;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemAdminTests
{
    [Fact]
    public async Task Message_template_put_requires_version_after_first_save()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string url = V1 + "/mesaj-sablonlari/TalepAlindi/Sms";

        var list = await Json(await admin.C.GetAsync(V1 + "/mesaj-sablonlari"));
        var empty = list.EnumerateArray().Single(x => x.GetProperty("tur").GetString() == "TalepAlindi" && x.GetProperty("kanal").GetString() == "Sms");
        Assert.False(empty.GetProperty("kayitli").GetBoolean());

        var first = await Json(await Send(admin, HttpMethod.Put, url, new { govde = "Merhaba {{Ad}}", aktif = true }));
        var surum = first.GetProperty("surum").GetString();
        Assert.NotNull(surum);
        Assert.Equal("Merhaba {{Ad}}", first.GetProperty("govde").GetString());

        await Problem(await Send(admin, HttpMethod.Put, url, new { govde = "ikinci" }), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var second = await Json(await Send(admin, HttpMethod.Put, url, new { govde = "ikinci", surum }));
        Assert.Equal("ikinci", second.GetProperty("govde").GetString());
        await Problem(await Send(admin, HttpMethod.Put, url, new { govde = "bayat", surum }), HttpStatusCode.Conflict, "cakisma");

        await Problem(await Send(admin, HttpMethod.Put, url, new { govde = new string('x', 601), surum = second.GetProperty("surum").GetString() }),
            HttpStatusCode.BadRequest, "dogrulama", "govde");
        await Problem(await Send(admin, HttpMethod.Put, V1 + "/mesaj-sablonlari/TalepAlindi/Eposta", new { govde = "x" }),
            HttpStatusCode.BadRequest, "dogrulama", "konu");
        await Problem(await Send(admin, HttpMethod.Put, V1 + "/mesaj-sablonlari/Yok/Sms", new { govde = "x" }),
            HttpStatusCode.BadRequest, "dogrulama", "tur");
    }

    [Fact]
    public async Task Screen_permissions_reject_unknown_roles_and_are_tenant_scoped()
    {
        var a = await _kit.SetupAsync();
        var b = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(a, Who.Admin);
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/yetki/ekranlar", new { ekranKodu = "personel", roller = new[] { "Admin", "Patron" } }),
            HttpStatusCode.BadRequest, "dogrulama", "roller");
        var list = await Json(await Send(admin, HttpMethod.Post, V1 + "/yetki/ekranlar", new { ekranKodu = "Personel", roller = new[] { "Admin", "Yonetici" } }));
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal("personel", row.GetProperty("ekranKodu").GetString());
        Assert.Equal(["Admin", "Yonetici"], row.GetProperty("roller").EnumerateArray().Select(x => x.GetString()).ToArray());

        var otherAdmin = await _kit.LoginAsync(b, Who.Admin);
        Assert.Equal(0, (await Json(await otherAdmin.C.GetAsync(V1 + "/yetki/ekranlar"))).GetArrayLength());
        await Problem(await Send(otherAdmin, HttpMethod.Delete, V1 + "/yetki/ekranlar/personel"), HttpStatusCode.NotFound, null);

        var copy = await Json(await Send(admin, HttpMethod.Post, V1 + "/yetki/kopyala", new { kaynak = "Yonetici", hedef = "Muhasebe" }));
        Assert.Equal(1, copy.GetProperty("adet").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Delete, V1 + "/yetki/ekranlar/personel")).StatusCode);

        var matrix = await Json(await admin.C.GetAsync(V1 + "/yetki/matris"));
        var op = matrix.EnumerateArray().Single(x => x.GetProperty("rol").GetString() == "Operator");
        Assert.Equal(["OperationsWrite"], op.GetProperty("izinler").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task Audit_is_read_only_and_tenant_scoped()
    {
        var a = await _kit.SetupAsync();
        var b = await _kit.SetupAsync();
        var adminB = await _kit.LoginAsync(b, Who.Admin);
        await Json(await Send(adminB, HttpMethod.Post, V1 + "/yetki/ekranlar", new { ekranKodu = "gizli-b", roller = new[] { "Admin" } }));

        var adminA = await _kit.LoginAsync(a, Who.Admin);
        var page = await Json(await adminA.C.GetAsync(V1 + "/denetim?tablo=EkranYetkileri&boyut=200"));
        Assert.DoesNotContain("gizli-b", page.ToString(), StringComparison.Ordinal);
        Assert.NotEmpty((await Json(await adminB.C.GetAsync(V1 + "/denetim?tablo=EkranYetkileri"))).GetProperty("kayitlar").EnumerateArray());
        await Problem(await adminA.C.GetAsync(V1 + "/denetim?islem=Yok"), HttpStatusCode.BadRequest, "dogrulama", "islem");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(adminA, HttpMethod.Delete, V1 + "/denetim")).StatusCode);
    }

    [Fact]
    public async Task Notifications_and_search_are_open_to_any_session_and_tenant_scoped()
    {
        var a = await _kit.SetupAsync();
        var b = await _kit.SetupAsync();
        var vehicle = Guid.NewGuid();
        var own = new Bildirim { Tur = "Sigorta", VehicleId = vehicle, VadeTarihi = TestZaman.GunSonra(5), Mesaj = "Sigorta bitiyor" };
        await _kit.WriteAsync(a.TenantId, db => db.Bildirimler.Add(own));
        var foreign = new Bildirim { Tur = "Muayene", VehicleId = vehicle, VadeTarihi = TestZaman.GunSonra(6), Mesaj = "Yabancı" };
        await _kit.WriteAsync(b.TenantId, db => db.Bildirimler.Add(foreign));

        var op = await _kit.LoginAsync(a, Who.OperatorA);
        var center = await Json(await op.C.GetAsync(V1 + "/bildirimler"));
        Assert.Equal(1, center.GetProperty("okunmamis").GetInt32());
        Assert.Equal("Sigorta bitiyor", Assert.Single(center.GetProperty("bildirimler").EnumerateArray()).GetProperty("mesaj").GetString());

        await Problem(await Send(op, HttpMethod.Post, $"{V1}/bildirimler/{foreign.Id}/oku"), HttpStatusCode.NotFound, null);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Post, $"{V1}/bildirimler/{own.Id}/oku")).StatusCode);
        Assert.Equal(0, (await Json(await op.C.GetAsync(V1 + "/bildirimler"))).GetProperty("okunmamis").GetInt32());

        Assert.Equal(0, (await Json(await op.C.GetAsync(V1 + "/ara?q=x"))).GetArrayLength());
        await Problem(await op.C.GetAsync(V1 + "/ara?q=" + new string('a', 101)), HttpStatusCode.BadRequest, "dogrulama", "q");
    }
}
