using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Kur;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Denetim O5 — CRM ciro/segment ÇOK-DÖVİZ TL-baz (KurSnapshot). BAĞIMSIZ ORACLE: EUR kira 3×100=300 EUR,
/// sabit kur 40 → ciro 300×40=12.000 TL (düz 300 DEĞİL); VIP eşiği (≥10.000) artık anlamlı: 300 EUR müşteri
/// VIP, 300 TL müşteri Standart. Kur çözülemeyen FX kira REDDEDİLİR (sessiz 1:1 ciro yerine erken hata).
/// Snapshot yalnız RAPORLAMA — defter/fatura kendi kurunu kullanır (KiraFaturaDovizTests bunu ayrıca kapsar).
/// </summary>
[Collection("postgres")]
public sealed class CrmCiroFxTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> RentalAsync(IServiceProvider sp, Guid account, string plate, string? currency)
    {
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = account, VehicleId = veh, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m, Doviz = currency });
    }

    [Fact]
    public async Task FX_kira_cirosu_TL_baz_ve_VIP_esigi_anlamli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });

        var accountFx = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Euro", Soyad = "Musteri" });
        var accountTry = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Lira", Soyad = "Musteri" });
        await RentalAsync(sp, accountFx, "34 CX 01", "EURO"); // 300 EUR @40 → 12.000 TL
        await RentalAsync(sp, accountTry, "34 CX 02", "TL");   // 300 TL (snapshot 1 — regresyon)

        // CRM liste cirosu TL-baz.
        var rows = await sp.GetRequiredService<CustomerService>().SearchRowsAsync(new CustomerFilter());
        Assert.Equal(12000m, rows.Items.Single(r => r.Id == accountFx).Ciro); // 300×40 (düz 300 DEĞİL)
        Assert.Equal(300m, rows.Items.Single(r => r.Id == accountTry).Ciro);   // TRY snapshot=1 korunur

        // Segment: 12.000 ≥ 10.000 → VIP; 300 TL → Standart (eski bug: EUR müşteri "Standart" kalırdı).
        var seg = await sp.GetRequiredService<ReportService>().GetCustomerSegmentAsync();
        Assert.Equal("VIP", seg.Single(s => s.CariId == accountFx).Segment);
        Assert.Equal("Standart", seg.Single(s => s.CariId == accountTry).Segment);
    }

    [Fact]
    public async Task Kur_cozulemeyen_FX_kira_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // GBP için ne sabit kur ne TCMB kaydı var → FX kira erken ve temiz reddedilir.
        var account = await TestCustomer.NewAsync(sp); // gerçek cari: red kurdan gelmeli, varlık kontrolünden değil
        await Assert.ThrowsAsync<ValidationException>(() => RentalAsync(sp, account, "34 CX 03", "GBP"));
    }
}
