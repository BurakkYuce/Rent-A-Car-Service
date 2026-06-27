using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

[Collection("postgres")]
public sealed class ListSearchTests(PostgresFixture fx)
{
    private static async Task SeedVehiclesAsync(IServiceScope scope, params (string Plaka, string? Marka, string? Grup, string? Sube, VehicleStatus Durum)[] vs)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        foreach (var (plaka, marka, grup, sube, durum) in vs)
            db.Vehicles.Add(new Vehicle { Plaka = plaka, Marka = marka, Grup = grup, Sube = sube, Durum = durum });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Vehicle_search_filters_by_query_durum_grup()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedVehiclesAsync(scope,
            ("34BMW01", "BMW", "A", null, VehicleStatus.Musait),
            ("34BMW02", "BMW", "B", null, VehicleStatus.Kirada),
            ("06AUD01", "Audi", "A", null, VehicleStatus.Musait));
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        // Marka araması (case-insensitive).
        var bmw = await svc.SearchAsync(new VehicleFilter { Query = "bmw" });
        Assert.Equal(2, bmw.Total);

        // Plaka + durum filtresi.
        var musaitBmw = await svc.SearchAsync(new VehicleFilter { Query = "34BMW", Durum = VehicleStatus.Musait });
        Assert.Equal(1, musaitBmw.Total);
        Assert.Equal("34BMW01", musaitBmw.Items[0].Plaka);

        // Grup filtresi.
        var grupA = await svc.SearchAsync(new VehicleFilter { Grup = "A" });
        Assert.Equal(2, grupA.Total);
    }

    [Fact]
    public async Task Vehicle_search_paginates()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var seed = Enumerable.Range(1, 25)
            .Select(i => ($"34P{i:D3}", (string?)"X", (string?)"A", (string?)null, VehicleStatus.Musait)).ToArray();
        await SeedVehiclesAsync(scope, seed);
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var p1 = await svc.SearchAsync(new VehicleFilter { PageSize = 10, Page = 1 });
        Assert.Equal(25, p1.Total);
        Assert.Equal(10, p1.Items.Count);
        Assert.Equal(3, p1.TotalPages);
        Assert.True(p1.HasNext);
        Assert.False(p1.HasPrev);

        var p3 = await svc.SearchAsync(new VehicleFilter { PageSize = 10, Page = 3 });
        Assert.Equal(5, p3.Items.Count); // son sayfa
        Assert.False(p3.HasNext);
    }

    [Fact]
    public async Task Vehicle_search_respects_branch_scope()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var seed = host.ScopeFor(tenant))
            await SeedVehiclesAsync(seed,
                ("34MRK01", "X", "A", "Merkez", VehicleStatus.Musait),
                ("06ANK01", "X", "A", "Ankara", VehicleStatus.Musait));

        // Operatör Merkez → arama da yalnız kendi şubesi (seçtiği grup geçerli ama şube değişmez).
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        var res = await op.ServiceProvider.GetRequiredService<VehicleService>()
            .SearchAsync(new VehicleFilter { Query = "X" });
        Assert.Equal(1, res.Total);
        Assert.Equal("34MRK01", res.Items[0].Plaka);
    }

    [Fact]
    public async Task Customer_search_matches_name_tc_vergi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Customers.Add(new Customer { Tip = CariType.Bireysel, Ad = "Ahmet", Soyad = "Yılmaz", TcKimlik = "10000000146" });
            db.Customers.Add(new Customer { Tip = CariType.Kurumsal, Unvan = "Yılmaz Ltd", VergiNo = "1234567890" });
            db.Customers.Add(new Customer { Tip = CariType.Bireysel, Ad = "Mehmet", Soyad = "Demir" });
            await db.SaveChangesAsync();
        }
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        Assert.Equal(2, (await svc.SearchAsync(new CustomerFilter { Query = "yılmaz" })).Total); // ad + ünvan
        Assert.Equal(1, (await svc.SearchAsync(new CustomerFilter { Query = "10000000146" })).Total); // TC
        Assert.Equal(1, (await svc.SearchAsync(new CustomerFilter { Query = "1234567890" })).Total); // vergi
        Assert.Equal(3, (await svc.SearchAsync(new CustomerFilter { Query = null })).Total); // tümü
    }
}
