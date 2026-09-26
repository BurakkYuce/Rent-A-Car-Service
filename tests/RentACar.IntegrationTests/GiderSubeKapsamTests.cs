using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Branches;
using RentACar.Application.Expenses;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Şube tutarlılık: gider listesi artık şube-kapsamlı (mevcut yetki boşluğu kapandı — ExpenseService.ListAsync
/// BranchScope uygulamıyordu). Operatör yalnız kendi şubesinin giderlerini görür; Admin/şubesiz operatör tümünü.
/// Ayrıca gider create'i Sube'yi yazar ve interceptor SubeId FK'sini çözer. Diğer scoped entity'lerle birebir desen.
/// </summary>
[Collection("postgres")]
public sealed class GiderSubeKapsamTests(PostgresFixture fx)
{
    private static Task<System.Guid> ExpenseAsync(System.IServiceProvider sp, string? branch)
        => sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0.20m, OdemeYontemi = PaymentMethod.Nakit, Sube = branch });

    private static Task<IReadOnlyList<RentACar.Domain.Entities.Expense>> ListAsync(System.IServiceProvider sp)
        => sp.GetRequiredService<ExpenseService>().ListAsync();

    [Fact]
    public async Task Operator_yalniz_kendi_sube_giderini_gorur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = System.Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant)) // Admin — 3 gider (Merkez/Ankara/şubesiz)
        {
            await ExpenseAsync(seed.ServiceProvider, "Merkez");
            await ExpenseAsync(seed.ServiceProvider, "Ankara");
            await ExpenseAsync(seed.ServiceProvider, null);
        }

        using (var op = host.ScopeFor(tenant, System.Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var list = await ListAsync(op.ServiceProvider);
            Assert.Single(list);                          // yalnız Merkez
            Assert.All(list, e => Assert.Equal("Merkez", e.Sube));
        }

        using (var admin = host.ScopeFor(tenant))         // Admin → tümü
            Assert.Equal(3, (await ListAsync(admin.ServiceProvider)).Count);

        using (var op0 = host.ScopeFor(tenant, System.Guid.NewGuid(), "op0", UserRole.Operator, assignedBranch: null))
            Assert.Equal(3, (await ListAsync(op0.ServiceProvider)).Count); // şubesiz operatör → tümü
    }

    [Fact]
    public async Task Gider_sube_yazilir_ve_FK_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(System.Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<BranchService>().CreateAsync(new BranchInput { Kod = "MRK", Ad = "Merkez" });

        var id = await ExpenseAsync(sp, "Merkez");
        var e = await sp.GetRequiredService<ExpenseService>().GetAsync(id);
        Assert.Equal("Merkez", e!.Sube);
        Assert.NotNull(e.SubeId); // BranchFkInterceptor metin → SubeId çözdü
    }
}
