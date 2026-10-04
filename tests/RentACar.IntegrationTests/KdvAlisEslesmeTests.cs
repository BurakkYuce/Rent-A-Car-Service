using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Expenses;
using RentACar.Application.GelenEFaturalar;
using RentACar.Application.Reporting;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #369 adversarial incelemesinden kalıcılaştırılan durumlar (KDV geniş görünüm, alış tarafı). Beklenenler elle.
/// </summary>
[Collection("postgres")]
public sealed class KdvAlisEslesmeTests(PostgresFixture fx)
{
    private static GelenEFaturaInput Incoming(string ettn, decimal net, decimal vat, DateTimeOffset date)
        => new()
        {
            Ettn = ettn, GonderenVkn = "1234567890", GonderenUnvan = "Tedarikçi A.Ş.",
            Tarih = date, NetTutar = net, KdvTutar = vat, GenelToplam = net + vat, Currency = "TRY"
        };

    /// <summary>
    /// Aynı alış belgesi: gelen e-Fatura ("İşlendi" = elle muhasebeleştirildi) + elle girilen gider; giderin evrak no'su
    /// ETTN'i farklı yazımla taşır (küçük harf, "ETTN " öneki, boşluk). Tek belge → indirilecek KDV 200, tek alış satırı.
    /// </summary>
    [Theory]
    [InlineData("lower")]
    [InlineData("prefix")]
    [InlineData("spaces")]
    public async Task Same_document_is_counted_once_regardless_of_ettn_spelling(string variant)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var date = TestZaman.DaysLater(-2);
        var ettn = Guid.NewGuid().ToString().ToUpperInvariant();

        var incoming = scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>();
        var id = await incoming.CreateManualAsync(Incoming(ettn, 1000m, 200m, date));
        Assert.True(await incoming.LinkAsync(new GelenEFaturaBaglamaInput { Id = id, Kdv20Matrah = 1000m, Kdv20 = 200m }));
        Assert.True(await incoming.ApproveAsync(id));
        Assert.True(await incoming.IsleAsync(id));

        var evrak = variant switch
        {
            "lower" => ettn.ToLowerInvariant(),
            "prefix" => "ETTN " + ettn,
            _ => "  " + ettn + "  ",
        };
        await scope.ServiceProvider.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tarih = date, NetTutar = 1000m, KdvOrani = 0.20m, EvrakNo = evrak, Aciklama = "Tedarikçi faturası (elle)" });

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(TestZaman.DaysLater(-5, 0), TestZaman.DaysLater(1, 0), includePurchases: true);
        Assert.Equal(200m, g.AlisKdv);
        Assert.Equal(1, g.AlisBelgeAdet);
    }

    /// <summary>USD gider 100 net %20 @35 → base net 3.500, KDV 700; Gelir-Gider'in indirilecek KDV'si de 700.</summary>
    [Fact]
    public async Task Fx_expense_vat_is_in_base_and_matches_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var date = TestZaman.DaysLater(-2);
        await scope.ServiceProvider.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        { Tarih = date, NetTutar = 100m, KdvOrani = 0.20m, Doviz = "USD", Kur = 35m, Aciklama = "Dövizli" });

        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var g = await reports.GetVatExtendedAsync(TestZaman.DaysLater(-5, 0), TestZaman.DaysLater(1, 0), includePurchases: true);
        Assert.Equal(700m, g.AlisKdv);
        Assert.Equal(3500m, g.AlisNet);
        var gg = await reports.GetRevenueExpenseAsync(TestZaman.DaysLater(-5, 0), TestZaman.DaysLater(1, 0));
        Assert.Equal(700m, gg.KdvIndirilecek);
    }

    /// <summary>
    /// Kırılımsız belgede oran yalnız TEK standart oran TAM uyuyorsa seçilir. 0,04 net / 0,00 KDV: %10, %1 ve %0'ın üçü de
    /// round(net×oran,2) = 0,00 verir → belirsiz → "Diğer" kovası (hiçbir kademeye yazılmaz).
    /// </summary>
    [Fact]
    public async Task Ambiguous_rate_without_breakdown_goes_to_other()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var date = TestZaman.DaysLater(-2);
        await scope.ServiceProvider.GetRequiredService<IncomingEInvoiceService>()
            .CreateManualAsync(Incoming("ETTN-BELIRSIZ", 0.04m, 0m, date));

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetVatExtendedAsync(TestZaman.DaysLater(-5, 0), TestZaman.DaysLater(1, 0), includePurchases: true);
        var row = Assert.Single(g.Satirlar, r => r.AlisMi);
        Assert.Equal(0.04m, row.DigerNet);
        Assert.Equal(0m, row.Net10 + row.Net1 + row.Net0 + row.Net20);
    }
}
