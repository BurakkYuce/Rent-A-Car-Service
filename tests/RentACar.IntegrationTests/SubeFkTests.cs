using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap F1 — Şube FK forward-resolution. BAĞIMSIZ ORACLE: serbest-metin şube, AYNI tenant içindeki
/// Branch.Ad'a (case-insensitive) çözülüp SubeId set edilir; eşleşmezse null (metin korunur); çapraz-tenant
/// eşleşme YOK. Backfill (migration) aynı mantığın geçmişe uygulanmışıdır; adversarial ampirik doğrular.
/// </summary>
[Collection("postgres")]
public sealed class SubeFkTests(PostgresFixture fx)
{
    private static async Task<Guid> Branch(IServiceProvider sp, string kod, string ad)
        => await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = kod, Ad = ad, Aktif = true });

    [Fact]
    public async Task Vehicle_resolves_subeid_from_branch_name_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var branchId = await Branch(sp, "MRK", "Merkez");
        var vehicles = sp.GetRequiredService<VehicleService>();

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SF 01", Sube = "merkez" }); // küçük harf
        var v = await vehicles.GetAsync(id);
        Assert.Equal(branchId, v!.SubeId);     // FK çözüldü
        Assert.Equal("merkez", v.Sube);        // metin korundu
    }

    [Fact]
    public async Task Unmatched_sube_leaves_fk_null_text_preserved()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await Branch(sp, "MRK", "Merkez");
        var vehicles = sp.GetRequiredService<VehicleService>();

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SF 02", Sube = "Olmayan Şube" });
        var v = await vehicles.GetAsync(id);
        Assert.Null(v!.SubeId);                 // eşleşme yok → null
        Assert.Equal("Olmayan Şube", v.Sube);   // metin korundu (veri kaybı yok)
    }

    [Fact]
    public async Task Branch_resolution_is_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await Branch(s1.ServiceProvider, "MRK", "Merkez"); // şube yalnız t1'de

        using var s2 = host.ScopeFor(t2);
        var vehicles = s2.ServiceProvider.GetRequiredService<VehicleService>();
        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SF 03", Sube = "Merkez" });
        var v = await vehicles.GetAsync(id);
        Assert.Null(v!.SubeId);  // t2'de "Merkez" şubesi yok → çapraz-tenant eşleşme YOK
    }

    [Fact]
    public async Task Referenced_branch_cannot_be_deleted() // composite FK Restrict (adversarial M1 fix)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var branchId = await Branch(sp, "MRK", "Merkez");
        await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 SF 05", Sube = "Merkez" });

        // Araç bu şubeye FK ile bağlı → silme RED (dostça hata).
        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<BranchService>().DeleteAsync(branchId));
    }

    [Fact]
    public async Task Update_rewires_subeid()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await Branch(sp, "MRK", "Merkez");
        var subeB = await Branch(sp, "SB", "Şube B");
        var vehicles = sp.GetRequiredService<VehicleService>();

        var id = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 SF 04", Sube = "Merkez" });
        await vehicles.UpdateAsync(id, new VehicleInput { Plaka = "34 SF 04", Sube = "Şube B" });
        Assert.Equal(subeB, (await vehicles.GetAsync(id))!.SubeId);
    }
}
