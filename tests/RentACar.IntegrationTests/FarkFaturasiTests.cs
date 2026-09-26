using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-F1 — fark faturası: kira dönüşten ÖNCE faturalandıysa, dönüş ek bedeli (fazla km/yakıt/uzatma) için
/// FARK faturası kesilir → defter sözleşme GenelToplam'ı ile tutarlı kalır (adversarial P9 ıraksaması kapandı).
/// BAĞIMSIZ ORACLE: 3 gün × 100 = 300 baz; dönüş 300 km aşım × 2 = 600 fazla; toplam 900. Base fatura 300,
/// fark 600, cari 900.
/// </summary>
[Collection("postgres")]
public sealed class FarkFaturasiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private static async Task<Guid> KurKiraAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Fark", Soyad = "M" });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, KmLimit = 300, FazlaKmUcret = 2m });
    }

    [Fact]
    public async Task Donusten_once_fatura_sonra_fark_defteri_kapatir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();
        var id = await KurKiraAsync(sp, "34 FK 01");
        var cariId = (await rentals.GetAsync(id))!.MusteriId;

        // 1) Dönüşten ÖNCE faturala → baz 300; cari borç 300.
        await invoices.CreateFromRentalAsync(id);
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(cariId));

        // 2) Teslim + dönüş: 300 km aşım × 2 = 600 fazla km bedeli → sözleşme GenelToplam 900.
        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(id, returnKm: 1600, returnFuel: 8, Bas.AddDays(3));
        Assert.Equal(900m, (await rentals.GetAsync(id))!.GenelToplam);

        // 3) Tekrar faturala → FARK faturası 600 (900 − 300); cari borç ARTIK 900 (defter = sözleşme).
        await invoices.CreateFromRentalAsync(id);
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(cariId));

        // 4) Üçüncü faturalama → yeni ek bedel yok → temiz red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(id));
        Assert.Contains("zaten tam faturalanmış", ex.Message);
    }

    [Fact]
    public async Task Fark_faturasi_rentalId_null_kaynakKira_dolu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();
        var id = await KurKiraAsync(sp, "34 FK 02");

        await invoices.CreateFromRentalAsync(id);
        await rentals.DeliverAsync(id, pickupKm: 0, pickupFuel: 8);
        await rentals.ReturnAsync(id, returnKm: 500, returnFuel: 8, Bas.AddDays(3)); // 200 aşım × 2 = 400
        var farkId = await invoices.CreateFromRentalAsync(id);

        var fark = await sp.GetRequiredService<IInvoiceRepository>().FindAsync(farkId);
        Assert.Null(fark!.RentalId);           // kira-fatura unique index'ine çarpmaz
        Assert.Equal(id, fark.KaynakKiraId);   // kira bağı
        Assert.Equal(400m, fark.GenelToplam);  // 200 km × 2
    }

    [Fact]
    public async Task Escantli_cift_fark_idempotent_tek_yazar()
    {
        // Adversarial Kritik-1: N eşzamanlı CreateFromRentalAsync → TEK fark (900), cari 900 (12× DEĞİL).
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id, cariId;
        using (var scope = host.ScopeFor(tenant))
        {
            var sp = scope.ServiceProvider;
            id = await KurKiraAsync(sp, "34 FK 04");
            cariId = (await sp.GetRequiredService<RentalService>().GetAsync(id))!.MusteriId;
            await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
            await sp.GetRequiredService<RentalService>().DeliverAsync(id, pickupKm: 1000, pickupFuel: 8);
            await sp.GetRequiredService<RentalService>().ReturnAsync(id, returnKm: 1600, returnFuel: 8, Bas.AddDays(3));
        }

        // 10 eşzamanlı fark denemesi — her biri kendi scope'unda (kendi DbContext'i).
        var basari = 0;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try { await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id); Interlocked.Increment(ref basari); }
            catch (ValidationException) { /* idempotent red beklenir */ }
        })));

        using var check = host.ScopeFor(tenant);
        Assert.Equal(1, basari);                                                            // yalnız 1 fark başardı
        Assert.Equal(900m, await check.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(cariId)); // 12× değil
    }

    [Fact]
    public async Task Iade_sonrasi_donus_farki_defteri_hizalar()
    {
        // Adversarial High-2: base iade → dönüş ek bedel → fark, iade netlenerek defter = sözleşme.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();
        var id = await KurKiraAsync(sp, "34 FK 05");
        var cariId = (await rentals.GetAsync(id))!.MusteriId;

        var bazId = await invoices.CreateFromRentalAsync(id);        // base 300
        await invoices.CreateRefundAsync(bazId);                        // iade → cari 0
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(cariId));
        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(id, returnKm: 1600, returnFuel: 8, Bas.AddDays(3)); // sözleşme 900
        await invoices.CreateFromRentalAsync(id);                     // fark = 900 − 0 (iade netlendi)
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(cariId));   // defter = sözleşme
    }

    [Fact]
    public async Task Iade_sonrasi_yeniden_faturalanabilir()
    {
        // Adversarial High-3: base iade → yeniden faturala → cari tekrar 300 (kayıp gelir yok).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var invoices = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();
        var id = await KurKiraAsync(sp, "34 FK 06");
        var cariId = (await sp.GetRequiredService<RentalService>().GetAsync(id))!.MusteriId;

        var bazId = await invoices.CreateFromRentalAsync(id);        // 300
        await invoices.CreateRefundAsync(bazId);                        // iade → 0
        await invoices.CreateFromRentalAsync(id);                     // yeniden → 300 (fark yolu, iade netlendi)
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(cariId));
    }

    [Fact]
    public async Task Fark_iade_edilince_yeniden_kesilebilir()
    {
        // Adversarial V6: fark faturasının KENDİSİ iade edilirse aynı ek bedel yeniden kesilebilmeli (FarkSira
        // idempotency: iade'li fark sayaçta kalır → yeniden kesim yeni sıra alır; mutlak-hedef kilidi yok).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var invoices = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();
        var id = await KurKiraAsync(sp, "34 FK 07");
        var cariId = (await rentals.GetAsync(id))!.MusteriId;

        await invoices.CreateFromRentalAsync(id);                    // base 300
        await rentals.DeliverAsync(id, pickupKm: 1000, pickupFuel: 8);
        await rentals.ReturnAsync(id, returnKm: 1600, returnFuel: 8, Bas.AddDays(3)); // sözleşme 900
        var fark1 = await invoices.CreateFromRentalAsync(id);        // fark 600 (sıra 1) → cari 900
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(cariId));

        await invoices.CreateRefundAsync(fark1);                        // fark'ı iade et → cari 300
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(cariId));

        await invoices.CreateFromRentalAsync(id);                     // yeniden fark 600 (sıra 2) → cari 900
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(cariId));   // defter = sözleşme (kilitlenmedi)
    }

    [Fact]
    public async Task Donussuz_tek_fatura_hala_calisir()
    {
        // Regresyon: dönüşsüz normal faturalama (yaygın akış) bozulmadı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var invoices = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();
        var id = await KurKiraAsync(sp, "34 FK 03");
        var cariId = (await sp.GetRequiredService<RentalService>().GetAsync(id))!.MusteriId;

        await invoices.CreateFromRentalAsync(id);
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(cariId));
        // İkinci kez (değişiklik yok) → temiz red.
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(id));
    }
}
