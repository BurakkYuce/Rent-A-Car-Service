using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-10 — grup eşleme aracı + rename cascade + Ad benzersizliği.
///
/// Neden bir arada: üçü de <c>Vehicle.Grup</c> ile <c>VehicleGroup.Ad</c> arasındaki STRING eşleşmesini
/// korur. Ad taşıyıcı kolondur (vitrin/arama onunla eşler) → adı değiştiren ama araçları taşımayan bir
/// güncelleme, tüm filoyu sessizce vitrin-dışı bırakır. Bu dosya o regresyonun kalıcı kilididir.
/// Beklenen değerler senaryodan kurulur (kaç araç yazıldıysa o), servis dönüşünden türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class GrupEslemeTests(PostgresFixture fx)
{
    private static VehicleGroupInput Group(string code, string name, bool active = true)
        => new() { Kod = code, Ad = name, Aktif = active };

    private static async Task<Guid> VehicleAsync(VehicleService svc, string plate, string? group)
        => await svc.CreateAsync(new VehicleInput { Plaka = plate, Grup = group, GrupBilincliBos = group is null });

    [Fact]
    public async Task Ata_eslesen_araclari_hedef_gruba_tasir_TURKCE_duyarli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await groups.CreateAsync(Group("EKO", "Ekonomi"));
        await VehicleAsync(vehicles, "34 EGE 001", "FİAT-EGEA");
        await VehicleAsync(vehicles, "34 EGE 002", "fiat-egea");   // Türkçe İ/i farkı — AYNI değer sayılmalı
        await VehicleAsync(vehicles, "34 OTH 003", "OPEL-CORSA");  // kapsam dışı

        var n = await groups.AssignGroupValueAsync("FİAT-EGEA", emptyOnes: false, ekoId);

        Assert.Equal(2, n); // senaryoda 2 Egea var
        var fleet = await vehicles.ListAsync();
        Assert.Equal(2, fleet.Count(v => v.Grup == "Ekonomi"));
        Assert.Equal("OPEL-CORSA", fleet.Single(v => v.Plaka == "34OTH003").Grup); // dokunulmadı
    }

    [Fact]
    public async Task Ata_bos_bayragiyla_grupsuz_araclari_toplar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await groups.CreateAsync(Group("EKO", "Ekonomi"));
        await VehicleAsync(vehicles, "34 BOS 001", null);
        await VehicleAsync(vehicles, "34 BOS 002", null);
        // Gerçekten "(boş)" YAZAN bir değer: string sentinel kullanılsaydı bu da taşınırdı.
        await VehicleAsync(vehicles, "34 LIT 003", "(boş)");

        var n = await groups.AssignGroupValueAsync(sourceValue: null, emptyOnes: true, ekoId);

        Assert.Equal(2, n);
        var fleet = await vehicles.ListAsync();
        Assert.Equal("(boş)", fleet.Single(v => v.Plaka == "34LIT003").Grup); // literal değer korundu
    }

    [Fact]
    public async Task Ata_PASIF_gruba_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var inactiveId = await groups.CreateAsync(Group("ESK", "Eski", active: false));
        await VehicleAsync(vehicles, "34 XXX 001", "FİAT-EGEA");

        // Pasif grup vitrinde okunmaz → atama araçları sessizce görünmez yapardı.
        await Assert.ThrowsAsync<ValidationException>(
            () => groups.AssignGroupValueAsync("FİAT-EGEA", false, inactiveId));

        Assert.Equal("FİAT-EGEA", (await vehicles.ListAsync()).Single().Grup); // hiçbir şey taşınmadı
    }

    [Fact]
    public async Task Ata_denetim_izi_birakir_UpdatedAt_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await groups.CreateAsync(Group("EKO", "Ekonomi"));
        var vehicleId = await VehicleAsync(vehicles, "34 AUD 001", "FİAT-EGEA");
        Assert.Null((await vehicles.GetAsync(vehicleId))!.UpdatedAtUtc); // başlangıçta yok

        await groups.AssignGroupValueAsync("FİAT-EGEA", false, ekoId);

        // ExecuteUpdateAsync kullanılsaydı bu alan (ve audit) sessizce yazılmazdı.
        Assert.NotNull((await vehicles.GetAsync(vehicleId))!.UpdatedAtUtc);
    }

    [Fact]
    public async Task Grup_adi_degisince_araclar_AYNI_islemde_tasinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await groups.CreateAsync(Group("EKO", "Ekonomi"));
        await VehicleAsync(vehicles, "34 REN 001", "Ekonomi");
        await VehicleAsync(vehicles, "34 REN 002", "EKONOMİ"); // Türkçe varyant da taşınmalı
        await VehicleAsync(vehicles, "34 REN 003", "SUV");     // kapsam dışı

        await groups.UpdateAsync(ekoId, Group("EKO", "Ekonomi Sınıfı"));

        // REGRESYON KİLİDİ: cascade olmasaydı bu araçlar hiçbir gruba eşleşmez, vitrin/arama boşalırdı.
        var fleet = await vehicles.ListAsync();
        Assert.Equal(2, fleet.Count(v => v.Grup == "Ekonomi Sınıfı"));
        Assert.Equal("SUV", fleet.Single(v => v.Plaka == "34REN003").Grup);
    }

    [Fact]
    public async Task Ayni_ad_TURKCE_duyarsiz_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await groups.CreateAsync(Group("EKO", "EKONOMİ"));

        // Kodlar farklı ama Ad aynı → iki grubun filosu tek isim havuzunda birleşirdi.
        await Assert.ThrowsAsync<ValidationException>(
            () => groups.CreateAsync(Group("EKO2", "ekonomi")));
    }

    [Fact]
    public async Task Rename_ile_mevcut_adin_UZERINE_gelmek_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await groups.CreateAsync(Group("EKO", "Ekonomi"));
        var suvId = await groups.CreateAsync(Group("SUV", "SUV"));

        await Assert.ThrowsAsync<ValidationException>(
            () => groups.UpdateAsync(suvId, Group("SUV", "Ekonomi")));
    }

    [Fact]
    public async Task Kendi_adiyla_guncelleme_serbesttir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await groups.CreateAsync(Group("EKO", "Ekonomi"));
        // excludeId çalışmazsa grup kendi adıyla çakışır ve hiçbir alan güncellenemezdi.
        await groups.UpdateAsync(id, new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomi", Aciklama = "not", Aktif = true });

        Assert.Equal("not", (await groups.GetAsync(id))!.Aciklama);
    }

    [Fact]
    public async Task Ata_yetki_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid ekoId;
        using (var admin = host.ScopeFor(tenant))
            ekoId = await admin.ServiceProvider.GetRequiredService<VehicleGroupService>().CreateAsync(Group("EKO", "Ekonomi"));

        using var accounting = host.ScopeFor(tenant, role: UserRole.Muhasebe); // OperationsWrite YOK
        await Assert.ThrowsAsync<NoPermissionException>(
            () => accounting.ServiceProvider.GetRequiredService<VehicleGroupService>()
                .AssignGroupValueAsync("FİAT-EGEA", false, ekoId));
    }

    [Fact]
    public async Task Ata_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using var s1 = host.ScopeFor(t1);
        var ekoId = await s1.ServiceProvider.GetRequiredService<VehicleGroupService>().CreateAsync(Group("EKO", "Ekonomi"));
        await VehicleAsync(s1.ServiceProvider.GetRequiredService<VehicleService>(), "34 ISO 001", "FİAT-EGEA");

        using (var s2 = host.ScopeFor(t2))
            await VehicleAsync(s2.ServiceProvider.GetRequiredService<VehicleService>(), "34 ISO 002", "FİAT-EGEA");

        var n = await s1.ServiceProvider.GetRequiredService<VehicleGroupService>()
            .AssignGroupValueAsync("FİAT-EGEA", false, ekoId);

        Assert.Equal(1, n); // T2'nin aracı SAYILMADI
        using var s2Check = host.ScopeFor(t2);
        Assert.Equal("FİAT-EGEA",
            (await s2Check.ServiceProvider.GetRequiredService<VehicleService>().ListAsync()).Single().Grup);
    }

    [Fact]
    public async Task Tanilama_listesi_bos_gruplu_araclari_da_gosterir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var groups = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await groups.CreateAsync(Group("EKO", "Ekonomi"));
        await VehicleAsync(vehicles, "34 TAN 001", "FİAT-EGEA");
        await VehicleAsync(vehicles, "34 TAN 002", null);
        await VehicleAsync(vehicles, "34 TAN 003", "Ekonomi"); // eşleşiyor → listede OLMAMALI

        var list = await groups.ListUnmatchedGroupValuesAsync();

        Assert.Equal(1, list.Single(x => x.Grup == "FİAT-EGEA").AracSayisi);
        var empty = list.Single(x => x.Bos);
        Assert.Equal(1, empty.AracSayisi);
        Assert.DoesNotContain(list, x => x.Grup == "Ekonomi");
    }
}
