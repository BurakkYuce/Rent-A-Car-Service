using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ4-4.2-B3 — Dönem kes + tahsilat orkestratörü (endpoint bu akışı çağırır). BAĞIMSIZ ORACLE
/// (elle): 15 Oca 2027 + 90g × 100 = 9000; D1 = 3100. ÇİFT-SUBMIT → TEK fatura + TEK tahsilat
/// (fatura idempotent; tahsilat DETERMİNİSTİK RowKey(rentalId, donemSira) anahtarıyla — ikinci
/// yazım sessiz no-op). Bakiye mutabakatı: cari 0 (fatura borç == tahsilat alacak); kira Tahsilat
/// alanı işler. Tahsilatsız yol yalnız fatura keser.
/// </summary>
[Collection("postgres")]
public sealed class DonemUiTahsilatTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2027, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid kira, Guid cari)> RentalAsync(IServiceProvider sp, string plate)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "DT", Soyad = "M" });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Start, BitTar = Start.AddDays(90), GunlukUcret = 100m });
        return (id, m);
    }

    [Fact]
    public async Task Cift_submit_tek_fatura_tek_tahsilat_ve_bakiye_mutabakati()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, account) = await RentalAsync(sp, "34 DT 01");
        var svc = sp.GetRequiredService<PeriodCollectionService>();

        // İki kez aynı istek (çift-tık/geri-tuşu): fatura idempotent + tahsilat RowKey'li no-op.
        var f1 = await svc.IssueAndCollectAsync(rental, 1, collectionRecord: true, LedgerAccountType.Kasa);
        var f2 = await svc.IssueAndCollectAsync(rental, 1, collectionRecord: true, LedgerAccountType.Kasa);
        Assert.Equal(f1, f2);

        // TEK fatura (D1 = 3100) — fark-state faturalananı 3100 (ikinci fatura yok).
        var repo = sp.GetRequiredService<IInvoiceRepository>();
        Assert.Equal(3100m, (await repo.FindAsync(f1))!.GenelToplam);
        var (invoiced, _) = await repo.GetDifferenceStateAsync(rental);
        Assert.Equal(3100m, invoiced);

        // TEK tahsilat: cari bakiye 0 (borç 3100 == alacak 3100) — çift tahsilat -3100 yapardı.
        var balance = await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account);
        Assert.Equal(0m, balance);

        // Kira tahsilat alanı işledi (RentalId bağlı tahsilat).
        Assert.Equal(3100m, (await sp.GetRequiredService<RentalService>().GetAsync(rental))!.Tahsilat);
    }

    [Fact]
    public async Task Tahsilatsiz_yol_yalniz_fatura_keser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, account) = await RentalAsync(sp, "34 DT 02");

        await sp.GetRequiredService<PeriodCollectionService>()
            .IssueAndCollectAsync(rental, 1, collectionRecord: false, LedgerAccountType.Kasa);

        // Fatura borcu cariye işledi (3100), tahsilat YOK → bakiye 3100.
        Assert.Equal(3100m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account));
        Assert.Equal(0m, (await sp.GetRequiredService<RentalService>().GetAsync(rental))!.Tahsilat);
    }
}
