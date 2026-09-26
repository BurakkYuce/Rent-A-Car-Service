using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ4-4.2-B2 — Dönem faturası kesimi. BAĞIMSIZ ORACLE (elle): 15 Oca 2027 + 90g × 100 = 9000;
/// dönem tahakkukları 3100/2800/3100 (31/28/31 gün, son dönem kalan-yöntemi). Kesimler fark
/// mekanizmasından (KaynakKiraId + sıra unique — çift yapısal imkânsız): D1=3100 (net 2583,33 +
/// KDV 516,67), D2=2800, D3=3100 → Σ == GenelToplam KURUŞ-BİREBİR; sonra normal fatura "tam
/// faturalanmış" reddi. İdempotent (Kesildi → aynı InvoiceId); SIRALI kesim; kompozisyon: 2 dönem
/// + dönüş fazla-km 1500 → D3 3100 + normal fark 1500; cap: tam-faturalı kirada dönem → ATLANDI +
/// gürültülü red; net-mod snapshot KDV'si; İptal red.
/// </summary>
[Collection("postgres")]
public sealed class DonemFaturaKesTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2027, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> RentalAsync(IServiceProvider sp, string plate,
        DateTimeOffset start, DateTimeOffset bit, string? priceType = null,
        int kmLimit = 0, decimal excessKmFee = 0m)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "DF", Soyad = "M" });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = start, BitTar = bit, GunlukUcret = 100m,
            FiyatTuru = priceType, KmLimit = kmLimit, FazlaKmUcret = excessKmFee
        });
    }

    [Fact]
    public async Task Uc_donem_kesimi_kurus_birebir_ve_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var id = await RentalAsync(sp, "34 DF 01", Start, Start.AddDays(90));
        var invoices = sp.GetRequiredService<InvoiceService>();
        var repo = sp.GetRequiredService<IInvoiceRepository>();

        // Sıra dışı kesim reddi: önce D1.
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreatePeriodInvoiceAsync(id, 2));

        // D1: 3100 brüt → 2583,33 net + 516,67 KDV (elle, %20).
        var f1 = await invoices.CreatePeriodInvoiceAsync(id, 1);
        var inv1 = (await repo.FindAsync(f1))!;
        Assert.Equal(3100m, inv1.GenelToplam);
        Assert.Equal(2583.33m, inv1.NetTutar);
        Assert.Equal(516.67m, inv1.KdvTutar);
        Assert.Contains("Dönem 1", inv1.Lines.Count > 0 ? inv1.Lines[0].Aciklama : "");

        // İdempotent: aynı dönem tekrar → AYNI fatura Id (yeni fatura yok).
        Assert.Equal(f1, await invoices.CreatePeriodInvoiceAsync(id, 1));

        // D2 + D3 → Σ = 9000 kuruş-birebir; dönem satırları Kesildi + iz.
        var f2 = await invoices.CreatePeriodInvoiceAsync(id, 2);
        var f3 = await invoices.CreatePeriodInvoiceAsync(id, 3);
        Assert.Equal(2800m, (await repo.FindAsync(f2))!.GenelToplam);
        Assert.Equal(3100m, (await repo.FindAsync(f3))!.GenelToplam);
        var periods = await sp.GetRequiredService<IInvoicePeriodRepository>().ListForRentalAsync(id);
        Assert.All(periods, d => Assert.Equal(InvoicePeriodStatus.Kesildi, d.Durum));
        Assert.Equal(9000m, periods.Sum(d => d.KesilenTutar ?? 0m));

        // Tam faturalandı: normal fatura kesimi temiz red (fark 0).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(id));
        Assert.Contains("tam faturalanmış", ex.Message);
    }

    [Fact]
    public async Task Donus_ucreti_kompozisyonu_son_delta_normal_farkla()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // Geçmişte başlayan 90 günlük kira (dönüş yapılabilsin); toplam limit 9000 km,
        // dönüş 9750 → aşım 750 × 2 = 1500 (elle).
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-90);
        var id = await RentalAsync(sp, "34 DF 02", start, start.AddDays(90), kmLimit: 9000, excessKmFee: 2m);
        var invoices = sp.GetRequiredService<InvoiceService>();
        var rentals = sp.GetRequiredService<RentalService>();

        var repo = sp.GetRequiredService<IInvoiceRepository>();
        var f1 = await invoices.CreatePeriodInvoiceAsync(id, 1);
        var f2 = await invoices.CreatePeriodInvoiceAsync(id, 2);
        var issued12 = (await repo.FindAsync(f1))!.GenelToplam + (await repo.FindAsync(f2))!.GenelToplam;

        // Dönüş: toplam limit 9000; 9750 km → 750 aşım × 2 = 1500 (elle).
        await rentals.DeliverAsync(id, pickupKm: 0, pickupFuel: 8);
        await rentals.ReturnAsync(id, returnKm: 9750, returnFuel: 8, start.AddDays(90));
        Assert.Equal(10500m, (await rentals.GetAsync(id))!.GenelToplam);

        // D3: tahakkuk cap'i BAZ brütten (9000) — dönüş ücreti dönem tahakkukuna GİRMEZ.
        // (Dönem günleri now-göreli aylara bağlı → D3 = 9000 − D1 − D2 kalan-yöntemi invaryantı.)
        var f3 = await invoices.CreatePeriodInvoiceAsync(id, 3);
        Assert.Equal(9000m - issued12, (await repo.FindAsync(f3))!.GenelToplam);

        // Kalan delta (dönüş bedeli 1500) NORMAL "Fatura Kes" ile — kompozisyon bedava.
        var difference = await invoices.CreateFromRentalAsync(id);
        Assert.Equal(1500m, (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(difference))!.GenelToplam);
    }

    [Fact]
    public async Task Cap_tam_faturali_kirada_donem_atlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var id = await RentalAsync(sp, "34 DF 03", Start, Start.AddDays(90));
        var invoices = sp.GetRequiredService<InvoiceService>();

        // Önce NORMAL tam fatura (9000) kesilir → dönem tahakkuku kalmaz.
        await invoices.CreateFromRentalAsync(id);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => invoices.CreatePeriodInvoiceAsync(id, 1));
        Assert.Contains("ATLANDI", ex.Message);

        // Kalıcı iz + ikinci deneme "atlanmış" reddi (sessiz tekrar yok).
        var d1 = (await sp.GetRequiredService<IInvoicePeriodRepository>().ListForRentalAsync(id))
            .Single(d => d.DonemSira == 1);
        Assert.Equal(InvoicePeriodStatus.Atlandi, d1.Durum);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => invoices.CreatePeriodInvoiceAsync(id, 1));
        Assert.Contains("atlanmış", ex2.Message);
    }

    [Fact]
    public async Task Paralel_base_ve_donem_kesimi_cift_faturalayamaz() // adversarial B2 KRİTİK-1 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var invoices = sp.GetRequiredService<InvoiceService>();
        var repo = sp.GetRequiredService<IInvoiceRepository>();

        // Düzeltme öncesi: base (TAM 9000) ile dönem (3100) farklı unique-index'lere yazdığından
        // paralel koşuda İKİSİ DE commit ediyordu → faturalanan 12.100 > GenelToplam (çift faturalama).
        // Artık advisory kira-fatura kilidi + TX-içi yeniden doğrulama tek kazanan bırakır.
        for (var i = 0; i < 5; i++)
        {
            var id = await RentalAsync(sp, $"34 DF 1{i}", Start, Start.AddDays(90));
            var baseInvoice = Task.Run(async () =>
            { try { await invoices.CreateFromRentalAsync(id); } catch (ValidationException) { } });
            var period = Task.Run(async () =>
            { try { await invoices.CreatePeriodInvoiceAsync(id, 1); } catch (ValidationException) { } });
            await Task.WhenAll(baseInvoice, period);

            var (invoiced, _) = await repo.GetDifferenceStateAsync(id);
            Assert.True(invoiced is 3100m or 9000m,
                $"iterasyon {i}: faturalanan {invoiced} — çift faturalama ya da sıfır kesim");
        }
    }

    [Fact]
    public async Task Iptal_kiraya_normal_fatura_da_kesilemez() // adversarial B2 Orta-2 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var id = await RentalAsync(sp, "34 DF 20", Start, Start.AddDays(90));
        await sp.GetRequiredService<RentalService>().CancelAsync(id);
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id));
        Assert.Contains("İptal", ex.Message);
    }

    [Fact]
    public async Task Net_mod_snapshot_kdv_ve_iptal_reddi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.VarsayilanKdvOrani = 0.10m);

        // "Günlük" NET 100 → brüt 110 (tenant %10, snapshot yazılır) × 90g = 9900; D1 tahakkuk
        // 9900×31/90 = 3410,00 → SNAPSHOT %10'dan ayrışır: net 3100,00 + KDV 310,00 (elle).
        var id = await RentalAsync(sp, "34 DF 04", Start, Start.AddDays(90), priceType: "Günlük");
        var invoices = sp.GetRequiredService<InvoiceService>();
        var f1 = await invoices.CreatePeriodInvoiceAsync(id, 1);
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(f1))!;
        Assert.Equal(3410.00m, inv.GenelToplam);
        Assert.Equal(3100.00m, inv.NetTutar);
        Assert.Equal(310.00m, inv.KdvTutar);

        // Net-modda farklı oran parametresi → guard reddi (CreateFromRentalAsync ile birebir).
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreatePeriodInvoiceAsync(id, 2, vatRate: 0.20m));

        // İptal kira reddi.
        var cancel = await RentalAsync(sp, "34 DF 05", Start, Start.AddDays(90));
        await sp.GetRequiredService<RentalService>().CancelAsync(cancel);
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreatePeriodInvoiceAsync(cancel, 1));
    }
}
