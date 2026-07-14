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
    private static readonly DateTimeOffset Bas = new(2027, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> KiraAsync(IServiceProvider sp, string plaka,
        DateTimeOffset bas, DateTimeOffset bit, string? fiyatTuru = null,
        int kmLimit = 0, decimal fazlaKmUcret = 0m)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CariType.Bireysel, Ad = "DF", Soyad = "M" });
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bit, GunlukUcret = 100m,
            FiyatTuru = fiyatTuru, KmLimit = kmLimit, FazlaKmUcret = fazlaKmUcret
        });
    }

    [Fact]
    public async Task Uc_donem_kesimi_kurus_birebir_ve_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var id = await KiraAsync(sp, "34 DF 01", Bas, Bas.AddDays(90));
        var invoices = sp.GetRequiredService<InvoiceService>();
        var repo = sp.GetRequiredService<IInvoiceRepository>();

        // Sıra dışı kesim reddi: önce D1.
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateDonemFaturasiAsync(id, 2));

        // D1: 3100 brüt → 2583,33 net + 516,67 KDV (elle, %20).
        var f1 = await invoices.CreateDonemFaturasiAsync(id, 1);
        var inv1 = (await repo.FindAsync(f1))!;
        Assert.Equal(3100m, inv1.GenelToplam);
        Assert.Equal(2583.33m, inv1.NetTutar);
        Assert.Equal(516.67m, inv1.KdvTutar);
        Assert.Contains("Dönem 1", inv1.Lines.Count > 0 ? inv1.Lines[0].Aciklama : "");

        // İdempotent: aynı dönem tekrar → AYNI fatura Id (yeni fatura yok).
        Assert.Equal(f1, await invoices.CreateDonemFaturasiAsync(id, 1));

        // D2 + D3 → Σ = 9000 kuruş-birebir; dönem satırları Kesildi + iz.
        var f2 = await invoices.CreateDonemFaturasiAsync(id, 2);
        var f3 = await invoices.CreateDonemFaturasiAsync(id, 3);
        Assert.Equal(2800m, (await repo.FindAsync(f2))!.GenelToplam);
        Assert.Equal(3100m, (await repo.FindAsync(f3))!.GenelToplam);
        var donemler = await sp.GetRequiredService<IFaturaDonemRepository>().ListForRentalAsync(id);
        Assert.All(donemler, d => Assert.Equal(FaturaDonemDurum.Kesildi, d.Durum));
        Assert.Equal(9000m, donemler.Sum(d => d.KesilenTutar ?? 0m));

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
        var bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-90);
        var id = await KiraAsync(sp, "34 DF 02", bas, bas.AddDays(90), kmLimit: 9000, fazlaKmUcret: 2m);
        var invoices = sp.GetRequiredService<InvoiceService>();
        var rentals = sp.GetRequiredService<RentalService>();

        var repo = sp.GetRequiredService<IInvoiceRepository>();
        var f1 = await invoices.CreateDonemFaturasiAsync(id, 1);
        var f2 = await invoices.CreateDonemFaturasiAsync(id, 2);
        var kesilen12 = (await repo.FindAsync(f1))!.GenelToplam + (await repo.FindAsync(f2))!.GenelToplam;

        // Dönüş: toplam limit 9000; 9750 km → 750 aşım × 2 = 1500 (elle).
        await rentals.DeliverAsync(id, cikisKm: 0, cikisYakit: 8);
        await rentals.ReturnAsync(id, donusKm: 9750, donusYakit: 8, bas.AddDays(90));
        Assert.Equal(10500m, (await rentals.GetAsync(id))!.GenelToplam);

        // D3: tahakkuk cap'i BAZ brütten (9000) — dönüş ücreti dönem tahakkukuna GİRMEZ.
        // (Dönem günleri now-göreli aylara bağlı → D3 = 9000 − D1 − D2 kalan-yöntemi invaryantı.)
        var f3 = await invoices.CreateDonemFaturasiAsync(id, 3);
        Assert.Equal(9000m - kesilen12, (await repo.FindAsync(f3))!.GenelToplam);

        // Kalan delta (dönüş bedeli 1500) NORMAL "Fatura Kes" ile — kompozisyon bedava.
        var fark = await invoices.CreateFromRentalAsync(id);
        Assert.Equal(1500m, (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(fark))!.GenelToplam);
    }

    [Fact]
    public async Task Cap_tam_faturali_kirada_donem_atlanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var id = await KiraAsync(sp, "34 DF 03", Bas, Bas.AddDays(90));
        var invoices = sp.GetRequiredService<InvoiceService>();

        // Önce NORMAL tam fatura (9000) kesilir → dönem tahakkuku kalmaz.
        await invoices.CreateFromRentalAsync(id);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateDonemFaturasiAsync(id, 1));
        Assert.Contains("ATLANDI", ex.Message);

        // Kalıcı iz + ikinci deneme "atlanmış" reddi (sessiz tekrar yok).
        var d1 = (await sp.GetRequiredService<IFaturaDonemRepository>().ListForRentalAsync(id))
            .Single(d => d.DonemSira == 1);
        Assert.Equal(FaturaDonemDurum.Atlandi, d1.Durum);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateDonemFaturasiAsync(id, 1));
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
            var id = await KiraAsync(sp, $"34 DF 1{i}", Bas, Bas.AddDays(90));
            var basefat = Task.Run(async () =>
            { try { await invoices.CreateFromRentalAsync(id); } catch (ValidationException) { } });
            var donem = Task.Run(async () =>
            { try { await invoices.CreateDonemFaturasiAsync(id, 1); } catch (ValidationException) { } });
            await Task.WhenAll(basefat, donem);

            var (faturalanan, _) = await repo.GetFarkStateAsync(id);
            Assert.True(faturalanan is 3100m or 9000m,
                $"iterasyon {i}: faturalanan {faturalanan} — çift faturalama ya da sıfır kesim");
        }
    }

    [Fact]
    public async Task Iptal_kiraya_normal_fatura_da_kesilemez() // adversarial B2 Orta-2 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var id = await KiraAsync(sp, "34 DF 20", Bas, Bas.AddDays(90));
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
        var id = await KiraAsync(sp, "34 DF 04", Bas, Bas.AddDays(90), fiyatTuru: "Günlük");
        var invoices = sp.GetRequiredService<InvoiceService>();
        var f1 = await invoices.CreateDonemFaturasiAsync(id, 1);
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(f1))!;
        Assert.Equal(3410.00m, inv.GenelToplam);
        Assert.Equal(3100.00m, inv.NetTutar);
        Assert.Equal(310.00m, inv.KdvTutar);

        // Net-modda farklı oran parametresi → guard reddi (CreateFromRentalAsync ile birebir).
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateDonemFaturasiAsync(id, 2, kdvRate: 0.20m));

        // İptal kira reddi.
        var iptal = await KiraAsync(sp, "34 DF 05", Bas, Bas.AddDays(90));
        await sp.GetRequiredService<RentalService>().CancelAsync(iptal);
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateDonemFaturasiAsync(iptal, 1));
    }
}
