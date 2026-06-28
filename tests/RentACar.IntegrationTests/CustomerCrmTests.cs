using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Cari CRM agrega + filtre — bağımsız oracle. Ali'nin 2 kirası (300 + 200, biri İptal=sayılmaz),
/// Ciro/Adet/SonKira elle hesaplanır; İYS/uyarı/kara-liste/tür filtreleri doğrulanır.
/// </summary>
[Collection("postgres")]
public sealed class CustomerCrmTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> SeedAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var veh = new Vehicle { Plaka = "34CRM01", Durum = VehicleStatus.Musait };
        db.Vehicles.Add(veh);

        // Ali: bireysel, İYS izinli, uyarılı. İki kira: 300 (Tamamlandı, 06-01) + 200 (Kirada, 06-10) → ciro 500, adet 2, son 06-10.
        // + bir İPTAL kira 999 → sayılmamalı.
        var ali = new Customer { Tip = CariType.Bireysel, Ad = "Ali", Soyad = "Veli", IysIzinli = true, Uyari = true };
        // Beta A.Ş.: kurumsal, kara liste, kira yok → adet 0, ciro 0, son null.
        var beta = new Customer { Tip = CariType.Kurumsal, Unvan = "Beta A.Ş.", KaraListe = true };
        db.Customers.Add(ali);
        db.Customers.Add(beta);

        db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-C1", MusteriId = ali.Id, VehicleId = veh.Id,
            Durum = RentalStatus.Tamamlandi, BasTar = D(2026, 6, 1), BitTar = D(2026, 6, 3), GenelToplam = 300m });
        db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-C2", MusteriId = ali.Id, VehicleId = veh.Id,
            Durum = RentalStatus.Kirada, BasTar = D(2026, 6, 10), BitTar = D(2026, 6, 12), GenelToplam = 200m });
        db.Rentals.Add(new RentalContract { SozlesmeNo = "KS-C3", MusteriId = ali.Id, VehicleId = veh.Id,
            Durum = RentalStatus.Iptal, BasTar = D(2026, 6, 20), BitTar = D(2026, 6, 22), GenelToplam = 999m });

        await db.SaveChangesAsync();
        return ali.Id;
    }

    [Fact]
    public async Task Aggregates_count_revenue_lastrental_exclude_cancelled()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var aliId = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        var rows = await svc.SearchRowsAsync(new CustomerFilter());
        var ali = rows.Items.Single(r => r.Id == aliId);
        Assert.Equal(2, ali.KiraAdet);             // İptal hariç
        Assert.Equal(500m, ali.Ciro);              // 300 + 200
        Assert.Equal(D(2026, 6, 10), ali.SonKira); // en geç (İptal 06-20 sayılmaz)

        var beta = rows.Items.Single(r => r.Tip == CariType.Kurumsal);
        Assert.Equal(0, beta.KiraAdet);
        Assert.Equal(0m, beta.Ciro);
        Assert.Null(beta.SonKira);
    }

    [Fact]
    public async Task Filter_by_tip_iys_uyari_kara()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<CustomerService>();

        Assert.Single((await svc.SearchRowsAsync(new CustomerFilter { Tip = CariType.Kurumsal })).Items);
        Assert.Single((await svc.SearchRowsAsync(new CustomerFilter { IysIzinli = true })).Items);
        Assert.Single((await svc.SearchRowsAsync(new CustomerFilter { Uyari = true })).Items);
        var kara = (await svc.SearchRowsAsync(new CustomerFilter { KaraListe = true })).Items;
        Assert.Single(kara);
        Assert.Equal("Beta A.Ş.", kara[0].DisplayName);
        // İYS izinsiz → yalnız Beta.
        Assert.Single((await svc.SearchRowsAsync(new CustomerFilter { IysIzinli = false })).Items);
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid())) await SeedAsync(s1);
        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Empty((await s2.ServiceProvider.GetRequiredService<CustomerService>()
            .SearchRowsAsync(new CustomerFilter())).Items);
    }
}
