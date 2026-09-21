using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Baflar;
using RentACar.Application.Common;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Pre-launch adversarial (liste-scope taraması, Expense-sınıfı kaçak) — Baf (personel araç tahsis) şube-kapsamı:
/// operatör YALNIZ kendi şubesinin tahsislerini listeler/okur (önceden ListAsync BranchScope'u yok sayıyordu →
/// tüm şubeleri görüyordu). Bağımsız oracle: Merkez+Ankara tahsisi → operatör(Merkez) yalnız 1, Admin 2.
/// </summary>
[Collection("postgres")]
public sealed class BafSubeKapsamTests(PostgresFixture fx)
{
    private static BafInput Bir(string sube) => new()
    { PersonelId = Guid.NewGuid(), VehicleId = Guid.NewGuid(), CikisKm = 100, Sube = sube };

    [Fact]
    public async Task Operator_yalniz_kendi_sube_tahsislerini_gorur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid ankara;
        using (var seed = host.ScopeFor(tenant)) // Admin
        {
            var svc = seed.ServiceProvider.GetRequiredService<BafService>();
            await svc.CreateAsync(Bir("Merkez"));
            ankara = await svc.CreateAsync(Bir("Ankara"));
        }

        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var svc = op.ServiceProvider.GetRequiredService<BafService>();
            var list = await svc.ListAsync();
            Assert.Single(list);                                   // yalnız Merkez tahsisi
            Assert.All(list, b => Assert.Equal("Merkez", b.Sube));
            Assert.Contains("kapsamınız dışında",                   // başka şube tahsisi ID ile okunamaz
                (await Assert.ThrowsAsync<YetkiYokException>(() => svc.GetAsync(ankara))).Message);
        }

        using (var admin = host.ScopeFor(tenant))
            Assert.Equal(2, (await admin.ServiceProvider.GetRequiredService<BafService>().ListAsync()).Count); // Admin tümü
    }
}
