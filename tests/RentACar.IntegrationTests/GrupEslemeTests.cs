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
    private static VehicleGroupInput Grup(string kod, string ad, bool aktif = true)
        => new() { Kod = kod, Ad = ad, Aktif = aktif };

    private static async Task<Guid> AracAsync(VehicleService svc, string plaka, string? grup)
        => await svc.CreateAsync(new VehicleInput { Plaka = plaka, Grup = grup, GrupBilincliBos = grup is null });

    [Fact]
    public async Task Ata_eslesen_araclari_hedef_gruba_tasir_TURKCE_duyarli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        await AracAsync(araclar, "34 EGE 001", "FİAT-EGEA");
        await AracAsync(araclar, "34 EGE 002", "fiat-egea");   // Türkçe İ/i farkı — AYNI değer sayılmalı
        await AracAsync(araclar, "34 OTH 003", "OPEL-CORSA");  // kapsam dışı

        var n = await gruplar.GrupDegeriAtaAsync("FİAT-EGEA", bosOlanlar: false, ekoId);

        Assert.Equal(2, n); // senaryoda 2 Egea var
        var filo = await araclar.ListAsync();
        Assert.Equal(2, filo.Count(v => v.Grup == "Ekonomi"));
        Assert.Equal("OPEL-CORSA", filo.Single(v => v.Plaka == "34OTH003").Grup); // dokunulmadı
    }

    [Fact]
    public async Task Ata_bos_bayragiyla_grupsuz_araclari_toplar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        await AracAsync(araclar, "34 BOS 001", null);
        await AracAsync(araclar, "34 BOS 002", null);
        // Gerçekten "(boş)" YAZAN bir değer: string sentinel kullanılsaydı bu da taşınırdı.
        await AracAsync(araclar, "34 LIT 003", "(boş)");

        var n = await gruplar.GrupDegeriAtaAsync(kaynakDeger: null, bosOlanlar: true, ekoId);

        Assert.Equal(2, n);
        var filo = await araclar.ListAsync();
        Assert.Equal("(boş)", filo.Single(v => v.Plaka == "34LIT003").Grup); // literal değer korundu
    }

    [Fact]
    public async Task Ata_PASIF_gruba_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var pasifId = await gruplar.CreateAsync(Grup("ESK", "Eski", aktif: false));
        await AracAsync(araclar, "34 XXX 001", "FİAT-EGEA");

        // Pasif grup vitrinde okunmaz → atama araçları sessizce görünmez yapardı.
        await Assert.ThrowsAsync<ValidationException>(
            () => gruplar.GrupDegeriAtaAsync("FİAT-EGEA", false, pasifId));

        Assert.Equal("FİAT-EGEA", (await araclar.ListAsync()).Single().Grup); // hiçbir şey taşınmadı
    }

    [Fact]
    public async Task Ata_denetim_izi_birakir_UpdatedAt_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        var aracId = await AracAsync(araclar, "34 AUD 001", "FİAT-EGEA");
        Assert.Null((await araclar.GetAsync(aracId))!.UpdatedAtUtc); // başlangıçta yok

        await gruplar.GrupDegeriAtaAsync("FİAT-EGEA", false, ekoId);

        // ExecuteUpdateAsync kullanılsaydı bu alan (ve audit) sessizce yazılmazdı.
        Assert.NotNull((await araclar.GetAsync(aracId))!.UpdatedAtUtc);
    }

    [Fact]
    public async Task Grup_adi_degisince_araclar_AYNI_islemde_tasinir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var ekoId = await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        await AracAsync(araclar, "34 REN 001", "Ekonomi");
        await AracAsync(araclar, "34 REN 002", "EKONOMİ"); // Türkçe varyant da taşınmalı
        await AracAsync(araclar, "34 REN 003", "SUV");     // kapsam dışı

        await gruplar.UpdateAsync(ekoId, Grup("EKO", "Ekonomi Sınıfı"));

        // REGRESYON KİLİDİ: cascade olmasaydı bu araçlar hiçbir gruba eşleşmez, vitrin/arama boşalırdı.
        var filo = await araclar.ListAsync();
        Assert.Equal(2, filo.Count(v => v.Grup == "Ekonomi Sınıfı"));
        Assert.Equal("SUV", filo.Single(v => v.Plaka == "34REN003").Grup);
    }

    [Fact]
    public async Task Ayni_ad_TURKCE_duyarsiz_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await gruplar.CreateAsync(Grup("EKO", "EKONOMİ"));

        // Kodlar farklı ama Ad aynı → iki grubun filosu tek isim havuzunda birleşirdi.
        await Assert.ThrowsAsync<ValidationException>(
            () => gruplar.CreateAsync(Grup("EKO2", "ekonomi")));
    }

    [Fact]
    public async Task Rename_ile_mevcut_adin_UZERINE_gelmek_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        var suvId = await gruplar.CreateAsync(Grup("SUV", "SUV"));

        await Assert.ThrowsAsync<ValidationException>(
            () => gruplar.UpdateAsync(suvId, Grup("SUV", "Ekonomi")));
    }

    [Fact]
    public async Task Kendi_adiyla_guncelleme_serbesttir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();

        var id = await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        // excludeId çalışmazsa grup kendi adıyla çakışır ve hiçbir alan güncellenemezdi.
        await gruplar.UpdateAsync(id, new VehicleGroupInput { Kod = "EKO", Ad = "Ekonomi", Aciklama = "not", Aktif = true });

        Assert.Equal("not", (await gruplar.GetAsync(id))!.Aciklama);
    }

    [Fact]
    public async Task Ata_yetki_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid ekoId;
        using (var admin = host.ScopeFor(tenant))
            ekoId = await admin.ServiceProvider.GetRequiredService<VehicleGroupService>().CreateAsync(Grup("EKO", "Ekonomi"));

        using var muhasebe = host.ScopeFor(tenant, role: UserRole.Muhasebe); // OperationsWrite YOK
        await Assert.ThrowsAsync<ValidationException>(
            () => muhasebe.ServiceProvider.GetRequiredService<VehicleGroupService>()
                .GrupDegeriAtaAsync("FİAT-EGEA", false, ekoId));
    }

    [Fact]
    public async Task Ata_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using var s1 = host.ScopeFor(t1);
        var ekoId = await s1.ServiceProvider.GetRequiredService<VehicleGroupService>().CreateAsync(Grup("EKO", "Ekonomi"));
        await AracAsync(s1.ServiceProvider.GetRequiredService<VehicleService>(), "34 ISO 001", "FİAT-EGEA");

        using (var s2 = host.ScopeFor(t2))
            await AracAsync(s2.ServiceProvider.GetRequiredService<VehicleService>(), "34 ISO 002", "FİAT-EGEA");

        var n = await s1.ServiceProvider.GetRequiredService<VehicleGroupService>()
            .GrupDegeriAtaAsync("FİAT-EGEA", false, ekoId);

        Assert.Equal(1, n); // T2'nin aracı SAYILMADI
        using var s2Kontrol = host.ScopeFor(t2);
        Assert.Equal("FİAT-EGEA",
            (await s2Kontrol.ServiceProvider.GetRequiredService<VehicleService>().ListAsync()).Single().Grup);
    }

    [Fact]
    public async Task Tanilama_listesi_bos_gruplu_araclari_da_gosterir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var gruplar = scope.ServiceProvider.GetRequiredService<VehicleGroupService>();
        var araclar = scope.ServiceProvider.GetRequiredService<VehicleService>();

        await gruplar.CreateAsync(Grup("EKO", "Ekonomi"));
        await AracAsync(araclar, "34 TAN 001", "FİAT-EGEA");
        await AracAsync(araclar, "34 TAN 002", null);
        await AracAsync(araclar, "34 TAN 003", "Ekonomi"); // eşleşiyor → listede OLMAMALI

        var liste = await gruplar.ListUnmatchedGrupValuesAsync();

        Assert.Equal(1, liste.Single(x => x.Grup == "FİAT-EGEA").AracSayisi);
        var bos = liste.Single(x => x.Bos);
        Assert.Equal(1, bos.AracSayisi);
        Assert.DoesNotContain(liste, x => x.Grup == "Ekonomi");
    }
}
