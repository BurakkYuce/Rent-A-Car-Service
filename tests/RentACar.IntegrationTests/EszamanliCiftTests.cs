using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Denetim O9 + O10b — paralel-çift yarışları (AdversarialCashTests ters-kayıt yarışı deseni).
/// BAĞIMSIZ ORACLE'lar elle: 3g × 100 = 300 TL fatura; satış net 100.000 + %20 KDV = 120.000 brüt;
/// 8 tahsilat → TH-000001..TH-000008 (boşluksuz küme). Kaybeden ValidationException almalı,
/// kazananın parası TEK kez yazılmalı (çift borç YOK).
/// </summary>
[Collection("postgres")]
public sealed class EszamanliCiftTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(bool ok, Exception? ex)> Wrap(Task t)
    {
        try { await t; return (true, null); }
        catch (Exception ex) { return (false, ex); }
    }

    // ---- O9a — aynı kiraya İKİ PARALEL fatura: TAM BİRİ kazanır, cari TEK fatura borçlanır ----
    [Fact]
    public async Task Paralel_cift_fatura_tam_biri_kazanir_cari_tek_borclanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid rentalId, account;
        using (var s0 = host.ScopeFor(tenant))
        {
            var sp = s0.ServiceProvider;
            account = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Yarış Fatura" });
            var veh = await sp.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 YC 01", Durum = VehicleStatus.Musait });
            rentalId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = account, VehicleId = veh, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m });
        }

        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        var t1 = Task.Run(() => s1.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId));
        var t2 = Task.Run(() => s2.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rentalId));
        var results = await Task.WhenAll(Wrap(t1), Wrap(t2));

        Assert.Equal(1, results.Count(r => r.ok));                              // tam BİRİ başarılı
        Assert.Equal(1, results.Count(r => r.ex is ValidationException));       // diğeri temiz red

        using var s3 = host.ScopeFor(tenant);
        var sp3 = s3.ServiceProvider;
        var factory = sp3.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(1, await db.Invoices.AsNoTracking().CountAsync(i => i.RentalId == rentalId)); // TEK fatura

        // Cari borç TEK fatura tutarı: 3 gün × 100 = 300 (çift borç 600 OLMAZ).
        Assert.Equal(300m, await sp3.GetRequiredService<CashService>().GetAccountBalanceAsync(account));
    }

    // ---- O9b — aynı araca İKİ PARALEL satış: tek kazanan, alıcı TEK satış borçlanır ----
    [Fact]
    public async Task Paralel_cift_arac_satisi_tek_kazanan()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid vehId, recipient;
        using (var s0 = host.ScopeFor(tenant))
        {
            var sp = s0.ServiceProvider;
            recipient = await sp.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Yarış Alıcı" });
            vehId = await sp.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 YC 02", Durum = VehicleStatus.Musait });
        }

        VehicleSaleInput Sale() => new()
        { VehicleId = vehId, AliciCariId = recipient, SatisNet = 100000m, KdvOrani = 0.20m };

        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        var t1 = Task.Run(() => s1.ServiceProvider.GetRequiredService<VehicleSaleService>().CreateAsync(Sale()));
        var t2 = Task.Run(() => s2.ServiceProvider.GetRequiredService<VehicleSaleService>().CreateAsync(Sale()));
        var results = await Task.WhenAll(Wrap(t1), Wrap(t2));

        Assert.Equal(1, results.Count(r => r.ok));                        // tam BİRİ kazanır
        Assert.Equal(1, results.Count(r => r.ex is ValidationException)); // diğeri "araç zaten satılmış"

        using var s3 = host.ScopeFor(tenant);
        var sp3 = s3.ServiceProvider;
        var factory = sp3.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.VehicleSales.AsNoTracking().CountAsync(s => s.VehicleId == vehId)); // TEK satış
            var v = await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == vehId);
            Assert.Equal(VehicleStatus.Satildi, v.Durum);
        }

        // Alıcı borcu TEK satış brütü: 100.000 + %20 KDV = 120.000 (çift 240.000 OLMAZ).
        Assert.Equal(120000m, await sp3.GetRequiredService<CashService>().GetAccountBalanceAsync(recipient));
    }

    // ---- O10b — SequenceAllocator eşzamanlılık: 8 paralel tahsilat → No'lar BENZERSİZ ve boşluksuz ----
    [Fact]
    public async Task Sekiz_paralel_tahsilat_no_benzersiz_ve_bosluksuz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account;
        using (var s0 = host.ScopeFor(tenant))
            account = await s0.ServiceProvider.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Sıra No" });

        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            await s.ServiceProvider.GetRequiredService<CashService>().CollectAsync(
                new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        }));
        await Task.WhenAll(tasks);

        using var check = host.ScopeFor(tenant);
        var list = await check.ServiceProvider.GetRequiredService<CashService>().ListAsync();
        Assert.Equal(8, list.Count);

        // Küme karşılaştırması (sıra garantisi değil, BOŞLUKSUZLUK): TH-000001..TH-000008, tekrarsız.
        var expected = Enumerable.Range(1, 8).Select(i => DocumentNoOracle.Wait(5, i)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var actual = list.Select(t => t.No).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);

        // Toplam da tutarlı: 8 × 100 tahsilat → cari −800 (alacaklandı).
        Assert.Equal(-800m, await check.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(account));
    }
}
