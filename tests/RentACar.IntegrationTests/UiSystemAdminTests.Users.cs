using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemAdminTests
{
    private const string Users = V1 + "/kullanicilar";

    private static JsonElement UserOf(JsonElement list, Guid id)
        => list.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task User_list_has_no_hash_and_create_validates_branch_and_role()
    {
        var e = await _kit.SetupAsync();
        await _kit.WriteAsync(e.TenantId, db => db.Branches.Add(new Branch { Kod = "SA", Ad = "SubeA", Aktif = true }));
        var admin = await _kit.LoginAsync(e, Who.Admin);

        var text = await (await admin.C.GetAsync(Users)).Content.ReadAsStringAsync();
        var hash = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking()
            .Where(u => u.TenantId == e.TenantId).Select(u => u.PasswordHash).FirstAsync());
        Assert.DoesNotContain(hash, text, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, JsonDocument.Parse(text).RootElement.GetArrayLength());

        var name = Random("yeni");
        var pw = WebFixture.RastgeleParola();
        await Problem(await Send(admin, HttpMethod.Post, Users, new { kullaniciAdi = name, rol = "Operator", sifre = pw, atanmisSube = "YokSube" }),
            HttpStatusCode.BadRequest, "dogrulama", "atanmisSube");
        await Problem(await Send(admin, HttpMethod.Post, Users, new { kullaniciAdi = name, rol = "Patron", sifre = pw }),
            HttpStatusCode.BadRequest, "dogrulama", "rol");
        await Problem(await Send(admin, HttpMethod.Post, Users, new { kullaniciAdi = name, rol = "Operator", sifre = "123" }),
            HttpStatusCode.BadRequest, "dogrulama", "sifre");
        var created = await Json(await Send(admin, HttpMethod.Post, Users, new { kullaniciAdi = name, rol = "Operator", sifre = pw, atanmisSube = "SubeA" }),
            HttpStatusCode.Created);
        Assert.Equal("Operator", created.GetProperty("rol").GetString());
        Assert.Equal("SubeA", created.GetProperty("atanmisSube").GetString());
        Assert.Equal(["OperationsWrite"], created.GetProperty("etkinIzinler").EnumerateArray().Select(x => x.GetString()).ToArray());
        await Problem(await Send(admin, HttpMethod.Post, Users, new { kullaniciAdi = name, rol = "Operator", sifre = pw }),
            HttpStatusCode.BadRequest, "dogrulama", "kullaniciAdi");
    }

    [Fact]
    public async Task Users_of_another_tenant_are_not_found()
    {
        var a = await _kit.SetupAsync();
        var b = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(a, Who.Admin);
        var foreign = b.UserIds[Who.OperatorA];

        await Problem(await admin.C.GetAsync($"{Users}/{foreign}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(admin, HttpMethod.Post, $"{Users}/{foreign}/aktif", new { aktif = false }), HttpStatusCode.NotFound, null);
        await Problem(await Send(admin, HttpMethod.Post, $"{Users}/{foreign}/sifre", new { sifre = WebFixture.RastgeleParola() }), HttpStatusCode.NotFound, null);
        await Problem(await Send(admin, HttpMethod.Put, $"{Users}/{foreign}/istisnalar/ViewReports", new { ver = true }), HttpStatusCode.NotFound, null);
        var list = await Json(await admin.C.GetAsync(Users));
        Assert.DoesNotContain(list.EnumerateArray(), u => u.GetProperty("id").GetGuid() == foreign);

        // Hiçbir şey yazılmadı: yabancı kullanıcı aktif, eski parolasıyla girebilir, istisnası yok.
        await _kit.LoginAsync(b, Who.OperatorA);
        Assert.Equal(0, await _kit.ReadAsync(b.TenantId, db => db.KullaniciIzinIstisnalari.AsNoTracking().CountAsync(x => x.UserId == foreign)));
    }

    [Fact]
    public async Task Lockout_guards_self_and_last_admin()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Problem(await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/aktif", new { aktif = false }),
            HttpStatusCode.BadRequest, "dogrulama", "aktif");
        await Problem(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin]}/istisnalar/ViewReports", new { ver = false }),
            HttpStatusCode.BadRequest, "dogrulama", "izin");
        await Problem(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}/istisnalar/ManageUsers", new { ver = false }),
            HttpStatusCode.BadRequest, "dogrulama", "izin");
        await Problem(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}/istisnalar/Tanrı", new { ver = true }),
            HttpStatusCode.BadRequest, "dogrulama", "izin");

        // Yöneticiye kullanıcı yönetimi istisnası; Admin2 pasif → tek aktif Admin kaldı.
        var granted = await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Manager]}/istisnalar/ManageUsers", new { ver = true }));
        Assert.Contains("ManageUsers", granted.GetProperty("etkinIzinler").EnumerateArray().Select(x => x.GetString()));
        var off = await Json(await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin2]}/aktif", new { aktif = false }));
        Assert.False(off.GetProperty("aktif").GetBoolean());

        var manager = await _kit.LoginAsync(e, Who.Manager); // istisna girişte claim'e yazılır
        await Problem(await Send(manager, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/aktif", new { aktif = false }),
            HttpStatusCode.BadRequest, "dogrulama", "aktif");
        Assert.True(await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().Where(u => u.Id == e.UserIds[Who.Admin]).Select(u => u.IsActive).FirstAsync()));

        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(admin, HttpMethod.Delete, $"{Users}/{e.UserIds[Who.Manager]}/istisnalar/ManageUsers")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_and_own_password_change()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var reset = WebFixture.RastgeleParola();
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.OperatorA]}/sifre", new { sifre = reset })).StatusCode);
        var op = await _kit.LoginAsync(e, Who.OperatorA, reset);

        var next = WebFixture.RastgeleParola();
        await Problem(await Send(op, HttpMethod.Post, V1 + "/profil/sifre", new { eskiSifre = "yanlis-parola", yeniSifre = next }),
            HttpStatusCode.BadRequest, "dogrulama", "eskiSifre");
        await Problem(await Send(op, HttpMethod.Post, V1 + "/profil/sifre", new { eskiSifre = reset, yeniSifre = next, yeniSifreTekrar = next + "x" }),
            HttpStatusCode.BadRequest, "dogrulama", "yeniSifreTekrar");
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(op, HttpMethod.Post, V1 + "/profil/sifre", new { eskiSifre = reset, yeniSifre = next, yeniSifreTekrar = next })).StatusCode);
        await _kit.LoginAsync(e, Who.OperatorA, next);
    }
}
