using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G2 — manuel/serbest fatura (PARA) + araç satış derinlik. BAĞIMSIZ ORACLE: manuel fatura DENGELİ
/// defter (Borç Cari brüt / Alacak Gelir net + KDV), cari +1200/gelir 1000/kdv 200; dönem-kilidi; idempotency;
/// VehicleSale additive roundtrip.
/// </summary>
[Collection("postgres")]
public sealed class InvoiceManualTests(PostgresFixture fx)
{
    private static async Task<Guid> Cari(IServiceProvider sp)
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Manuel Cari" });

    [Fact]
    public async Task Manual_invoice_posts_balanced_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await Cari(sp);

        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = cariId, NetTutar = 1000m, KdvOrani = 0.20m, Aciklama = "Danışmanlık" });

        // Oracle: net 1000 + kdv 200 = brüt 1200; cari BORÇLANIR (+1200); gelir 1000; kdv tahsil 200.
        Assert.Equal(1200m, await sp.GetRequiredService<CashService>().GetCariBalanceAsync(cariId));
        var gg = await sp.GetRequiredService<ReportService>().GetGelirGiderAsync();
        Assert.Equal(1000m, gg.GelirToplam);
        Assert.Equal(200m, gg.KdvTahsil);
    }

    [Fact]
    public async Task Manual_invoice_blocked_by_period_lock()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await Cari(sp);
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 500m }));
    }

    [Fact]
    public async Task Manual_invoice_idempotent_with_key()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cariId = await Cari(sp);
        var key = Guid.NewGuid();
        var svc = sp.GetRequiredService<InvoiceService>();

        var id1 = await svc.CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = key });
        var id2 = await svc.CreateManualAsync(new ManualInvoiceInput { CariId = cariId, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = key });

        Assert.Equal(id1, id2);                 // çift-submit aynı fatura
        Assert.Equal(1200m, await sp.GetRequiredService<CashService>().GetCariBalanceAsync(cariId)); // tek borç (çiftlenmedi)
    }

    [Fact]
    public async Task VehicleSale_depth_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 G2 01" });
        var sales = sp.GetRequiredService<VehicleSaleService>();

        var id = await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = vId, AliciCariId = await Cari(sp), SatisNet = 5000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m,
            HedefFiyat = 5500m, SatisKm = 120000, SatisKanali = "Galeri", Devir = "Noter"
        });
        var s = await sales.GetAsync(id);
        Assert.Equal(5500m, s!.HedefFiyat);
        Assert.Equal(120000, s.SatisKm);
        Assert.Equal("Galeri", s.SatisKanali);
        Assert.Equal("Noter", s.Devir);
    }
}
