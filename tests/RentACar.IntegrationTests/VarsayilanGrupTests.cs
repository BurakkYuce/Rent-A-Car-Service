using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-10 — grubu belirtilmeden açılan aracın düşeceği varsayılan grup. Bağımsız oracle: beklenen
/// değerler senaryodan kurulur (hangi grup tanımlıysa o), <see cref="DefaultGroupResolver"/>'nün
/// kendi mantığından türetilmez.
///
/// Zincirin can alıcı noktası: eşleşme yoksa <b>null</b> döner — "ilk aktif grup" gibi bir fallback
/// YOKTUR. Aksi halde grubu unutulan bir araç "Lüks" segmentte yayına girebilirdi.
/// </summary>
[Collection("postgres")]
public sealed class VarsayilanGrupTests(PostgresFixture fx)
{
    private static VehicleGroupInput Group(string code, string name, bool active = true)
        => new() { Kod = code, Ad = name, Aktif = active };

    [Fact]
    public async Task Grup_bos_gelirse_Ekonomi_atanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await groups.CreateAsync(Group("LUX", "Lüks"));
        await groups.CreateAsync(Group("EKO", "Ekonomi"));

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 AAA 001" });

        Assert.Equal("Ekonomi", (await vehicles.GetAsync(id))!.Grup);
    }

    [Fact]
    public async Task Ekonomi_yoksa_grup_NULL_kalir_ilk_gruba_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        // "Lüks" tek aktif grup — yanlış segmentte yayınlamaktansa araç grupsuz (pending) kalmalı.
        await groups.CreateAsync(Group("LUX", "Lüks"));

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 AAA 002" });

        Assert.Null((await vehicles.GetAsync(id))!.Grup);
    }

    [Fact]
    public async Task Ayardaki_grup_Ekonomiye_TERCIH_EDILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var settings = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await groups.CreateAsync(Group("EKO", "Ekonomi"));
        var commercialId = await groups.CreateAsync(Group("TIC", "Ticari"));
        await settings.SaveAsync(new TenantSettingsModel { VarsayilanGrupId = commercialId });

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 AAA 003" });

        Assert.Equal("Ticari", (await vehicles.GetAsync(id))!.Grup); // Ekonomi DEĞİL
    }

    [Fact]
    public async Task Ayardaki_grup_PASIFSE_Ekonomiye_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var settings = scope.ServiceProvider.GetRequiredService<TenantSettingsService>();

        await groups.CreateAsync(Group("EKO", "Ekonomi"));
        var commercialId = await groups.CreateAsync(Group("TIC", "Ticari"));
        await settings.SaveAsync(new TenantSettingsModel { VarsayilanGrupId = commercialId });
        // Ayar dururken grup pasifleştirilirse araçlar görünmez bir gruba yazılmamalı.
        await groups.UpdateAsync(commercialId, Group("TIC", "Ticari", active: false));

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 AAA 004" });

        Assert.Equal("Ekonomi", (await vehicles.GetAsync(id))!.Grup);
    }

    [Fact]
    public async Task Bilincli_Grupsuz_secimi_varsayilana_SNAPLENMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await groups.CreateAsync(Group("EKO", "Ekonomi"));

        // Web formundaki "(Grupsuz)" seçeneği: alan BOŞ ama BİLİNÇLİ. Varsayılan uygulanmaz.
        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 AAA 005", GrupBilincliBos = true });

        Assert.Null((await vehicles.GetAsync(id))!.Grup);
    }

    [Fact]
    public async Task Dolu_grup_degeri_KORUNUR_varsayilan_ezmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await groups.CreateAsync(Group("EKO", "Ekonomi"));

        // Hiçbir gruba eşleşmeyen serbest metin bile olsa kullanıcının girdiği değer korunur
        // (toplu düzeltme yeri Araç Grupları ekranındaki eşleme aracıdır, sessiz ezme DEĞİL).
        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 AAA 006", Grup = "FİAT-EGEA" });

        Assert.Equal("FİAT-EGEA", (await vehicles.GetAsync(id))!.Grup);
    }

    [Fact]
    public async Task Varsayilan_grup_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<VehicleGroupService>().CreateAsync(Group("EKO", "Ekonomi"));

        using var s2 = host.ScopeFor(t2);
        var id = await s2.ServiceProvider.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 AAA 007" });

        // T1'in "Ekonomi" grubu T2'ye SIZMAZ.
        Assert.Null((await s2.ServiceProvider.GetRequiredService<VehicleService>().GetAsync(id))!.Grup);
    }
}
