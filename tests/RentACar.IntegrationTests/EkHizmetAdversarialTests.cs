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
/// ADVERSARIAL — ek hizmet ekonomisini ÇÜRÜTME denemeleri. Beklenen değerler ELLE kurulur.
/// </summary>
[Collection("postgres")]
public sealed class EkHizmetAdversarialTests(PostgresFixture fx)
{
    private static async Task<(Guid rentalId, Guid gpsId, Guid koltukId)> SeedAsync(IServiceScope scope, decimal tutar = 400m)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        var v = new Vehicle { Plaka = "34ADV" + Guid.NewGuid().ToString("N")[..4], Durum = VehicleStatus.Kirada };
        var c = new Customer { Tip = CariType.Bireysel, Ad = "Adv", Soyad = "Ersary" };
        db.Vehicles.Add(v);
        db.Customers.Add(c);

        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 100m, KdvOrani = 0.20m };
        var koltuk = new EkHizmetTanim { Kod = "KOLTUK", Ad = "Bebek Koltuğu", BirimUcret = 50m, KdvOrani = 0.10m };
        db.EkHizmetTanimlari.Add(gps);
        db.EkHizmetTanimlari.Add(koltuk);

        var rental = new RentalContract
        {
            SozlesmeNo = "KS-ADV-" + Guid.NewGuid().ToString("N")[..6], VehicleId = v.Id, MusteriId = c.Id,
            Durum = RentalStatus.Kirada,
            BasTar = DateTimeOffset.UtcNow.AddDays(-2), BitTar = DateTimeOffset.UtcNow.AddDays(2),
            Gun = 4, GunlukUcret = 100m, Tutar = tutar, GenelToplam = tutar, Bakiye = tutar,
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

    // ---- VECTOR 5: çift fatura idempotency ----
    [Fact]
    public async Task Double_invoice_same_rental_double_debits_cari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var (rentalId, _, _) = await SeedAsync(scope);
        var invSvc = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        await invSvc.CreateFromRentalAsync(rentalId);
        // İkinci kez faturalama idempotency guard ile REDDEDİLMELİ (cari çift borçlanmaz).
        await Assert.ThrowsAsync<ValidationException>(() => invSvc.CreateFromRentalAsync(rentalId));

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var invoiceCount = await db.Invoices.CountAsync(i => i.RentalId == rentalId);
        var cariDebit = (await db.AccountLedgerEntries
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.SourceType == "Fatura")
            .ToListAsync()).Sum(e => e.SignedBase);

        Assert.Equal(1, invoiceCount);     // tek fatura
        Assert.Equal(400m, cariDebit);     // tek borç
    }

    // ---- VECTOR 4: dönüşten SONRA ek hizmet eklenince double-count / base doğru mu ----
    [Fact]
    public async Task Addon_added_after_return_includes_return_charges_not_double()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // Uzatma olacak şekilde: 1 gün geç dönüş → +100 (GunlukUcret). KmLimit 0 → fazla km yok.
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var rentalSvc = scope.ServiceProvider.GetRequiredService<RentalService>();
        var addSvc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();

        var bitTar = (await GetRentalAsync(scope, rentalId)).BitTar;
        // 1 gün geç dönüş → uzatma 1 gün × 100 = 100. Baz brüt 400+100 = 500.
        await rentalSvc.ReturnAsync(rentalId, donusKm: 1000, donusYakit: 8, gercekDonus: bitTar.AddDays(1));
        var afterReturn = await GetRentalAsync(scope, rentalId);
        Assert.Equal(500m, afterReturn.GenelToplam); // 400 baz + 100 uzatma

        // Dönüşten SONRA ek hizmet (faturalanmadı → izinli). GPS net 100 brüt 120.
        await addSvc.AddAsync(rentalId, gpsId, miktar: 1m);
        var final = await GetRentalAsync(scope, rentalId);
        // Doğru: 500 (baz+uzatma) + 120 (ek hizmet) = 620. Çift sayım olursa 720 olur.
        Assert.Equal(620m, final.GenelToplam);
        Assert.Equal(620m, final.Bakiye);
    }

    // ---- VECTOR 1+9: ÇOK kalemli, ODD net → kuruş sızıntısı (net+kdv ?= gross) ----
    [Fact]
    public async Task Many_odd_addons_no_penny_leak_in_invoice()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // Tanım: birim 0.33 net %20, miktar 3 (net round(0.99)=0.99); ve 10.005 gibi.
        var (rentalId, _, _) = await SeedAsync(scope, tutar: 333.33m);
        var addSvc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        var invSvc = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        Guid oddTanim, oddTanim2;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var t1 = new EkHizmetTanim { Kod = "ODD1", Ad = "Odd1", BirimUcret = 0.33m, KdvOrani = 0.20m };
            var t2 = new EkHizmetTanim { Kod = "ODD2", Ad = "Odd2", BirimUcret = 3.337m, KdvOrani = 0.10m };
            db.EkHizmetTanimlari.Add(t1); db.EkHizmetTanimlari.Add(t2);
            await db.SaveChangesAsync();
            oddTanim = t1.Id; oddTanim2 = t2.Id;
        }

        // 7 adet odd kalem ekle (yuvarlama birikimi tetikle).
        for (int i = 0; i < 7; i++)
        {
            await addSvc.AddAsync(rentalId, oddTanim, miktar: 1m);
            await addSvc.AddAsync(rentalId, oddTanim2, miktar: 1.5m);
        }

        var invId = await invSvc.CreateFromRentalAsync(rentalId);
        var inv = await invSvc.GetAsync(invId);
        Assert.NotNull(inv);

        // Sızıntı kontrolü: NetTutar + KdvTutar == GenelToplam (kuruş tutarlı).
        Assert.Equal(inv!.GenelToplam, inv.NetTutar + inv.KdvTutar);

        // Defter dengesi.
        await using var db2 = await factory.CreateDbContextAsync();
        var entries = await db2.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Fatura" && e.SourceId == invId).ToListAsync();
        var borc = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var alacak = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(borc, alacak); // DENGE
        Assert.Equal(inv.GenelToplam, borc);

        // Satır toplamları = fatura toplamı (kalem-bazlı sızıntı yok).
        Assert.Equal(inv.NetTutar, inv.Lines.Sum(l => l.SatirNet));
        Assert.Equal(inv.KdvTutar, inv.Lines.Sum(l => l.SatirKdv));
        Assert.Equal(inv.GenelToplam, inv.Lines.Sum(l => l.SatirToplam));
    }

    // ---- VECTOR 8: negatif override ----
    [Fact]
    public async Task Negative_unit_override_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AddAsync(rentalId, gpsId, miktar: 1m, birimNetOverride: -100m));
    }

    [Fact]
    public async Task Kdv_rate_over_one_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AddAsync(rentalId, gpsId, miktar: 1m, kdvOraniOverride: 1.5m));
    }

    [Fact]
    public async Task Negative_quantity_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AddAsync(rentalId, gpsId, miktar: -3m));
    }

    // ---- VECTOR 6: tenant izolasyonu — başka tenant kira id'siyle ek hizmet ----
    [Fact]
    public async Task Cannot_add_addon_to_other_tenant_rental()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Guid rentalA, gpsB;
        using (var scopeA = host.ScopeFor(tenantA))
        {
            var (r, _, _) = await SeedAsync(scopeA);
            rentalA = r;
        }
        using (var scopeB = host.ScopeFor(tenantB))
        {
            var (_, g, _) = await SeedAsync(scopeB);
            gpsB = g;
        }

        // Tenant B, tenant A'nın kira id'sine kendi GPS tanımıyla ek hizmet eklemeye çalışır.
        using var scopeBAttack = host.ScopeFor(tenantB);
        var svc = scopeBAttack.ServiceProvider.GetRequiredService<RentalAddOnService>();
        // Kira A tenant B'de görünmemeli → "Kira bulunamadı" beklenir.
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.AddAsync(rentalA, gpsB, miktar: 1m));

        // Ve tenant A'nın kirasında hiç ek hizmet oluşmamalı.
        using var scopeAVerify = host.ScopeFor(tenantA);
        var svcA = scopeAVerify.ServiceProvider.GetRequiredService<RentalAddOnService>();
        Assert.Empty(await svcA.ListAsync(rentalA));
    }

    // ---- VECTOR 6: cross-tenant list sızıntısı ----
    [Fact]
    public async Task Cannot_list_other_tenant_addons()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Guid rentalA;
        using (var scopeA = host.ScopeFor(tenantA))
        {
            var (r, gps, _) = await SeedAsync(scopeA);
            var svcA = scopeA.ServiceProvider.GetRequiredService<RentalAddOnService>();
            await svcA.AddAsync(r, gps, miktar: 1m);
            rentalA = r;
        }

        using var scopeB = host.ScopeFor(tenantB);
        var svcB = scopeB.ServiceProvider.GetRequiredService<RentalAddOnService>();
        // Tenant B, A'nın kira id'siyle listeler → boş gelmeli (sızıntı yok).
        Assert.Empty(await svcB.ListAsync(rentalA));
    }

    // ---- VECTOR 3: işaret — ek hizmet ekleyince bakiye ARTAR, faturada cari borç pozitif ----
    [Fact]
    public async Task Addon_increases_balance_and_cari_debit_positive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (rentalId, gpsId, _) = await SeedAsync(scope);
        var addSvc = scope.ServiceProvider.GetRequiredService<RentalAddOnService>();
        var invSvc = scope.ServiceProvider.GetRequiredService<InvoiceService>();

        var before = (await GetRentalAsync(scope, rentalId)).Bakiye;
        await addSvc.AddAsync(rentalId, gpsId, miktar: 1m); // +120 brüt
        var after = (await GetRentalAsync(scope, rentalId)).Bakiye;
        Assert.True(after > before, "ek hizmet bakiyeyi ARTIRMALI");
        Assert.Equal(before + 120m, after);

        var invId = await invSvc.CreateFromRentalAsync(rentalId);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var cariSigned = (await db.AccountLedgerEntries
            .Where(e => e.AccountType == LedgerAccountType.Cari && e.SourceId == invId)
            .ToListAsync()).Sum(e => e.SignedBase);
        Assert.True(cariSigned > 0, "müşteri borçlu (pozitif) olmalı");
        Assert.Equal(520m, cariSigned); // 400 + 120
    }
}
