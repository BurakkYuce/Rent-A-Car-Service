using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// KDV Listesi raporu — bağımsız oracle. %20 ve %10 satırlı faturalar; İptal ve dönem-dışı hariç;
/// çok-döviz Kur baz dönüşümü. Beklenen toplamlar ELLE hesaplanır (rapor kodundan değil).
/// </summary>
[Collection("postgres")]
public sealed class KdvRaporuTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    private static Invoice Inv(Guid cariId, string no, DateTimeOffset tarih, InvoiceStatus durum, decimal kur, params (decimal oran, decimal net, decimal kdv)[] lines)
    {
        var inv = new Invoice
        {
            No = no, Durum = durum, CariId = cariId, Tarih = tarih,
            Currency = kur == 1m ? "TRY" : "USD", Kur = kur,
            NetTutar = lines.Sum(l => l.net), KdvTutar = lines.Sum(l => l.kdv),
            GenelToplam = lines.Sum(l => l.net + l.kdv)
        };
        foreach (var (oran, net, kdv) in lines)
            inv.Lines.Add(new InvoiceLine
            {
                InvoiceId = inv.Id, Aciklama = "kalem", Miktar = 1m, BirimNetFiyat = net,
                KdvOrani = oran, SatirNet = net, SatirKdv = kdv, SatirToplam = net + kdv
            });
        return inv;
    }

    [Fact]
    public async Task Groups_by_rate_excludes_cancelled_and_out_of_range()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CariType.Bireysel, Ad = "Test", Soyad = "Cari" };
            db.Customers.Add(c);
            // A: dönem içi, %20 (100/20) + %10 (50/5)
            db.Invoices.Add(Inv(c.Id, "FT-K01", D(2026, 6, 10), InvoiceStatus.Kesildi, 1m, (0.20m, 100m, 20m), (0.10m, 50m, 5m)));
            // B: dönem içi, %20 (200/40)
            db.Invoices.Add(Inv(c.Id, "FT-K02", D(2026, 6, 15), InvoiceStatus.Kesildi, 1m, (0.20m, 200m, 40m)));
            // C: İptal → HARİÇ
            db.Invoices.Add(Inv(c.Id, "FT-K03", D(2026, 6, 12), InvoiceStatus.Iptal, 1m, (0.20m, 999m, 199.8m)));
            // D: dönem DIŞI (mayıs) → HARİÇ
            db.Invoices.Add(Inv(c.Id, "FT-K04", D(2026, 5, 1), InvoiceStatus.Kesildi, 1m, (0.20m, 1000m, 200m)));
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var rapor = await svc.GetKdvListesiAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1));

        Assert.Equal(2, rapor.Satirlar.Count); // %10 ve %20
        var y20 = rapor.Satirlar.Single(s => s.Oran == 0.20m);
        Assert.Equal(300m, y20.Net);   // 100 + 200
        Assert.Equal(60m, y20.Kdv);    // 20 + 40
        Assert.Equal(360m, y20.Brut);
        Assert.Equal(2, y20.FaturaAdet); // A + B

        var y10 = rapor.Satirlar.Single(s => s.Oran == 0.10m);
        Assert.Equal(50m, y10.Net);
        Assert.Equal(5m, y10.Kdv);
        Assert.Equal(1, y10.FaturaAdet); // yalnız A

        Assert.Equal(350m, rapor.ToplamNet);   // 300 + 50
        Assert.Equal(65m, rapor.ToplamKdv);    // 60 + 5
        Assert.Equal(415m, rapor.ToplamBrut);
        Assert.Equal(2, rapor.FaturaAdet);     // distinct A, B (C/D hariç)
    }

    [Fact]
    public async Task Applies_currency_rate_to_base()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var c = new Customer { Tip = CariType.Bireysel, Ad = "Doviz", Soyad = "Cari" };
            db.Customers.Add(c);
            // USD fatura, Kur 30: net 10 / kdv 2 → base net 300, kdv 60, brüt 360.
            db.Invoices.Add(Inv(c.Id, "FT-USD1", D(2026, 6, 5), InvoiceStatus.Kesildi, 30m, (0.20m, 10m, 2m)));
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var rapor = await svc.GetKdvListesiAsync(D(2026, 6, 1), D(2026, 6, 30).AddDays(1).AddTicks(-1));

        var y20 = Assert.Single(rapor.Satirlar);
        Assert.Equal(0.20m, y20.Oran);
        Assert.Equal(300m, y20.Net);  // 10 × 30
        Assert.Equal(60m, y20.Kdv);   // 2 × 30
        Assert.Equal(360m, y20.Brut); // 12 × 30
    }

    [Fact]
    public async Task Tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var factory = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var c = new Customer { Tip = CariType.Bireysel, Ad = "T1", Soyad = "C" };
            db.Customers.Add(c);
            db.Invoices.Add(Inv(c.Id, "FT-T1", D(2026, 6, 5), InvoiceStatus.Kesildi, 1m, (0.20m, 100m, 20m)));
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var rapor = await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetKdvListesiAsync(D(2026, 6, 1), D(2026, 6, 30));
        Assert.Empty(rapor.Satirlar);
        Assert.Equal(0, rapor.FaturaAdet);
    }
}
