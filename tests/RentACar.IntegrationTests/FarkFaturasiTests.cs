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
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Fark", Soyad = "M" });
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
        Assert.Equal(300m, await cash.GetCariBalanceAsync(cariId));

        // 2) Teslim + dönüş: 300 km aşım × 2 = 600 fazla km bedeli → sözleşme GenelToplam 900.
        await rentals.DeliverAsync(id, cikisKm: 1000, cikisYakit: 8);
        await rentals.ReturnAsync(id, donusKm: 1600, donusYakit: 8, Bas.AddDays(3));
        Assert.Equal(900m, (await rentals.GetAsync(id))!.GenelToplam);

        // 3) Tekrar faturala → FARK faturası 600 (900 − 300); cari borç ARTIK 900 (defter = sözleşme).
        await invoices.CreateFromRentalAsync(id);
        Assert.Equal(900m, await cash.GetCariBalanceAsync(cariId));

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
        await rentals.DeliverAsync(id, cikisKm: 0, cikisYakit: 8);
        await rentals.ReturnAsync(id, donusKm: 500, donusYakit: 8, Bas.AddDays(3)); // 200 aşım × 2 = 400
        var farkId = await invoices.CreateFromRentalAsync(id);

        var fark = await sp.GetRequiredService<IInvoiceRepository>().FindAsync(farkId);
        Assert.Null(fark!.RentalId);           // kira-fatura unique index'ine çarpmaz
        Assert.Equal(id, fark.KaynakKiraId);   // kira bağı
        Assert.Equal(400m, fark.GenelToplam);  // 200 km × 2
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
        Assert.Equal(300m, await cash.GetCariBalanceAsync(cariId));
        // İkinci kez (değişiklik yok) → temiz red.
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(id));
    }
}
