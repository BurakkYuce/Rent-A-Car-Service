using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Tarih giriş politikası (TarihPolitikasi) — server-side asıl koruma. BAĞIMSIZ ORACLE: beklenen sınırlar
/// elle (now±N gün / +400 gün = 1 yıldan fazla), servis kodundan türetilmez. Politika kararları (2026-07-06):
/// kira geçmişe AÇIK + gelecek ≤ +1yıl; rezervasyon geçmişe KAPALI + gelecek ≤ +1yıl; doğum gelecek reddi;
/// tahsilat/gider gelecek reddi (savunma).
/// </summary>
[Collection("postgres")]
public sealed class TarihKisitlariTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static async Task<(Guid cari, Guid veh)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Test", Soyad = "Musteri" });
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        return (cari, veh);
    }

    private static BookingInput Booking(Guid cari, Guid veh, DateTimeOffset bas, DateTimeOffset bit) =>
        new() { MusteriId = cari, VehicleId = veh, BasTar = bas, BitTar = bit, GunlukUcret = 100m };

    // ---------- KİRA: geçmişe açık, gelecek ≤ +1 yıl ----------
    [Fact]
    public async Task Kira_gecmis_tarihli_serbest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TK 01");
        // 30 gün önce çıkmış, şimdi giriliyor (retroaktif) → serbest.
        var id = await scope.ServiceProvider.GetRequiredService<RentalService>()
            .CreateDirectAsync(Booking(cari, veh, Now.AddDays(-30), Now.AddDays(-27)));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Kira_yakin_gelecek_serbest_prep_ahead()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TK 02");
        // "bugün hazırla, 2 gün sonra çık" → kilitlenmemeli.
        var id = await scope.ServiceProvider.GetRequiredService<RentalService>()
            .CreateDirectAsync(Booking(cari, veh, Now.AddDays(2), Now.AddDays(5)));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Kira_bir_yildan_fazla_ileri_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TK 03");
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<RentalService>()
                .CreateDirectAsync(Booking(cari, veh, Now.AddDays(400), Now.AddDays(403))));
        Assert.Contains("1 yıl", ex.Message);
    }

    [Fact]
    public async Task Kira_bitis_baslangictan_once_reddedilir_regresyon()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TK 04");
        // bit < bas → BookingMath.Validate zaten reddeder (bug DEĞİL, mevcut koruma — regresyon).
        await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<RentalService>()
                .CreateDirectAsync(Booking(cari, veh, Now.AddDays(3), Now.AddDays(1))));
    }

    // ---------- REZERVASYON: geçmişe kapalı, gelecek ≤ +1 yıl ----------
    [Fact]
    public async Task Rez_gecmis_tarihli_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TR 01");
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<ReservationService>()
                .CreateAsync(Booking(cari, veh, Now.AddDays(-30), Now.AddDays(-27))));
        Assert.Contains("geçmiş", ex.Message);
    }

    [Fact]
    public async Task Rez_yakin_gelecek_serbest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TR 02");
        var id = await scope.ServiceProvider.GetRequiredService<ReservationService>()
            .CreateAsync(Booking(cari, veh, Now.AddDays(10), Now.AddDays(13)));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task Rez_bir_yildan_fazla_ileri_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TR 03");
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<ReservationService>()
                .CreateAsync(Booking(cari, veh, Now.AddDays(400), Now.AddDays(403))));
        Assert.Contains("1 yıl", ex.Message);
    }

    [Fact]
    public async Task Rez_update_gecmise_tasima_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var res = scope.ServiceProvider.GetRequiredService<ReservationService>();
        var (cari, veh) = await SeedAsync(scope.ServiceProvider, "34 TR 04");
        var id = await res.CreateAsync(Booking(cari, veh, Now.AddDays(10), Now.AddDays(13)));
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            res.UpdateAsync(id, Booking(cari, veh, Now.AddDays(-30), Now.AddDays(-27))));
        Assert.Contains("geçmiş", ex.Message);
    }

    // ---------- DOĞUM: gelecek reddi ----------
    [Fact]
    public async Task Dogum_gelecek_tarihli_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
            { Tip = CariType.Bireysel, Ad = "Gelecek", Soyad = "Bebek", DogumTarihi = Now.AddDays(1) }));
        Assert.Contains("gelecekte", ex.Message);
    }

    [Fact]
    public async Task Dogum_gecmis_tarihli_serbest()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var id = await scope.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "Normal", Soyad = "Musteri", DogumTarihi = Now.AddYears(-30) });
        Assert.NotEqual(Guid.Empty, id);
    }

    // ---------- TAHSİLAT / GİDER: gelecek reddi (savunma) ----------
    [Fact]
    public async Task Tahsilat_gelecek_tarihli_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cari = await scope.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Cari", Soyad = "X" });
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<CashService>()
                .CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Tarih = Now.AddDays(30) }));
        Assert.Contains("gelecekte", ex.Message);
    }

    [Fact]
    public async Task Gider_gelecek_tarihli_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
            { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0.20m, Tarih = Now.AddDays(30) }));
        Assert.Contains("gelecekte", ex.Message);
    }
}
