using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap J4 — servis yansıtma/rücu→cari/defter (PARA). BAĞIMSIZ ORACLE: maliyet 1000 × kusur 0.5 = 500 →
/// Borç Cari 500 (cari borçlanır) / Alacak Gelir 500; kayıt Yansitildi + YansitilanTutar=500; çift-yansıtma red.
/// </summary>
[Collection("postgres")]
public sealed class ServisYansitmaTests(PostgresFixture fx)
{
    private static async Task<(IServiceProvider sp, Guid svcId, Guid cari)> Seed(
        IServiceScope scope, string plaka, decimal maliyet = 1000m, decimal kusur = 0.5m,
        HasarSorumlu sorumlu = HasarSorumlu.Musteri)
    {
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        var cari = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rücu Cari" });
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = v, Tip = ServisTipi.Ariza, GirisKm = 0, HasarSorumlu = sorumlu, KusurOrani = kusur,
            Lines = [new ServiceLineInput { Aciklama = "Tampon", Tutar = maliyet }]
        });
        await svc.BaslatAsync(id);
        await svc.TamamlaAsync(id, cikisKm: 100);
        return (sp, id, cari);
    }

    [Fact]
    public async Task Yansit_cari_borclanir_gelir_artar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, svcId, cari) = await Seed(scope, "34 SV 01");
        var svc = sp.GetRequiredService<ServiceRecordService>();

        await svc.YansitAsync(svcId, cari);

        Assert.Equal(500m, await sp.GetRequiredService<CashService>().GetCariBalanceAsync(cari)); // 1000 × 0.5
        var gg = await sp.GetRequiredService<ReportService>().GetGelirGiderAsync();
        Assert.Equal(500m, gg.GelirToplam);
        Assert.Equal(0m, gg.GiderToplam);

        var rec = (await svc.ListAsync()).Single(r => r.Id == svcId);
        Assert.True(rec.Yansitildi);
        Assert.Equal(500m, rec.YansitilanTutar);

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() => svc.YansitAsync(svcId, cari));
    }

    [Fact]
    public async Task Sirket_kusurunda_yansitilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, svcId, cari) = await Seed(scope, "34 SV 02", sorumlu: HasarSorumlu.Sirket);
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<ServiceRecordService>().YansitAsync(svcId, cari));
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, svcId, cari) = await Seed(scope, "34 SV 03");
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<ServiceRecordService>().YansitAsync(svcId, cari));
    }
}
