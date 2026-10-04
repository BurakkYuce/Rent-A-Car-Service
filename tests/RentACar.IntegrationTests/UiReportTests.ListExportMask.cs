using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Finance;
using RentACar.Application.Legal;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// KVKK — LİSTE export'ları (<c>/listeler/export/*</c>) anonim cariyi ekrandaki kuralla (<c>CustomerView</c>) maskeler:
/// AnonimAd → ad yerine sabit etiket, AnonimTelefon → telefon boş, AnonimMail → e-posta boş, AnonimAdres → adres/il/ilçe
/// boş. Ekranlar maskeliydi; dosyalar ham ad/telefon/e-posta/adres yazıyordu (adversarial tur 2, HIGH).
/// </summary>
public sealed partial class UiReportTests
{
    private const string AnonymousMail = "gizli.kisi@ornek.test";
    private const string AnonymousAddress = "Gizli Sokak No 7";
    private const string AnonymousDistrict = "Gizliilce";

    [Fact]
    public async Task List_exports_mask_anonymous_customer_like_the_screen()
    {
        // SetupAsync: B carisi AnonimAd + AnonimTelefon. Ek olarak bütün bayrakları açık C carisi (e-posta + adres).
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var today = TenantDay.Day(DateTimeOffset.UtcNow);
        var c = new Customer
        {
            Tip = CustomerType.Bireysel, Ad = AnonymousRealName, Soyad = "Tam", CepTel = "05329999999",
            Email = AnonymousMail, Adres = AnonymousAddress, Ilce = AnonymousDistrict,
            AnonimAd = true, AnonimTelefon = true, AnonimMail = true, AnonimAdres = true,
        };
        await WriteAsync(e.TenantId, db => db.Customers.Add(c));

        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = e.CustomerB, Tutar = 100m });
            await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput { CariId = e.CustomerB, NetTutar = 100m, KdvOrani = 0.20m });
            await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = e.CustomerB, VehicleId = e.VehicleB, GunlukUcret = 100m,
                BasTar = new DateTimeOffset(today.AddDays(-1).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
                BitTar = new DateTimeOffset(today.AddDays(2).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            });
            await sp.GetRequiredService<ReservationService>().CreateAsync(new BookingInput
            {
                MusteriId = e.CustomerB, VehicleId = e.VehicleA, GunlukUcret = 100m,
                BasTar = new DateTimeOffset(today.AddDays(10).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
                BitTar = new DateTimeOffset(today.AddDays(12).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            });
            await sp.GetRequiredService<LegalCaseService>().CreateAsync(new HukukDosyaInput
            { DosyaNo = "2026/KVKK", CariId = e.CustomerB, Tutar = 100m });
        }

        string[] urls =
        [
            "/listeler/export/cariler?format=csv", "/listeler/export/kiralar?format=csv",
            "/listeler/export/faturalar?format=csv", "/listeler/export/nakit-islemler?format=csv",
            "/listeler/export/rezervasyonlar?format=csv", "/listeler/export/hukuk?format=csv",
        ];
        var leaks = new List<string>();
        foreach (var url in urls)
        {
            var res = await s.C.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            foreach (var secret in new[] { AnonymousRealName, "05329999999", AnonymousMail, AnonymousAddress, AnonymousDistrict })
                if (body.Contains(secret, StringComparison.Ordinal)) leaks.Add($"{url}: {secret}");
            // Satır gerçekten dosyada (maske boş dosyayla geçmesin): anonim etiketi görünür.
            if (!body.Contains(RentACar.Application.Customers.CustomerAnonymity.NameLabel, StringComparison.Ordinal))
                leaks.Add($"{url}: anonim satır yok");
        }
        Assert.Empty(leaks);

        // Anonim OLMAYAN cari aynen yazılır (maske fazla geniş değil).
        var customers = await (await s.C.GetAsync("/listeler/export/cariler?format=csv")).Content.ReadAsStringAsync();
        Assert.Contains(CustomerA, customers);
        Assert.Contains("05320000001", customers);
    }
}
