using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// KVKK — rapor export'ları ekrandaki <c>CustomerMask</c> kuralını uygular: anonim (AnonimAd/AnonimTelefon) carinin
/// gerçek adı ve telefonu indirilen dosyada da görünmez. Ekran zaten maskeliydi; export ham DisplayName yazıyordu.
/// </summary>
public sealed partial class UiReportTests
{
    [Fact]
    public async Task Report_exports_mask_anonymous_customer_like_the_screen()
    {
        // SetupAsync: anonim B carisine araç satışı (borç 1.200) → yaşlandırma + cari bakiye satırı.
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var today = TenantDay.Day(DateTimeOffset.UtcNow);
        var yesterday = today.AddDays(-1);

        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = e.CustomerB, Tutar = 100m });
            var invoices = sp.GetRequiredService<InvoiceService>();
            await invoices.CreateManualAsync(new ManualInvoiceInput { CariId = e.CustomerB, NetTutar = 100m, KdvOrani = 0.20m });
            await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = e.CustomerB, VehicleId = e.VehicleB, GunlukUcret = 100m,
                BasTar = new DateTimeOffset(yesterday.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
                BitTar = new DateTimeOffset(today.AddDays(2).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            });
        }

        var period = $"from={yesterday:yyyy-MM-dd}&to={today:yyyy-MM-dd}";
        string[] urls =
        [
            "/raporlar/export/yaslandirma?format=csv",
            "/raporlar/export/cari-bakiye?format=csv",
            "/raporlar/export/kasa-banka?format=csv&hesap=Kasa&" + period,
            "/raporlar/export/kdv-genis?format=csv&" + period,
            "/raporlar/export/fatura-donem?format=csv&" + period,
            "/raporlar/export/kira-fatura-durum?format=csv&" + period,
            "/raporlar/export/karlilik?format=csv&" + period,
            $"/raporlar/export/arac-gunluk-durum?format=csv&gun={today:yyyy-MM-dd}",
        ];
        var leaks = new List<string>();
        var masked = 0;
        foreach (var url in urls)
        {
            var res = await s.C.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            if (body.Contains(AnonymousRealName, StringComparison.Ordinal) || body.Contains("05329999999", StringComparison.Ordinal))
                leaks.Add(url);
            if (body.Contains(RentACar.Application.Customers.CustomerAnonymity.NameLabel, StringComparison.Ordinal)) masked++;
        }
        Assert.Empty(leaks);
        Assert.Equal(urls.Length, masked);   // her dosyada anonim cari satırı VAR ve etiketiyle yazılmış
    }
}
