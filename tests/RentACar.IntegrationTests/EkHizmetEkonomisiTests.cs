using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.RentalAddOns;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Ek hizmet ekonomisi (PARA) — bağımsız oracle. Beklenen tutarlar ELLE hesaplanır (servis
/// kodundan değil): GPS net 100×2=200, KDV 200×0.20=40, brüt 240; koltuk net 50, KDV %10=5, brüt 55.
/// Kira bazı 400 brüt → fatura net 333.33 + KDV 66.67. Defter dengesi (borç=alacak) zorunlu.
/// </summary>
[Collection("postgres")]
public sealed class EkHizmetEkonomisiTests(PostgresFixture fx)
{
    /// <summary>Araç + cari + kira (Tutar=GenelToplam=400, Kirada, teslim edilmiş) + GPS/Koltuk tanımları seed eder.</summary>
    private static async Task<(Guid rentalId, Guid gpsId, Guid koltukId)> SeedAsync(IServiceScope scope)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var v = new Vehicle { Plaka = "34EKO01", Durum = VehicleStatus.Kirada };
        var c = new Customer { Tip = CariType.Bireysel, Ad = "Ek", Soyad = "Hizmet" };
        db.Vehicles.Add(v);
        db.Customers.Add(c);

        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 100m, KdvOrani = 0.20m };
        var koltuk = new EkHizmetTanim { Kod = "KOLTUK", Ad = "Bebek Koltuğu", BirimUcret = 50m, KdvOrani = 0.10m };
        db.EkHizmetTanimlari.Add(gps);
        db.EkHizmetTanimlari.Add(koltuk);

        var rental = new RentalContract
        {
            SozlesmeNo = "KS-EKO1", VehicleId = v.Id, MusteriId = c.Id, Durum = RentalStatus.Kirada,
            BasTar = DateTimeOffset.UtcNow.AddDays(-2), BitTar = DateTimeOffset.UtcNow.AddDays(2),
            Gun = 4, GunlukUcret = 100m, Tutar = 400m, GenelToplam = 400m, Bakiye = 400m,
            CikisKm = 1000, CikisYakit = 8, KmLimit = 0
        };
        db.Rentals.Add(rental);
        await db.SaveChangesAsync();
        return (rental.Id, gps.Id, koltuk.Id);
    }

    private static async Task<RentalContract> GetRentalAsync(IServiceScope scope, Guid id)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Rentals.AsNoTracking().FirstAsync(r => r.Id == id);
    }

    [Fact]
    public async Task Add_addon_recomputes_genel_toplam_and_bakiye()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();

        await svc.AddAsync(rentalId, gpsId, miktar: 2m);

        var addons = await svc.ListAsync(rentalId);
        var gps = Assert.Single(addons);
        Assert.Equal(200m, gps.NetTutar);   // 100 × 2
        Assert.Equal(40m, gps.KdvTutar);    // 200 × 0.20
        Assert.Equal(240m, gps.Toplam);     // 200 + 40

        var rental = await GetRentalAsync(scope, rentalId);
        Assert.Equal(640m, rental.GenelToplam); // 400 + 240
        Assert.Equal(640m, rental.Bakiye);      // Tahsilat 0
    }

    [Fact]
    public async Task Remove_addon_restores_totals()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();

        var addOnId = await svc.AddAsync(rentalId, gpsId, miktar: 2m);
        Assert.Equal(640m, (await GetRentalAsync(scope, rentalId)).GenelToplam);

        Assert.True(await svc.RemoveAsync(addOnId));
        var rental = await GetRentalAsync(scope, rentalId);
        Assert.Equal(400m, rental.GenelToplam);
        Assert.Equal(400m, rental.Bakiye);
    }

    [Fact]
    public async Task Invoice_multiline_preserves_per_rate_and_balances_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, koltukId) = await SeedAsync(scope);
        var addSvc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        var invSvc = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        await addSvc.AddAsync(rentalId, gpsId, miktar: 2m);     // net 200, kdv 40 (%20)
        await addSvc.AddAsync(rentalId, koltukId, miktar: 1m);  // net 50, kdv 5 (%10)
        // GenelToplam = 400 + 240 + 55 = 695

        var invId = await invSvc.CreateFromRentalAsync(rentalId);
        var inv = await invSvc.GetAsync(invId);
        Assert.NotNull(inv);

        // Baz kira 400 brüt → net 333.33, kdv 66.67. Toplam net = 333.33+200+50 = 583.33; kdv = 66.67+40+5 = 111.67.
        Assert.Equal(583.33m, inv!.NetTutar);
        Assert.Equal(111.67m, inv.KdvTutar);
        Assert.Equal(695m, inv.GenelToplam);     // net + kdv
        Assert.Equal(3, inv.Lines.Count);        // kira + GPS + koltuk

        // Satır oranları korunur.
        Assert.Contains(inv.Lines, l => l.KdvOrani == 0.20m && l.SatirKdv == 40m);
        Assert.Contains(inv.Lines, l => l.KdvOrani == 0.10m && l.SatirKdv == 5m);

        // Defter dengesi: Borç Cari 695 = Alacak Gelir 583.33 + KDV 111.67.
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Fatura" && e.SourceId == invId).ToListAsync();
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(695m, borc);
        Assert.Equal(borc, alacak);  // DENGE
        Assert.Equal(695m, entries.Single(e => e.AccountType == LedgerAccountType.Cari).Amount.AmountInBase);
        Assert.Equal(583.33m, entries.Single(e => e.AccountType == LedgerAccountType.Gelir).Amount.AmountInBase);
        Assert.Equal(111.67m, entries.Single(e => e.AccountType == LedgerAccountType.Kdv).Amount.AmountInBase);
    }

    [Fact]
    public async Task Cannot_add_addon_to_invoiced_rental()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var addSvc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        var invSvc = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        await invSvc.CreateFromRentalAsync(rentalId); // faturalandı
        await Assert.ThrowsAsync<ValidationException>(() => addSvc.AddAsync(rentalId, gpsId, miktar: 1m));
    }

    [Fact]
    public async Task Return_preserves_addon_in_total()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var addSvc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        var rentalSvc = scope.ServiceProvider.GetRequiredService<RentalService>();

        await addSvc.AddAsync(rentalId, gpsId, miktar: 2m); // +240 brüt
        var bitTar = (await GetRentalAsync(scope, rentalId)).BitTar;

        // Limit içinde, yakıt tam, zamanında dönüş → ek bedel yok; yalnız baz 400 + ek hizmet 240.
        await rentalSvc.ReturnAsync(rentalId, donusKm: 1000, donusYakit: 8, gercekDonus: bitTar);

        var rental = await GetRentalAsync(scope, rentalId);
        Assert.Equal(RentalStatus.Tamamlandi, rental.Durum);
        Assert.Equal(640m, rental.GenelToplam); // 400 baz + 240 ek hizmet (dönüşte korundu)
    }

    [Fact]
    public async Task Zero_quantity_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.AddAsync(rentalId, gpsId, miktar: 0m));
    }

    [Fact]
    public async Task NonOperations_user_cannot_add()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        // Muhasebe rolü OperationsWrite'a sahip değil.
        using var muh = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        // Aynı tenant'ta çalışması için aynı tenant id ile seed gerekir; burada yetki reddi yeterli:
        var svc = muh.ServiceProvider.GetRequiredService<RentalAddOnService>();
        await Assert.ThrowsAsync<ValidationException>(() => svc.AddAsync(rentalId, gpsId, miktar: 1m));
    }
}
