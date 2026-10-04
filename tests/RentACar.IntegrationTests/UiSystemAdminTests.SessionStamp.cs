using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Users;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Identity;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// Güvenlik incelemesi #374 F1/F2 (probe'lar kalıcı teste çevrildi). F1: rolü düşürülen Admin'in AÇIK oturumu (kayan
/// çerez → süresiz) yeni Admin açabiliyor ve kalan Admin'in parolasını sıfırlayabiliyordu. Artık rol/şube/aktiflik/
/// parola/istisna değişince oturum damgası yenilenir; eski oturum bir sonraki istekte 401 <c>oturum_yok</c> alır.
/// Antiforgery bozulmaz: değişikliği yapan aktörün oturumu ve XSRF belirteci geçerli kalır.
/// F2: kendi şubesini değiştiremez; kendi kıdeminden yüksek rol veremez.
/// </summary>
public sealed partial class UiSystemAdminTests
{
    private async Task<string?> VersionOfAsync(Session s, Guid id)
        => (await Json(await s.C.GetAsync($"{Users}/{id}"))).GetProperty("surum").GetString();

    [Fact]
    public async Task F1_rolu_dusurulen_adminin_acik_oturumu_duser_aktorun_oturumu_ve_xsrf_gecerli_kalir()
    {
        var e = await _kit.SetupAsync();
        var a = await _kit.LoginAsync(e, Who.Admin);
        var b = await _kit.LoginAsync(e, Who.Admin2); // B'nin oturumu düşürülmeden ÖNCE açık
        Assert.Equal(HttpStatusCode.OK, (await b.C.GetAsync(Users)).StatusCode);

        await Json(await Send(a, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}",
            UpdateBody("Operator", null, await VersionOfAsync(a, e.UserIds[Who.Admin2]))));

        // B'nin eski oturumu: yeni Admin açamaz, kalan Admin'in parolasını sıfırlayamaz — oturum düşmüştür.
        await Problem(await Send(b, HttpMethod.Post, Users,
                new { kullaniciAdi = Random("arka"), gorunenAd = "x", rol = "Admin", sifre = WebFixture.RandomPassword() }),
            HttpStatusCode.Unauthorized, "oturum_yok");
        await Problem(await Send(b, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/sifre", new { sifre = WebFixture.RandomPassword() }),
            HttpStatusCode.Unauthorized, "oturum_yok");
        Assert.Equal(1, await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking()
            .CountAsync(u => u.TenantId == e.TenantId && u.Rol == UserRole.Admin)));

        // Aktörün (A) oturumu ve XSRF belirteci geçerli: yazma 201 (antiforgery bozulmadı).
        var created = await Send(a, HttpMethod.Post, Users,
            new { kullaniciAdi = Random("yeni"), gorunenAd = "y", rol = "Operator", sifre = WebFixture.RandomPassword() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // B yeniden girişte yeni rolüyle çalışır (Operatör → kullanıcı yönetimi 403).
        var b2 = await _kit.LoginAsync(e, Who.Admin2);
        await Problem(await b2.C.GetAsync(Users), HttpStatusCode.Forbidden, "yetki_yok");
    }

    [Fact]
    public async Task F1_pasiflestirme_istisna_ve_parola_sifirlama_acik_oturumu_dusurur()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);

        var op = await _kit.LoginAsync(e, Who.OperatorA);
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[Who.OperatorA]}/istisnalar/ViewReports", new { ver = true }));
        await Problem(await op.C.GetAsync(V1 + "/oturum/ben"), HttpStatusCode.Unauthorized, "oturum_yok");

        var acc = await _kit.LoginAsync(e, Who.Accounting);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Accounting]}/sifre", new { sifre = WebFixture.RandomPassword() })).StatusCode);
        await Problem(await acc.C.GetAsync(V1 + "/oturum/ben"), HttpStatusCode.Unauthorized, "oturum_yok");

        var manager = await _kit.LoginAsync(e, Who.Manager);
        await Json(await Send(admin, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Manager]}/aktif", new { aktif = false }));
        await Problem(await manager.C.GetAsync(V1 + "/oturum/ben"), HttpStatusCode.Unauthorized, "oturum_yok");

        // Değişikliği yapan Admin'in oturumu hiçbirinden etkilenmedi.
        Assert.Equal(HttpStatusCode.OK, (await admin.C.GetAsync(V1 + "/oturum/ben")).StatusCode);
    }

    [Fact]
    public async Task F1_servis_aktor_rolunu_DBden_dogrular()
    {
        var e = await _kit.SetupAsync();
        var a = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(a, HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}",
            UpdateBody("Operator", null, await VersionOfAsync(a, e.UserIds[Who.Admin2]))));

        // Damga önbelleği bayat olsa / başka kimlik yolunda bile: eski "Admin" kimliği DB'de Operatör → 403.
        using var host = new TestHost(_kit.Fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId, e.UserIds[Who.Admin2], "b", UserRole.Admin);
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        await Assert.ThrowsAsync<NoPermissionException>(() => users.CreateAsync(new UserInput
        {
            UserName = Random("arka"), DisplayName = "x", Rol = UserRole.Admin, Password = WebFixture.RandomPassword(),
        }));
        await Assert.ThrowsAsync<NoPermissionException>(() => users.ResetPasswordAsync(e.UserIds[Who.Admin], WebFixture.RandomPassword()));
    }

    [Fact]
    public async Task F1_fazla_alanlar_yok_sayilir_parola_ve_kimlik_degismez()
    {
        var e = await _kit.SetupAsync();
        await _kit.WriteAsync(e.TenantId, db => db.Branches.Add(new Branch { Kod = "SA", Ad = "SubeA", Aktif = true }));
        var a = await _kit.LoginAsync(e, Who.Admin);
        var target = e.UserIds[Who.OperatorA];
        var before = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == target));
        await Json(await Send(a, HttpMethod.Put, $"{Users}/{target}", new
        {
            rol = "Operator", atanmisSube = "SubeA", surum = await VersionOfAsync(a, target),
            aktif = false, sifre = "Hack123456", passwordHash = "x", kullaniciAdi = "degisti", gorunenAd = "degisti",
            tenantId = Guid.NewGuid(), istisnalar = new[] { new { izin = "ManageUsers", ver = true } },
            atanmisSubeId = Guid.NewGuid(), id = Guid.NewGuid(),
        }));
        var after = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == target));
        Assert.Equal((before.IsActive, before.PasswordHash, before.UserName, before.DisplayName, before.TenantId),
            (after.IsActive, after.PasswordHash, after.UserName, after.DisplayName, after.TenantId));
        Assert.Equal(0, await _kit.ReadAsync(e.TenantId, db => db.KullaniciIzinIstisnalari.AsNoTracking().CountAsync(x => x.UserId == target)));
    }

    [Fact]
    public async Task F2_admin_kendi_subesini_degistiremez()
    {
        var e = await _kit.SetupAsync();
        await _kit.WriteAsync(e.TenantId, db => db.Branches.Add(new Branch { Kod = "SA", Ad = "SubeA", Aktif = true }));
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var self = e.UserIds[Who.Admin];
        await Problem(await Send(admin, HttpMethod.Put, $"{Users}/{self}", UpdateBody("Admin", "SubeA", await VersionOfAsync(admin, self))),
            HttpStatusCode.BadRequest, "dogrulama", "atanmisSube");
        Assert.Null((await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == self))).AtanmisSube);
    }

    // ---- güvenlik tur 2 M1: kullanıcı/yetki YAZMALARI yalnız Admin rolü (ManageUsers istisnası açmaz) ----

    [Theory]
    [InlineData(Who.OperatorA)]
    [InlineData(Who.Manager)]
    public async Task M1_ManageUsers_istisnali_admin_olmayan_hicbir_yonetim_yazmasi_yapamaz(Who who)
    {
        var e = await _kit.SetupAsync();
        await _kit.WriteAsync(e.TenantId, db => db.Branches.Add(new Branch { Kod = "SA", Ad = "SubeA", Aktif = true }));
        var admin = await _kit.LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Put, $"{Users}/{e.UserIds[who]}/istisnalar/ManageUsers", new { ver = true }));
        var actor = await _kit.LoginAsync(e, who);
        var accId = e.UserIds[Who.Accounting];
        var before = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().CountAsync(u => u.TenantId == e.TenantId));

        // Okuma ManageUsers ile açık kalır.
        Assert.Equal(HttpStatusCode.OK, (await actor.C.GetAsync(Users)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actor.C.GetAsync(V1 + "/yetki/ekranlar")).StatusCode);

        var writes = new (HttpMethod M, string Url, object? Body)[]
        {
            (HttpMethod.Post, Users, new { kullaniciAdi = Random("k"), gorunenAd = "x", rol = "Operator", sifre = WebFixture.RandomPassword() }),
            (HttpMethod.Post, Users, new { kullaniciAdi = Random("k"), gorunenAd = "x", rol = "Muhasebe", sifre = WebFixture.RandomPassword() }),
            (HttpMethod.Put, $"{Users}/{accId}", UpdateBody("Operator", "SubeA", await VersionOfAsync(admin, accId))),
            (HttpMethod.Post, $"{Users}/{accId}/aktif", new { aktif = false }),
            (HttpMethod.Post, $"{Users}/{accId}/sifre", new { sifre = WebFixture.RandomPassword() }),
            (HttpMethod.Put, $"{Users}/{accId}/istisnalar/OperationsDelete", new { ver = true }),
            (HttpMethod.Put, $"{Users}/{e.UserIds[Who.Admin2]}/istisnalar/OperationsWrite", new { ver = false }),
            (HttpMethod.Delete, $"{Users}/{e.UserIds[who]}/istisnalar/ManageUsers", null),
            (HttpMethod.Post, V1 + "/yetki/ekranlar", new { ekranKodu = "personel", roller = new[] { "Operator" }, aktif = true }),
            (HttpMethod.Post, V1 + "/yetki/kopyala", new { kaynak = "Operator", hedef = "Muhasebe" }),
            (HttpMethod.Post, V1 + "/yetki/gruplar", new { ad = "sablon" }),
        };
        foreach (var (m, url, body) in writes)
        {
            var r = await Send(actor, m, url, body);
            var text = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{who} {m} {url}: beklenen 403, gelen {(int)r.StatusCode}: {text}");
        }

        // Hiçbir şey yazılmadı.
        Assert.Equal(before, await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().CountAsync(u => u.TenantId == e.TenantId)));
        var acc = await _kit.ReadAsync(e.TenantId, db => db.Users.AsNoTracking().FirstAsync(u => u.Id == accId));
        Assert.Equal((UserRole.Muhasebe, true), (acc.Rol, acc.IsActive));
        Assert.Equal(0, await _kit.ReadAsync(e.TenantId, db => db.KullaniciIzinIstisnalari.AsNoTracking().CountAsync(x => x.UserId == accId)));
        Assert.Equal(0, await _kit.ReadAsync(e.TenantId, db => db.KullaniciIzinIstisnalari.AsNoTracking()
            .CountAsync(x => x.UserId == e.UserIds[Who.Admin2])));
    }

    [Fact]
    public async Task L1_baska_surecte_dusurulen_admin_onbellek_bayatken_de_yonetim_yazamaz()
    {
        var e = await _kit.SetupAsync();
        var b = await _kit.LoginAsync(e, Who.Admin2);
        Assert.Equal(HttpStatusCode.OK, (await b.C.GetAsync(Users)).StatusCode); // damga önbelleği dolu
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_kit.Fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            // Başka süreç (ikinci Web örneği) yazdı: bu sürecin önbelleği geçersiz kılınmadı.
            var u = await db.Users.SingleAsync(x => x.Id == e.UserIds[Who.Admin2]);
            u.Rol = UserRole.Operator;
            u.GuvenlikDamgasi = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
        }
        var r = await Send(b, HttpMethod.Post, Users, new { kullaniciAdi = Random("arka"), gorunenAd = "x", rol = "Admin", sifre = WebFixture.RandomPassword() });
        Assert.True(r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"Admin açıldı: {(int)r.StatusCode}");
        var p = await Send(b, HttpMethod.Post, $"{Users}/{e.UserIds[Who.Admin]}/sifre", new { sifre = WebFixture.RandomPassword() });
        Assert.True(p.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"parola sıfırlandı: {(int)p.StatusCode}");
        // Bayatlık penceresi (L1) en çok 10 sn.
        Assert.True(UserSessionStateCache.Ttl <= TimeSpan.FromSeconds(10));
    }

    // L2 (JWT damgası) ApiSessionStampTests'te — "postgres" koleksiyonunda: Api host'u bu koleksiyondaki Web host'unun
    // Serilog'unu susturuyordu (UiIstemciHataTests log olayını bulamıyordu).
}
