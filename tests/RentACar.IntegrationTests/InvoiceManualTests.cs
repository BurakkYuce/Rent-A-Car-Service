using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G2 — manuel/serbest fatura (PARA) + araç satış derinlik. BAĞIMSIZ ORACLE: manuel fatura DENGELİ
/// defter (Borç Cari brüt / Alacak Gelir net + KDV), cari +1200/gelir 1000/kdv 200; dönem-kilidi; idempotency;
/// VehicleSale additive roundtrip.
/// </summary>
[Collection("postgres")]
public sealed class InvoiceManualTests(PostgresFixture fx)
{
    private static async Task<Guid> Account(IServiceProvider sp)
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Manuel Cari" });

    [Fact]
    public async Task Manual_invoice_posts_balanced_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await Account(sp);

        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m, Aciklama = "Danışmanlık" });

        // Oracle: net 1000 + kdv 200 = brüt 1200; cari BORÇLANIR (+1200); gelir 1000; kdv tahsil 200.
        Assert.Equal(1200m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(customerId));
        var gg = await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync();
        Assert.Equal(1000m, gg.GelirToplam);
        Assert.Equal(200m, gg.KdvTahsil);
    }

    [Fact]
    public async Task Manual_invoice_blocked_by_period_lock()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await Account(sp);
        await sp.GetRequiredService<PeriodLockService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput { CariId = customerId, NetTutar = 500m }));
    }

    [Fact]
    public async Task Manual_invoice_idempotent_with_key()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await Account(sp);
        var key = Guid.NewGuid();
        var svc = sp.GetRequiredService<InvoiceService>();

        var id1 = await svc.CreateManualAsync(new ManualInvoiceInput { CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = key });
        var id2 = await svc.CreateManualAsync(new ManualInvoiceInput { CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = key });

        Assert.Equal(id1, id2);                 // çift-submit aynı fatura
        Assert.Equal(1200m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(customerId)); // tek borç (çiftlenmedi)
    }

    /// <summary>FAZ-51(a) — bilgi alanları round-trip. BAĞIMSIZ ORACLE: elle girilen 6 metin değeri
    /// aynen okunur; para toplamlarına/deftere DOKUNMAZ (mevcut oracle: net 1000 + kdv 200 = 1200).</summary>
    [Fact]
    public async Task Manual_invoice_bilgi_alanlari_roundtrip_without_touching_ledger()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await Account(sp);
        var svc = sp.GetRequiredService<InvoiceService>();

        var invId = await svc.CreateManualAsync(new ManualInvoiceInput
        {
            CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m, Aciklama = "Danışmanlık",
            IslemSube = "Merkez", EvrakNo = "EVR-2026-001", FaturaOzelKod = "OZK-7",
            OdemeTuru = "Havale", GonderimSekli = "Mail", KdvSifirSebep = "11/1-A Hizmet İhracatı"
        });
        var inv = await svc.GetAsync(invId);

        Assert.Equal("Merkez", inv!.IslemSube);
        Assert.Equal("EVR-2026-001", inv.EvrakNo);
        Assert.Equal("OZK-7", inv.FaturaOzelKod);
        Assert.Equal("Havale", inv.OdemeTuru);
        Assert.Equal("Mail", inv.GonderimSekli);
        Assert.Equal("11/1-A Hizmet İhracatı", inv.KdvSifirSebep);

        // Oracle: net 1000 + kdv 200 = brüt 1200; bilgi alanları TOPLAMLARI DEĞİŞTİRMEDİ.
        Assert.Equal(1000m, inv.NetTutar);
        Assert.Equal(200m, inv.KdvTutar);
        Assert.Equal(1200m, inv.GenelToplam);
        Assert.Equal(1200m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(customerId));
    }

    /// <summary>FAZ-51(b) — ÖTV/tevkifat/damga UÇUK değerlerle doldurulur (tevkifat %90, damga 9999,
    /// ÖTV 12345.67); BAĞIMSIZ ORACLE: bu değerler entity'de saklanır AMA defter/cari-bakiye
    /// BİT-BİREBİR AYNI kalır — kırılgan regresyon testi (KARARLAR.md FAZ-51: fatura üzerinde
    /// bilgi kalır, deftere ayrı satır yazılmaz).</summary>
    [Fact]
    public async Task Manual_invoice_extreme_vergi_alanlari_do_not_alter_ledger_or_balance()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await Account(sp);
        var svc = sp.GetRequiredService<InvoiceService>();
        var cash = sp.GetRequiredService<CashService>();

        // Baseline: vergi alanı OLMADAN aynı tutarda bir fatura (karşılaştırma referansı).
        var baselineId = await svc.CreateManualAsync(new ManualInvoiceInput
        { CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m });
        var baselineBalance = await cash.GetAccountBalanceAsync(customerId);

        var extremeId = await svc.CreateManualAsync(new ManualInvoiceInput
        {
            CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m,
            Vergi = new InvoiceTaxInfo(Otv: 12345.67m, TevkifatOran: 90m, TevkifatTutar: 9000m, DamgaVergisi: 9999m)
        });
        var extreme = await svc.GetAsync(extremeId);

        // Uçuk değerler AYNEN saklandı (bağımsız oracle: elle girilen sabitler).
        Assert.Equal(12345.67m, extreme!.Otv);
        Assert.Equal(90m, extreme.TevkifatOran);
        Assert.Equal(9000m, extreme.TevkifatTutar);
        Assert.Equal(9999m, extreme.DamgaVergisi);
        Assert.True(extreme.ManuelMi);
        Assert.False(extreme.IadeMi); // form ManuelMi/IadeMi'yi taşımaz — servis invariant'ı

        // KRİTİK: fatura toplamları/cari bakiye UÇUK vergi değerlerinden BAĞIMSIZ (baseline ile bit-birebir
        // aynı artış) — deftere sızma YOK.
        Assert.Equal(1000m, extreme.NetTutar);
        Assert.Equal(200m, extreme.KdvTutar);
        Assert.Equal(1200m, extreme.GenelToplam);
        var afterBalance = await cash.GetAccountBalanceAsync(customerId);
        Assert.Equal(baselineBalance + 1200m, afterBalance); // ikinci fatura da tam olarak +1200 ekledi, ne fazla ne eksik

        // Defter satır SAYISI da sabit: her manuel fatura TAM 3 kayıt (Cari/Gelir/Kdv) — vergi alanları
        // için EK satır YOK (D5 kararı (ii): ayrı ledger satırı açılmadı).
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var entriesForExtreme = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == "Fatura" && e.SourceId == extremeId).ToListAsync();
        Assert.Equal(3, entriesForExtreme.Count);
        // Toplam defter tutarı (base): Σ Borç == Σ Alacak — dengesizlik varsa PostAsync zaten reddederdi,
        // ama açık doğrulama adversarial denemenin kalıcı izidir.
        var debit = entriesForExtreme.Where(e => e.Direction == RentACar.Domain.Entities.LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
        var credit = entriesForExtreme.Where(e => e.Direction == RentACar.Domain.Entities.LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
        Assert.Equal(debit, credit);
        Assert.Equal(1200m, debit);
    }

    /// <summary>Adversarial: ApplyVergi'nin negatif/aralık-dışı guard'ı manuel yolda da GEÇERLİ —
    /// yalnız kira-fatura yolunda test edilmişti, yeni bağlanan manuel yolda es geçilmediğini kanıtlar.</summary>
    [Theory]
    [InlineData(-1d, null, null, null)]      // ÖTV negatif
    [InlineData(null, 150d, null, null)]     // Tevkifat oranı > 100
    [InlineData(null, null, -5d, null)]      // Tevkifat tutarı negatif
    [InlineData(null, null, null, -0.01d)]   // Damga negatif
    public async Task Manual_invoice_rejects_invalid_vergi_values(
        double? otv, double? withholdingRate, double? withholdingAmount, double? stamp)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var customerId = await Account(sp);
        var svc = sp.GetRequiredService<InvoiceService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateManualAsync(new ManualInvoiceInput
        {
            CariId = customerId, NetTutar = 1000m, KdvOrani = 0.20m,
            Vergi = new InvoiceTaxInfo(
                Otv: (decimal?)otv, TevkifatOran: (decimal?)withholdingRate,
                TevkifatTutar: (decimal?)withholdingAmount, DamgaVergisi: (decimal?)stamp)
        }));

        // Red edilen istek CARİ BAKİYEYİ DEĞİŞTİRMEDİ (yarım/kirli fatura yazılmadı).
        Assert.Equal(0m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(customerId));
    }

    [Fact]
    public async Task VehicleSale_depth_fields_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vId = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 G2 01" });
        var sales = sp.GetRequiredService<VehicleSaleService>();

        var id = await sales.CreateAsync(new VehicleSaleInput
        {
            VehicleId = vId, AliciCariId = await Account(sp), SatisNet = 5000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m,
            HedefFiyat = 5500m, SatisKm = 120000, SatisKanali = "Galeri", Devir = "Noter"
        });
        var s = await sales.GetAsync(id);
        Assert.Equal(5500m, s!.HedefFiyat);
        Assert.Equal(120000, s.SatisKm);
        Assert.Equal("Galeri", s.SatisKanali);
        Assert.Equal("Noter", s.Devir);
    }
}
