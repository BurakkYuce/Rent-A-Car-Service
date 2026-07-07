using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-F3 — FiyatTuru KDV modları: girilen ücret NET mi BRÜT mü, GÜNLÜK mü TOPLAM mı → doğru brüt Tutar.
/// GunlukUcret hep brüte normalize (uzatma tutarlı); fatura BaseGross→FromGross ile net'i geri ayrıştırır → mod
/// niyeti korunur. BAĞIMSIZ ORACLE: KDV %20; 3 gün. brüt 360 → net 300 + kdv 60; brüt 300 → net 250 + kdv 50.
/// </summary>
[Collection("postgres")]
public sealed class KdvModuTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-2).AddHours(9);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kdv", Soyad = "M" });
        return (m, v);
    }

    private static BookingInput B(Guid m, Guid v, decimal ucret, string mod) => new()
    { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = ucret, FiyatTuru = mod };

    [Theory]
    [InlineData("KDV Dahil Günlük", 100, 300, 100)] // brüt günlük → Tutar 3×100=300; günlük 100
    [InlineData("Günlük", 100, 360, 120)]           // NET günlük 100 → brüt 120; Tutar 3×120=360
    [InlineData("KDV Dahil Toplam", 300, 300, 100)] // brüt toplam 300 → Tutar 300; günlük 300/3=100
    [InlineData("Toplam", 300, 360, 120)]           // NET toplam 300 → brüt 360; günlük 360/3=120
    public async Task Mod_dogru_brut_tutar_ve_gunluk_ucret(string mod, decimal girilen, decimal beklenenTutar, decimal beklenenGunluk)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 KV 0" + mod.Length % 9);
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(B(m, v, girilen, mod));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(beklenenTutar, c!.Tutar);        // brüt Tutar
        Assert.Equal(beklenenGunluk, c.GunlukUcret);  // günlük ücret brüte normalize
    }

    [Theory]
    [InlineData("Günlük", 100, 360, 300, 60)]           // NET girdi → fatura net 300 (girilen niyet), kdv 60
    [InlineData("Toplam", 300, 360, 300, 60)]           // NET toplam → net 300, kdv 60
    [InlineData("KDV Dahil Günlük", 100, 300, 250, 50)] // brüt 300 → net 250, kdv 50
    public async Task Fatura_roundtrip_net_niyeti_korur(string mod, decimal girilen, decimal brut, decimal beklenenNet, decimal beklenenKdv)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 KW 0" + mod.Length % 9);
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(B(m, v, girilen, mod));
        var fId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
        var f = await sp.GetRequiredService<IInvoiceRepository>().FindAsync(fId);
        Assert.Equal(brut, f!.GenelToplam);         // brüt = Tutar
        Assert.Equal(beklenenNet, f.NetTutar);      // net girilen niyeti korur (mod'a göre)
        Assert.Equal(beklenenKdv, f.KdvTutar);
        // Cari borç = brüt (defter).
        Assert.Equal(brut, await sp.GetRequiredService<CashService>().GetCariBalanceAsync(m));
    }

    [Fact]
    public async Task Net_mod_kirada_kdv_orani_override_reddedilir()
    {
        // Adversarial Bulgu-1: "Günlük"(net) kira farklı kdvRate ile faturalanınca net matrah niyetten sapardı →
        // net modda oran override reddedilir. Varsayılan (0.20) serbest.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var invoices = sp.GetRequiredService<InvoiceService>();
        var (m, v) = await SeedAsync(sp, "34 KY 01");
        var netId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(B(m, v, 100, "Günlük"));
        var ex = await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => invoices.CreateFromRentalAsync(netId, kdvRate: 0.10m));
        Assert.Contains("Net fiyat modlu", ex.Message);
        // Varsayılan oran → sorunsuz (net 300).
        var fId = await invoices.CreateFromRentalAsync(netId);
        Assert.Equal(300m, (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(fId))!.NetTutar);

        // Brüt modda override SERBEST (girilen zaten brüt; oran yalnız yeniden ayrıştırır).
        var (m2, v2) = await SeedAsync(sp, "34 KY 02");
        var brutId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(B(m2, v2, 100, "KDV Dahil Günlük"));
        var f2 = await invoices.CreateFromRentalAsync(brutId, kdvRate: 0.10m); // reddedilmez
        Assert.NotEqual(Guid.Empty, f2);
    }

    [Fact]
    public async Task Net_gunluk_uzatma_brut_ile_buyur()
    {
        // "Günlük" (net) modda GunlukUcret brüte normalize → uzatma brüt gün ekler (tutarlı).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var rentals = sp.GetRequiredService<RentalService>();
        var (m, v) = await SeedAsync(sp, "34 KX 01");
        var id = await rentals.CreateDirectAsync(B(m, v, 100, "Günlük")); // günlük brüt 120, Tutar 360
        await rentals.ExtendAsync(id, Bas.AddDays(4)); // +1 gün
        var c = await rentals.GetAsync(id);
        Assert.Equal(4, c!.Gun);
        Assert.Equal(360m + 120m, c.Tutar); // 480: brüt günlük 120 eklendi
    }
}
