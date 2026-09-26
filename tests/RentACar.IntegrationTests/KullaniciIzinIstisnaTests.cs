using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Infrastructure.Persistence;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Users;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Identity;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kullanıcı-bazlı izin istisnaları (2026-08-17): rol matrisinin üstüne tek kullanıcıya ek izin /
/// yasak. Karar bileşimi <c>etkin = !yasak && (matris || ek)</c> — yasak DAİMA kazanır.
/// Beklenen değerler ELLE kurulur (bağımsız oracle); istisna adları string taşınır, bilinmeyen
/// ad yok sayılır.
/// </summary>
public sealed class EffectivePermissionTests
{
    [Fact]
    public void Ek_izin_matris_disini_ACAR()
        => Assert.True(EffectivePermission.Has(UserRole.Operator, Permission.ViewReports,
            extraPermissions: ["ViewReports"], deniedPermissions: []));

    [Fact]
    public void Yasak_matristen_geleni_KESER()
        => Assert.False(EffectivePermission.Has(UserRole.Yonetici, Permission.FinanceReverse,
            extraPermissions: [], deniedPermissions: ["FinanceReverse"]));

    [Fact]
    public void Yasak_ek_izinden_USTUNDUR()
        => Assert.False(EffectivePermission.Has(UserRole.Operator, Permission.ViewReports,
            extraPermissions: ["ViewReports"], deniedPermissions: ["ViewReports"]));

    [Fact]
    public void Yasak_admin_rolunu_bile_keser()
        => Assert.False(EffectivePermission.Has(UserRole.Admin, Permission.FinanceWrite,
            extraPermissions: [], deniedPermissions: ["FinanceWrite"]));

    [Fact]
    public void Bilinmeyen_istisna_adi_yok_sayilir()
    {
        // Eski/bozuk claim yeni koda zarar veremez: ne açar ne kapatır.
        Assert.False(EffectivePermission.Has(UserRole.Operator, Permission.ViewReports,
            extraPermissions: ["OlmayanIzin"], deniedPermissions: []));
        Assert.True(EffectivePermission.Has(UserRole.Operator, Permission.OperationsWrite,
            extraPermissions: [], deniedPermissions: ["OlmayanIzin"]));
    }

    [Fact]
    public void Istisnasiz_kullanici_birebir_matris()
    {
        foreach (var rol in Enum.GetValues<UserRole>())
            foreach (var permission in Enum.GetValues<Permission>())
                Assert.Equal(RolePermissions.Has(rol, permission),
                    EffectivePermission.Has(rol, permission, [], []));
    }
}

[Collection("postgres")]
public sealed class KullaniciIzinIstisnaTests(PostgresFixture fx)
{
    private static TestIdentity Identity(IServiceScope s)
        => s.ServiceProvider.GetRequiredService<TestIdentity>();

    // ---------------------------------------------------------------- guard bileşimi (servis katmanı)

    [Fact]
    public async Task Ek_izinli_operator_ViewReports_guardindan_GECER()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "op", UserRole.Operator);
        Identity(scope).EkIzinler = ["ViewReports"];

        // InvoiceService.ListLinesAsync ViewReports ister — istisnasız operatörde patlar (kontrol),
        // ek izinli operatörde geçer (boş liste döner, istisna izni verdi).
        var svc = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        Assert.Empty(await svc.ListLinesAsync());
    }

    [Fact]
    public async Task Yasakli_yonetici_ters_kayit_ATAMAZ_digerleri_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "yon", UserRole.Yonetici);
        Identity(scope).YasakIzinler = ["FinanceReverse"];

        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        // FinanceWrite hâlâ var: tahsilat girebilir.
        var operation = await cash.CollectAsync(new CashInput { CariId = await TestCustomer.NewAsync(scope.ServiceProvider), Tutar = 100m });
        // FinanceReverse yasak: aynı işlemin tersini ATAMAZ — tam da istenen "tek kişiden yalnız
        // ters-kayıt yetkisi alınabilsin" senaryosu.
        var ex = await Assert.ThrowsAsync<NoPermissionException>(() => cash.ReverseAsync(operation));
        Assert.Contains("FinanceReverse", ex.Message);
    }

    // ---------------------------------------------------------------- istisna yönetimi guard'ları

    [Fact]
    public async Task Istisna_yonetimi_ManageUsers_ister_ve_kendine_dokunamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        // Yönetici (ManageUsers YOK) istisna listeleyemez/yazamaz.
        using (var yon = host.ScopeFor(tenant, Guid.NewGuid(), "yon", UserRole.Yonetici))
        {
            var svc = yon.ServiceProvider.GetRequiredService<UserPermissionService>();
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.ListAsync());
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.SetAsync(Guid.NewGuid(), "ViewReports", true));
        }

        // Admin KENDİ istisnasını değiştiremez (yetki yükseltme + kendini kilitleme aynı kapıda).
        using (var admin = host.ScopeFor(tenant, adminId, "admin", UserRole.Admin))
        {
            var svc = admin.ServiceProvider.GetRequiredService<UserPermissionService>();
            var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SetAsync(adminId, "ViewReports", true));
            Assert.Contains("Kendi izin istisnanızı", ex.Message);
        }
    }

    [Fact]
    public async Task Admin_rolunden_ManageUsers_YASAGI_konamaz_kilitlenme_kemeri()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "admin1", UserRole.Admin);

        // Gerçek bir Admin kullanıcı satırı gerekli (hedef rol kontrol DB'den okunur).
        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var targetAdmin = await users.CreateAsync(new UserInput
        { UserName = "admin2", DisplayName = "İkinci Admin", Rol = UserRole.Admin, Password = "sifre123" });

        var svc = scope.ServiceProvider.GetRequiredService<UserPermissionService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => svc.SetAsync(targetAdmin, "ManageUsers", give: false));
        Assert.Contains("alınamaz", ex.Message);

        // Ama Admin'e ViewReports YASAĞI konabilir (kilitlenme riski yok, bilinçli serbest).
        await svc.SetAsync(targetAdmin, "ViewReports", give: false);
        Assert.Single(await svc.ListAsync());
    }

    [Fact]
    public async Task Upsert_ayni_izinde_TEK_satir_son_yazan_kazanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant, Guid.NewGuid(), "admin", UserRole.Admin);

        var users = scope.ServiceProvider.GetRequiredService<UserService>();
        var target = await users.CreateAsync(new UserInput
        { UserName = "op1", DisplayName = "Operatör", Rol = UserRole.Operator, Password = "sifre123" });

        var svc = scope.ServiceProvider.GetRequiredService<UserPermissionService>();
        await svc.SetAsync(target, "ViewReports", give: true);
        await svc.SetAsync(target, "ViewReports", give: false); // çevirme: UPDATE, ikinci satır DEĞİL

        var row = Assert.Single(await svc.ListAsync());
        Assert.False(row.Ver); // son yazan kazandı
        Assert.Equal("ViewReports", row.Izin);

        Assert.True(await svc.RemoveAsync(target, "ViewReports"));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Gecersiz_izin_adi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "admin", UserRole.Admin);
        var svc = scope.ServiceProvider.GetRequiredService<UserPermissionService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.SetAsync(Guid.NewGuid(), "SuperAdmin", true));
    }

    // ---------------------------------------------------------------- login + izolasyon

    [Fact]
    public async Task Login_istisnalari_yukler_GUCsuz_bootstrap_yolu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid target;
        // Tenants platform tablosu: owner yazar, app okur — tenant satırı OWNER bağlantısıyla açılır
        // (racar_app INSERT'i 42501 permission denied ile reddeder; bu da başlı başına doğru davranış).
        await using (var ownerDb = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            ownerDb.Tenants.Add(new RentACar.Domain.Entities.Tenant
            { Id = tenant, Code = $"t{tenant:N}"[..8], Name = "Test", IsActive = true });
            await ownerDb.SaveChangesAsync();
        }

        using (var scope = host.ScopeFor(tenant, Guid.NewGuid(), "admin", UserRole.Admin))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserService>();
            target = await users.CreateAsync(new UserInput
            { UserName = "istisnali", DisplayName = "X", Rol = UserRole.Operator, Password = "sifre123" });

            var svc = scope.ServiceProvider.GetRequiredService<UserPermissionService>();
            await svc.SetAsync(target, "ViewReports", give: true);
            await svc.SetAsync(target, "OperationsWrite", give: false);
        }

        // Login kimliksiz scope'tan yapılır (gerçek hayattaki anonim istek — GUC yok):
        // istisna_select politikasının GUC-boşken açık olduğunun ampirik kanıtı.
        using (var anon = host.ScopeFor(null, null, null, role: null))
        {
            var login = anon.ServiceProvider.GetRequiredService<LoginService>();
            var result = await login.ValidateAsync($"t{tenant:N}"[..8], "istisnali", "sifre123");
            Assert.NotNull(result);
            Assert.Equal(["ViewReports"], result!.EkIzinler);
            Assert.Equal(["OperationsWrite"], result.YasakIzinler);
        }
    }

    [Fact]
    public async Task Istisna_izolasyonu_baska_tenant_gormez_ve_yazamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid userA;

        using (var a = host.ScopeFor(tenantA, Guid.NewGuid(), "adminA", UserRole.Admin))
        {
            var users = a.ServiceProvider.GetRequiredService<UserService>();
            userA = await users.CreateAsync(new UserInput
            { UserName = "opA", DisplayName = "A", Rol = UserRole.Operator, Password = "sifre123" });
            await a.ServiceProvider.GetRequiredService<UserPermissionService>()
                .SetAsync(userA, "ViewReports", true);
        }

        using (var b = host.ScopeFor(tenantB, Guid.NewGuid(), "adminB", UserRole.Admin))
        {
            var svc = b.ServiceProvider.GetRequiredService<UserPermissionService>();
            // B tenant'ı A'nın istisnasını LİSTEDE göremez (repo tenant filtresi + RLS).
            Assert.Empty(await svc.ListAsync());
            // B, A'nın kullanıcısına istisna YAZAMAZ — kullanıcı B'nin tenant'ında bulunamaz.
            await Assert.ThrowsAsync<ValidationException>(() => svc.SetAsync(userA, "ViewReports", false));
        }
    }
}
