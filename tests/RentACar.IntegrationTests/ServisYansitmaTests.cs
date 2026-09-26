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
        IServiceScope scope, string plate, decimal cost = 1000m, decimal fault = 0.5m,
        DamageResponsible responsible = DamageResponsible.Musteri)
    {
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        var account = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Rücu Cari" });
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = v, Tip = ServiceType.Ariza, GirisKm = 0, HasarSorumlu = responsible, KusurOrani = fault,
            Lines = [new ServiceLineInput { Aciklama = "Tampon", Tutar = cost }]
        });
        await svc.StartAsync(id);
        await svc.CompleteAsync(id, pickupKm: 100);
        return (sp, id, cari: account);
    }

    [Fact]
    public async Task Yansit_cari_borclanir_gelir_artar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, svcId, account) = await Seed(scope, "34 SV 01");
        var svc = sp.GetRequiredService<ServiceRecordService>();

        await svc.ReflectAsync(svcId, account);

        Assert.Equal(500m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account)); // 1000 × 0.5
        var gg = await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync();
        Assert.Equal(500m, gg.GelirToplam);
        Assert.Equal(0m, gg.GiderToplam);

        var rec = (await svc.ListAsync()).Single(r => r.Id == svcId);
        Assert.True(rec.Yansitildi);
        Assert.Equal(500m, rec.YansitilanTutar);

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() => svc.ReflectAsync(svcId, account));
    }

    [Fact]
    public async Task Sirket_kusurunda_yansitilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, svcId, account) = await Seed(scope, "34 SV 02", responsible: DamageResponsible.Sirket);
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<ServiceRecordService>().ReflectAsync(svcId, account));
    }

    [Fact]
    public async Task Donem_kilidi_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, svcId, account) = await Seed(scope, "34 SV 03");
        await sp.GetRequiredService<PeriodLockService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<ServiceRecordService>().ReflectAsync(svcId, account));
    }
}
