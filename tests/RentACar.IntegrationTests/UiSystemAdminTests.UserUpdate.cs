using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul d-sistem-kullanici-09: var olan kullanıcının rolü ve atanmış şubesi değiştirilebilir (PUT /kullanicilar/{id},
/// ManageUsers, zorunlu <c>surum</c>, bayat sürüm 409 <c>cakisma</c>). ORACLE: beklenen etkin izinler rol matrisinden
/// ELLE yazıldı (Yönetici: OperationsWrite, OperationsDelete, FinanceWrite, FinanceReverse, ViewReports); şube kimlikleri
/// DB'den ada göre okunur.
/// </summary>
public sealed partial class UiSystemAdminTests
{
    private static object UpdateBody(string? rol, string? sube, string? surum) => new { rol, atanmisSube = sube, surum };

    private static string? VersionOf(JsonElement user) => user.GetProperty("surum").GetString();

    [Fact]
    public async Task User_role_and_branch_update_with_version_and_branch_validation()
    {
        var e = await _kit.SetupAsync();
        await _kit.WriteAsync(e.TenantId, db => db.Branches.AddRange(
            new Branch { Kod = "SA", Ad = "SubeA", Aktif = true },
            new Branch { Kod = "SB", Ad = "SubeB", Aktif = true },
            new Branch { Kod = "ES", Ad = "Eski", Aktif = false }));
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var target = e.UserIds[Who.OperatorA];
        var url = $"{Users}/{target}";

        var before = await Json(await admin.C.GetAsync(url));
        var v1 = VersionOf(before);
        Assert.False(string.IsNullOrEmpty(v1));

        // Doğrulamalar: sürüm zorunlu; rol geçerli; şube firmanın AKTİF şubesi (yok/pasif → 400).
        await Problem(await Send(admin, HttpMethod.Put, url, UpdateBody("Yonetici", null, null)), HttpStatusCode.BadRequest, "dogrulama", "surum");
        await Problem(await Send(admin, HttpMethod.Put, url, UpdateBody("Patron", null, v1)), HttpStatusCode.BadRequest, "dogrulama", "rol");
        await Problem(await Send(admin, HttpMethod.Put, url, UpdateBody("Operator", "YokSube", v1)), HttpStatusCode.BadRequest, "dogrulama", "atanmisSube");
        await Problem(await Send(admin, HttpMethod.Put, url, UpdateBody("Operator", "Eski", v1)), HttpStatusCode.BadRequest, "dogrulama", "atanmisSube");

        // Operatör → Yönetici, şube kaldırılır.
        var promoted = await Json(await Send(admin, HttpMethod.Put, url, UpdateBody("Yonetici", null, v1)));
        Assert.Equal("Yonetici", promoted.GetProperty("rol").GetString());
        Assert.Equal(JsonValueKind.Null, promoted.GetProperty("atanmisSube").ValueKind);
        Assert.Equal(new[] { "OperationsWrite", "OperationsDelete", "FinanceWrite", "FinanceReverse", "ViewReports" }.Order(),
            promoted.GetProperty("etkinIzinler").EnumerateArray().Select(x => x.GetString()!).Order());
        Assert.NotEqual(v1, VersionOf(promoted));
        var row = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == target));
        Assert.Equal((UserRole.Yonetici, (string?)null, (Guid?)null), (row.Rol, row.AtanmisSube, row.AtanmisSubeId));

        // Bayat sürüm → 409 cakisma, hiçbir şey yazılmaz.
        await Problem(await Send(admin, HttpMethod.Put, url, UpdateBody("Muhasebe", null, v1)), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal(UserRole.Yonetici, (await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == target))).Rol);

        // Yönetici → Operatör @ SubeB: metin + FK birlikte.
        var moved = await Json(await Send(admin, HttpMethod.Put, url, UpdateBody("Operator", "SubeB", VersionOf(promoted))));
        Assert.Equal("SubeB", moved.GetProperty("atanmisSube").GetString());
        var subeB = await _kit.ReadAsync(e.TenantId, db => db.Branches.AsNoTracking().Where(b => b.Kod == "SB").Select(b => b.Id).FirstAsync());
        row = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == target));
        Assert.Equal((UserRole.Operator, "SubeB", (Guid?)subeB), (row.Rol, row.AtanmisSube, row.AtanmisSubeId));

        // Yeni rol ve şube bir sonraki girişte geçerli.
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        var ben = await Json(await op.C.GetAsync(V1 + "/oturum/ben"));
        Assert.Equal("Operator", ben.GetProperty("rol").GetString());
        Assert.Equal("SubeB", ben.GetProperty("subeKapsami").GetProperty("subeAd").GetString());
    }

    [Fact]
    public async Task User_update_guards_self_admin_role_permission_and_tenant()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var self = $"{Users}/{e.UserIds[Who.Admin]}";

        // Kendi rolünü değiştiremez (kendini düşürüp son Admin'i yok etme yolu kapalı); hiçbir şey yazılmaz.
        var me = await Json(await admin.C.GetAsync(self));
        await Problem(await Send(admin, HttpMethod.Put, self, UpdateBody("Operator", null, VersionOf(me))), HttpStatusCode.BadRequest, "dogrulama", "rol");
        Assert.Equal(UserRole.Admin, (await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == e.UserIds[Who.Admin]))).Rol);

        // ManageUsers istisnalı Yönetici: Admin hesabına dokunamaz ve kimseyi Admin yapamaz (403).
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Manager]}/istisnalar/ManageUsers", new { ver = true }));
        var manager = await _kit.LoginAsync(e, Who.Manager);
        var admin2 = await Json(await manager.C.GetAsync($"{Users}/{e.UserIds[Who.Admin2]}"));
        await Problem(await Send(manager, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}", UpdateBody("Operator", null, VersionOf(admin2))),
            HttpStatusCode.Forbidden, "yetki_yok");
        var accounting = await Json(await manager.C.GetAsync($"{Users}/{e.UserIds[Who.Accounting]}"));
        await Problem(await Send(manager, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Accounting]}", UpdateBody("Admin", null, VersionOf(accounting))),
            HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(UserRole.Admin, (await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == e.UserIds[Who.Admin2]))).Rol);
        Assert.Equal(UserRole.Muhasebe, (await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == e.UserIds[Who.Accounting]))).Rol);

        // ManageUsers'sız kullanıcı 403; başka kiracının kullanıcısı 404.
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        await Problem(await Send(op, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Accounting]}", UpdateBody("Operator", null, VersionOf(accounting))),
            HttpStatusCode.Forbidden, "yetki_yok");
        var other = await _kit.SetupAsync();
        await Problem(await Send(admin, HttpMethod.Put, $"{Users}/{other.UserIds[Who.OperatorA]}", UpdateBody("Muhasebe", null, "1")),
            HttpStatusCode.NotFound, null);
        Assert.Equal(UserRole.Operator,
            (await _kit.ReadAsync(other.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == other.UserIds[Who.OperatorA]))).Rol);
    }
}
