using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    private const string Users = V1 + "/kullanicilar";

    private Task<int> ActiveAdminsAsync(Env e) => _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking()
        .CountAsync(u => u.TenantId == e.TenantId && u.IsActive && u.Rol == Domain.Enums.UserRole.Admin));

    // M1 — iki Admin eşzamanlı birbirini pasifleştirir: en az bir aktif Admin kalmalı.
    [Fact]
    public async Task Concurrent_mutual_admin_deactivation_leaves_an_active_admin()
    {
        for (var round = 0; round < 4; round++)
        {
            var e = await _kit.SetupAsync();
            var a = await _kit.LoginAsync(e, Who.Admin);
            var b = await _kit.LoginAsync(e, Who.Admin2);
            var results = await Task.WhenAll(
                Send(a, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin2]}/aktif", new { aktif = false }),
                Send(b, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/aktif", new { aktif = false }));
            Assert.Equal(1, await ActiveAdminsAsync(e));
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);
        }
    }

    // M2 — ManageUsers istisnalı Yönetici Admin hesabını ele geçiremez.
    [Fact]
    public async Task Manager_with_manage_users_cannot_take_over_admin_accounts()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Manager]}/istisnalar/ManageUsers", new { ver = true }));
        var manager = await _kit.LoginAsync(e, Who.Manager);
        var pw = WebFixture.RastgeleParola();
        var before = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().CountAsync(u => u.TenantId == e.TenantId));

        await Problem(await Send(manager, HttpMethod.Post, Users, new { kullaniciAdi = Random("adm"), rol = "Admin", sifre = pw }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(manager, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/sifre", new { sifre = pw }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(manager, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin2]}/aktif", new { aktif = false }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(manager, HttpMethod.Put, $"{Users}/{e.UserIds[Who.OperatorA]}/istisnalar/ManageUsers", new { ver = true }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(manager, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}/istisnalar/ViewReports", new { ver = false }),
            HttpStatusCode.Forbidden, "yetki_yok");

        // Hiçbir şey değişmedi: kullanıcı sayısı aynı, Admin eski parolasıyla girer, Admin2 aktif, operatörde istisna yok.
        Assert.Equal(before, await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().CountAsync(u => u.TenantId == e.TenantId)));
        await _kit.LoginAsync(e, Who.Admin);
        Assert.Equal(2, await ActiveAdminsAsync(e));
        Assert.Equal(0, await _kit.ReadAsync(e.TenantId, db => db.KullaniciIzinIstisnalari.AsNoTracking()
            .CountAsync(x => x.UserId == e.UserIds[Who.OperatorA])));

        // Admin olmayan hedefte yönetici işlemi hâlâ çalışır.
        await Json(await Send(manager, HttpMethod.Post, Users, new { kullaniciAdi = Random("op"), rol = "Operator", sifre = pw }),
            HttpStatusCode.Created);
    }

    [Fact]
    public async Task User_management_actions_are_audited_without_secrets()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var pw = WebFixture.RastgeleParola();
        var name = Random("yeni");
        var created = await Json(await Send(admin, HttpMethod.Post, Users, new { kullaniciAdi = name, rol = "Operator", sifre = pw }),
            HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        var reset = WebFixture.RastgeleParola();
        Assert.Equal(HttpStatusCode.NoContent, (await Send(admin, HttpMethod.Post, $"{Users}/{id}/sifre", new { sifre = reset })).StatusCode);
        await Json(await Send(admin, HttpMethod.Post, $"{Users}/{id}/aktif", new { aktif = false }));
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{id}/istisnalar/ViewReports", new { ver = true }));

        var rows = await _kit.ReadAsync(e.TenantId, db => db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Users" && a.EntityId == id.ToString()).ToListAsync());
        var text = string.Join("\n", rows.Select(r => r.NewValues));
        foreach (var op in new[] { "KullaniciOlusturma", "ParolaSifirlama", "KullaniciPasif", "IzinIstisnasiVer" })
            Assert.Contains(op, text, StringComparison.Ordinal);
        Assert.All(rows, r => Assert.Equal(e.UserIds[Who.Admin], r.UserId));
        var hash = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.PasswordHash).FirstAsync());
        foreach (var secret in new[] { pw, reset, hash })
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
    }
}
