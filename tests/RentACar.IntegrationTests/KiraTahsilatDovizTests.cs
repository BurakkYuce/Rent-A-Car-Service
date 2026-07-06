using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Denetim K2+O1 — kira Tahsilat/Bakiye birim disiplini + eşzamanlılık. BAĞIMSIZ ORACLE:
/// EUR kira 3×100=300 EUR; 300 EUR tahsilat → Tahsilat=300, Bakiye=0 (eski bug: TL-baz 300×40=12000
/// birikip Bakiye=−11700 "alacak" gösterirdi). FX kirada farklı döviz tahsilat RED. Ters kayıt geri alır.
/// TRY kirada TL-baz davranış korunur. Paralel iki tahsilat kayıpsız (atomik SQL +=).
/// </summary>
[Collection("postgres")]
public sealed class KiraTahsilatDovizTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(IServiceProvider sp, Guid rentalId, Guid cariId)> Seed(
        IServiceScope scope, string plaka, string? doviz)
    {
        var sp = scope.ServiceProvider;
        var cari = Guid.NewGuid();
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, Doviz = doviz });
        return (sp, id, cari);
    }

    private static CashInput Tahsilat(Guid cari, Guid rental, decimal tutar, string doviz, decimal kur) => new()
    { CariId = cari, RentalId = rental, Tutar = tutar, Doviz = doviz, Kur = kur, Hesap = LedgerAccountType.Kasa };

    [Fact]
    public async Task FX_kira_ayni_doviz_tahsilat_kira_dovizinde_birikir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, cari) = await Seed(scope, "34 KD 01", "EURO"); // 300 EUR kira
        var cash = sp.GetRequiredService<CashService>();

        await cash.CollectAsync(Tahsilat(cari, id, 300m, "EUR", 40m)); // 300 EUR @40
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);

        Assert.Equal(300m, c!.Tahsilat); // KİRA DÖVİZİNDE (eski bug: 12000 TL-baz birikirdi)
        Assert.Equal(0m, c.Bakiye);      // 300 − 300 (eski bug: −11700 "alacak")
        // Defter yine TL-baz doğru: cari bakiye 300×40 tahsilatla düşer (fatura yok → −12000).
        Assert.Equal(-12000m, await cash.GetCariBalanceAsync(cari));
    }

    [Fact]
    public async Task FX_kira_farkli_doviz_tahsilat_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, cari) = await Seed(scope, "34 KD 02", "EURO");
        var cash = sp.GetRequiredService<CashService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CollectAsync(Tahsilat(cari, id, 5000m, "TRY", 1m))); // TL tahsilat FX kiraya bağlanamaz
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(0m, c!.Tahsilat); // hiçbir şey yazılmadı (tx bütünlüğü)
    }

    [Fact]
    public async Task FX_kira_ters_kayit_tahsilati_geri_alir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, cari) = await Seed(scope, "34 KD 03", "EURO");
        var cash = sp.GetRequiredService<CashService>();

        var txId = await cash.CollectAsync(Tahsilat(cari, id, 100m, "EUR", 40m));
        await cash.ReverseAsync(txId);

        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(0m, c!.Tahsilat);   // 100 − 100 (TersKayitMi yönü çevirir, aynı birim)
        Assert.Equal(300m, c.Bakiye);
    }

    [Fact]
    public async Task TRY_kira_doviz_tahsilat_TL_baz_korunur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, cari) = await Seed(scope, "34 KD 04", "TL"); // 300 TL kira
        var cash = sp.GetRequiredService<CashService>();

        await cash.CollectAsync(Tahsilat(cari, id, 5m, "EUR", 40m)); // 5 EUR @40 = 200 TL
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);

        Assert.Equal(200m, c!.Tahsilat); // TL-baz (mevcut davranış korunur)
        Assert.Equal(100m, c.Bakiye);    // 300 − 200
    }

    [Fact]
    public async Task Paralel_iki_tahsilat_kayipsiz() // denetim O1: atomik SQL +=
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid id, cari;
        using (var s0 = host.ScopeFor(tenant))
            (_, id, cari) = await Seed(s0, "34 KD 05", null); // 300 TL kira

        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        await Task.WhenAll(
            s1.ServiceProvider.GetRequiredService<CashService>().CollectAsync(Tahsilat(cari, id, 100m, "TRY", 1m)),
            s2.ServiceProvider.GetRequiredService<CashService>().CollectAsync(Tahsilat(cari, id, 100m, "TRY", 1m)));

        using var s3 = host.ScopeFor(tenant);
        var c = await s3.ServiceProvider.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(200m, c!.Tahsilat); // eski bug (read-modify-write): 100 kalabilirdi
        Assert.Equal(100m, c.Bakiye);
    }
}
