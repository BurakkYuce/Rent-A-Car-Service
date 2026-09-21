using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-D — YetkiGrup (ekran-izni şablonu, "güvenli genişletme"). BAĞIMSIZ ORACLE: snapshot→uygula yapıyı geri
/// yükler; yönetim ManageUsers ister; tenant izole (racar_app); ve KRİTİK GÜVENLİK İNVARYANTI: uygulanan grup
/// yalnız ekran-override'ı yazar → rol-matrisi floor'u DEĞİŞMEZ, grant floor'u AŞAMAZ.
/// </summary>
[Collection("postgres")]
public sealed class YetkiGrupTests(PostgresFixture fx)
{
    private static ScreenPermissionService Svc(IServiceScope s) => s.ServiceProvider.GetRequiredService<ScreenPermissionService>();

    [Fact]
    public async Task Snapshot_ve_uygula_yapiyi_geri_yukler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using var admin = host.ScopeFor(t, role: UserRole.Admin);
        var svc = Svc(admin);

        await svc.SetAsync("kasa", new[] { UserRole.Admin, UserRole.Muhasebe });
        await svc.SetAsync("giderler", new[] { UserRole.Admin });
        await svc.SnapshotGrupAsync("Profil A");

        // Yapıyı boz: kasa override'ını kaldır.
        await svc.RemoveAsync("kasa");
        Assert.DoesNotContain(await svc.ListAsync(), x => x.EkranKodu == "kasa");

        // Uygula → snapshot geri yüklenir (2 kalem: kasa + giderler).
        var n = await svc.UygulaGrupAsync("Profil A");
        Assert.Equal(2, n);
        var kasa = (await svc.ListAsync()).FirstOrDefault(x => x.EkranKodu == "kasa");
        Assert.NotNull(kasa);
        Assert.Contains("Admin", kasa!.AllowedRolesCsv);
        Assert.Contains("Muhasebe", kasa.AllowedRolesCsv);
    }

    [Fact]
    public async Task Ayni_ad_snapshot_uzerine_yazar_tek_satir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using var admin = host.ScopeFor(t, role: UserRole.Admin);
        var svc = Svc(admin);
        await svc.SetAsync("kasa", new[] { UserRole.Admin });
        await svc.SnapshotGrupAsync("Profil A");
        await svc.SnapshotGrupAsync("Profil A"); // ikinci kez → upsert
        Assert.Single(await svc.ListGruplarAsync(), g => g.Ad == "Profil A");
    }

    [Fact]
    public async Task Grup_yonetimi_ManageUsers_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using var op = host.ScopeFor(t, role: UserRole.Operator); // ManageUsers YOK
        var svc = Svc(op);
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.ListGruplarAsync());
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.SnapshotGrupAsync("x"));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.UygulaGrupAsync("x"));
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.SilGrupAsync("x"));
    }

    [Fact]
    public async Task Grup_tenant_izole_racar_app()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();
        using (var a = host.ScopeFor(tA, role: UserRole.Admin))
        {
            await Svc(a).SetAsync("kasa", new[] { UserRole.Admin });
            await Svc(a).SnapshotGrupAsync("Profil A");
        }
        using var b = host.ScopeFor(tB, role: UserRole.Admin);
        Assert.Empty(await Svc(b).ListGruplarAsync()); // tenant B, A'nın grubunu GÖRMEZ (RLS)
    }

    [Fact]
    public async Task Uygulanan_grup_floor_asamaz() // KRİTİK GÜVENLİK İNVARYANTI
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        using (var admin = host.ScopeFor(t, role: UserRole.Admin))
        {
            // "Gevşek" şablon: kasa ekranını Operator'e "izinli" yazar (grant denemesi).
            await Svc(admin).SetAsync("kasa", new[] { UserRole.Operator });
            await Svc(admin).SnapshotGrupAsync("Gevsek");
            await Svc(admin).RemoveAsync("kasa");
            await Svc(admin).UygulaGrupAsync("Gevsek"); // kasa override'ı Operator ile geri gelir
        }
        using var op = host.ScopeFor(t, role: UserRole.Operator);
        // FinanceWrite floor'u Operator'de YOK → grup override "izinli" dese bile RED (grant floor'u aşamaz).
        Assert.False(await Svc(op).IsScreenAllowedAsync("kasa", Permission.FinanceWrite));
        // OperationsWrite floor'u VAR + override'da Operator → izin (yalnız sıkılaştırma katmanını geçer).
        Assert.True(await Svc(op).IsScreenAllowedAsync("kasa", Permission.OperationsWrite));
    }
}
