using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Availability;
using RentACar.Application.Baflar;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 5-C3 — liste yüzeyleri FK-farkındalı kapsama geçti (tek kural: BranchScope.InScope —
/// C5 son hali: iki FK dolu → FK tek başına; aksi halde metin-eşit). DEĞER-KANITI: şube YENİDEN
/// ADLANDIRILINCA metin-filtre operatörü
/// kilitlerdi (0 satır); FK dalı doğru seti verir. Eşlenmemiş-metin şube (Branch master'da yok)
/// salt-metin yoluyla AYNEN çalışır (kilitlenme-önleyici). UI şube filtresi kapsamdan BAĞIMSIZ.
/// </summary>
[Collection("postgres")]
public sealed class BranchScopeFkListTests(PostgresFixture fx)
{
    [Fact]
    public async Task Sube_yeniden_adlandirilinca_fk_operatoru_kurtarir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid subeId;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            subeId = await sp.GetRequiredService<BranchService>()
                .CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
            var veh = sp.GetRequiredService<VehicleService>();
            await veh.CreateAsync(new VehicleInput { Plaka = "34 CF 01", Sube = "Merkez" }); // interceptor→FK
            await veh.CreateAsync(new VehicleInput { Plaka = "34 CF 02", Sube = "Merkez" });
            await veh.CreateAsync(new VehicleInput { Plaka = "06 CF 03", Sube = "Ankara" }); // kapsam dışı
            // ŞUBE YENİDEN ADLANDIRILIR — operatörün claim metni ("Merkez") artık uyuşmaz.
            await sp.GetRequiredService<BranchService>()
                .UpdateAsync(subeId, new BranchInput { Kod = "MRK", Ad = "Merkez Ofis" });
        }

        // Operatör eski oturum metni + FK claim'iyle: metin-only 0 verirdi; FK dalı 2 aracı verir.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: subeId);
        var svc = op.ServiceProvider.GetRequiredService<VehicleService>();

        var liste = await svc.ListAsync();
        Assert.Equal(2, liste.Count);                             // FK kurtardı (önce 0 olurdu)
        Assert.All(liste, v => Assert.Equal(subeId, v.SubeId));

        var arama = await svc.SearchAsync(new VehicleFilter());
        Assert.Equal(2, arama.Items.Count);                       // SQL şablonu da aynı kural

        // Tekil guard: kapsam-içi araç FK'yla açılır; çapraz-şube araç RED.
        var merkezArac = liste[0].Id;
        Assert.NotNull(await svc.GetAsync(merkezArac));
        var ankara = (await host.ScopeFor(tenant).ServiceProvider
            .GetRequiredService<VehicleService>().ListAsync()).Single(v => v.Sube == "Ankara").Id;
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.GetAsync(ankara));
    }

    [Fact]
    public async Task Eslenmemis_metin_subesi_aynen_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
        {
            var veh = seed.ServiceProvider.GetRequiredService<VehicleService>();
            await veh.CreateAsync(new VehicleInput { Plaka = "34 DP 01", Sube = "Depo" }); // Branch master'da YOK
            await veh.CreateAsync(new VehicleInput { Plaka = "34 DP 02", Sube = "Merkez" });
        }

        // Claim'siz-FK operatör (eski oturum): salt-metin yolu — kilitlenme yok.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Depo");
        var liste = await op.ServiceProvider.GetRequiredService<VehicleService>().ListAsync();
        var tek = Assert.Single(liste);
        Assert.Equal("Depo", tek.Sube);
        Assert.Null(tek.SubeId);                                   // FK çözülmemiş — metin dalı taşıdı
    }

    [Fact]
    public async Task Gider_ve_baf_yuzeyleri_kapsamli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid subeId;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            subeId = await sp.GetRequiredService<BranchService>()
                .CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
            var exp = sp.GetRequiredService<ExpenseService>();
            await exp.CreateAsync(new ExpenseInput
            { Tip = ExpenseType.Genel, NetTutar = 10m, KdvOrani = 0m, Sube = "Merkez", OdemeYontemi = PaymentMethod.Nakit });
            await exp.CreateAsync(new ExpenseInput
            { Tip = ExpenseType.Genel, NetTutar = 20m, KdvOrani = 0m, Sube = "Ankara", OdemeYontemi = PaymentMethod.Nakit });
            // Şube rename → gider FK'sı (varsa) kurtarır; Expense.SubeId interceptor'la doldu.
            await sp.GetRequiredService<BranchService>()
                .UpdateAsync(subeId, new BranchInput { Kod = "MRK", Ad = "Merkez Ofis" });
        }

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: subeId);
        var giderler = await op.ServiceProvider.GetRequiredService<ExpenseService>().ListAsync();
        var g = Assert.Single(giderler);                           // rename'e rağmen FK dalı buldu
        Assert.Equal(10m, g.NetTutar);

        // Baf FK'sız (SubeId kolonu yok) → metin dalı: rename SONRASI metin uyuşmaz → boş (bilinen sınır,
        // C3 kapsam notu — Baf FK'lanana dek rename Baf listesini etkiler; metin-claim güncellenince düzelir).
        var baflar = await op.ServiceProvider.GetRequiredService<BafService>().ListAsync();
        Assert.Empty(baflar);
    }

    [Fact]
    public async Task Rename_cakismasi_sizintisi_c5_ile_kapali()
    {
        // C5 değer-kanıtı (dokümante F2 kapanışı): B1 eski adını B2 devralınca ("Merkez"), o adla doğan
        // B2 kaydı metin-eşleşmeyle B1 operatörüne SIZARDI (C2-C4 bilinçli genişletmesi). C5: iki FK de
        // doluyken FK TEK BAŞINA karar verir → sızıntı biter; FK'sız kayıtta metin yolu aynen kalır.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid b1, sizanArac;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            var branches = sp.GetRequiredService<BranchService>();
            b1 = await branches.CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
            var b2 = await branches.CreateAsync(new BranchInput { Kod = "ANK", Ad = "Ankara" });
            var veh = sp.GetRequiredService<VehicleService>();
            await veh.CreateAsync(new VehicleInput { Plaka = "34 RC 01", Sube = "Merkez" });  // FK=B1
            // Çakışma: B1 adını bırakır, B2 devralır; ardından o adla B2'ye kayıt doğar (FK=B2, metin "Merkez").
            await branches.UpdateAsync(b1, new BranchInput { Kod = "MRK", Ad = "Eski Merkez" });
            await branches.UpdateAsync(b2, new BranchInput { Kod = "ANK", Ad = "Merkez" });
            sizanArac = await veh.CreateAsync(new VehicleInput { Plaka = "06 RC 02", Sube = "Merkez" });
        }

        // Eski oturumlu B1 operatörü (claim metni hâlâ "Merkez", FK=B1).
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: b1);
        var svc = op.ServiceProvider.GetRequiredService<VehicleService>();

        var liste = await svc.ListAsync();
        var tek = Assert.Single(liste);                            // C5 öncesi 2 dönerdi (B2 aracı sızardı)
        Assert.Equal("34RC01", tek.Plaka);
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.GetAsync(sizanArac)); // tekil guard da RED
    }

    [Fact]
    public async Task Musaitlik_kapsami_ui_filtresinden_bagimsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid subeId;
        using (var seed = host.ScopeFor(tenant))
        {
            var sp = seed.ServiceProvider;
            subeId = await sp.GetRequiredService<BranchService>()
                .CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });
            var veh = sp.GetRequiredService<VehicleService>();
            await veh.CreateAsync(new VehicleInput { Plaka = "34 MS 01", Sube = "Merkez" });
            await veh.CreateAsync(new VehicleInput { Plaka = "06 MS 02", Sube = "Ankara" });
        }
        var from = DateTimeOffset.UtcNow.AddDays(1);

        // Operatör: kapsam FK'lı → yalnız Merkez; UI sube parametresi verilmese bile.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator,
            assignedBranch: "Merkez", assignedBranchId: subeId);
        var musait = await op.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(from, from.AddDays(2), group: null, branch: null);
        Assert.Single(musait);
        Assert.Equal("34MS01", musait[0].Plaka);

        // Admin: kapsam yok; UI sube filtresi ek daraltma olarak çalışır.
        using var admin = host.ScopeFor(tenant);
        var adminHepsi = await admin.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(from, from.AddDays(2), group: null, branch: null);
        Assert.Equal(2, adminHepsi.Count);
        var adminAnkara = await admin.ServiceProvider.GetRequiredService<AvailabilityService>()
            .FindAvailableAsync(from, from.AddDays(2), group: null, branch: "Ankara");
        Assert.Single(adminAnkara);
    }
}
