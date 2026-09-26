using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.DolulukFiyat;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A7 — Doluluk-bazlı fiyat çarpanı. BAĞIMSIZ ORACLE (elle): 10 EKO aracı × 5 günlük pencere =
/// 50 araç-gün; 8 araç tam (40) + 1 araç 2 gün (42) dolu → %84. Kural eşik 80 / çarpan 15 →
/// 1000 → 1150/gün (ResolveTierRate SONRASI, iskonto ÖNCESİ — %10 iskonto matrahı SURGE'LÜ bazdan:
/// 5750 → 5175). Eşik altı (%90 kural) bayt-özdeş 1000. Rezervasyon-UPDATE reprice'ında surge
/// ATLANIR (Karar (4) 2026-09-25: no-op düzenleme kilitli fiyatı korur; fiyat-etkileyen düzenleme
/// surge'süz yeniden fiyatlanır — müşteriye verilen fiyata düzenleme anındaki doluluk zam bindiremez). DB CHECK pantolon
/// askısı: SQL'le çarpan 70 yazılamaz (uygulama kemeri min(carpan,50) koda ek savunma).
/// </summary>
[Collection("postgres")]
public sealed class DolulukCarpaniTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(10);

    private static async Task<Guid> SeedFleetAsync(IServiceProvider sp) // 10 araç, 42/50 dolu → %84
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m,
            OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
        });
        var veh = sp.GetRequiredService<VehicleService>();
        var vehicles = new List<Guid>();
        for (var i = 1; i <= 10; i++)
            vehicles.Add(await veh.CreateAsync(new VehicleInput { Plaka = $"34 DK {i:00}", Grup = "EKO" }));
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Dolu", Soyad = "M" });
        var rentals = sp.GetRequiredService<RentalService>();
        for (var i = 0; i < 8; i++)   // 8 araç tam 5 gün = 40 araç-gün
            await rentals.CreateDirectAsync(new BookingInput
            { MusteriId = m, VehicleId = vehicles[i], BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 100m });
        await rentals.CreateDirectAsync(new BookingInput // 9. araç 2 gün = 2 → toplam 42
        { MusteriId = m, VehicleId = vehicles[8], BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m });
        return vehicles[9];            // 10. araç boş (rez/teklif testleri için)
    }

    private static Task<Guid> RuleAsync(IServiceProvider sp, int threshold, decimal multiplier) =>
        sp.GetRequiredService<OccupancyPriceRuleService>().CreateAsync(new DolulukFiyatKuralInput
        { Kod = $"D{threshold}", Ad = $"Doluluk {threshold}", EsikYuzde = threshold, CarpanYuzde = multiplier });

    [Fact]
    public async Task Esik_asiminda_carpan_iskonto_matrahi_surgelu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFleetAsync(sp);
        await RuleAsync(sp, threshold: 80, multiplier: 15m);

        // %84 ≥ %80 → 1000 → 1150 (elle); 5 gün baz 5750.
        var engine = sp.GetRequiredService<RentalQuoteEngine>();
        var q = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1150m, q.GunlukUcret);
        Assert.Equal(5750m, q.BazTutar);
        Assert.Contains(q.Notlar, n => n.Contains("Doluluk %84") && n.Contains("+%15"));

        // İskonto matrahı SURGE'LÜ baz: %10 kural → 5750 × 0.90 = 5175 (elle).
        await sp.GetRequiredService<RentACar.Application.RentalRules.RentalRuleService>()
            .CreateAsync(new RentACar.Application.RentalRules.RentalRuleInput { Kod = "GENEL", Ad = "G", Iskonto = 10m });
        var q2 = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(575m, q2.IskontoTutar);
        Assert.Equal(5175m, q2.GenelToplam);
    }

    [Fact]
    public async Task Esik_alti_bayt_ozdes_ve_kural_yoksa_kisa_devre()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedFleetAsync(sp);
        var engine = sp.GetRequiredService<RentalQuoteEngine>();

        // Hiç kural yok → kısa devre (doluluk sorgusu yok) → 1000.
        var q0 = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1000m, q0.GunlukUcret);

        // Eşik %90 kural: %84 altında kalır → bayt-özdeş 1000, not yok.
        await RuleAsync(sp, threshold: 90, multiplier: 15m);
        var q1 = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1000m, q1.GunlukUcret);
        Assert.DoesNotContain(q1.Notlar, n => n.Contains("Doluluk"));
    }

    [Fact]
    public async Task Rez_update_fiyat_taahhudu_korunur_girdi_degisince_reprice() // adversarial A7-B4
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var idleVehicle = await SeedFleetAsync(sp);
        await RuleAsync(sp, threshold: 80, multiplier: 15m);
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Rez", Soyad = "M" });
        var res = sp.GetRequiredService<ReservationService>();

        BookingInput Input(int day, string? description = null) => new()
        {
            MusteriId = m, VehicleId = idleVehicle, BasTar = Start, BitTar = Start.AddDays(day),
            FiyatTuru = "Otomatik", Aciklama = description
        };

        // Create'te surge UYGULANIR (fiyat o an kilitlenir): 1150×5 = 5750.
        var id = await res.CreateAsync(Input(5));
        var r0 = (await res.GetAsync(id))!;
        Assert.Equal(5750m, r0.Tutar);

        // NOT/no-op düzenlemesi fiyatı SESSİZCE DÜŞÜREMEZ (düzeltme öncesi surge'süz 5000'e iniyordu):
        // fiyat-etkileyen girdiler (form prefill'li ücret dahil) aynı → reprice atlanır → 5750 korunur.
        await res.UpdateAsync(id, new BookingInput
        {
            MusteriId = m, VehicleId = idleVehicle, BasTar = Start, BitTar = Start.AddDays(5),
            FiyatTuru = "Otomatik", GunlukUcret = r0.GunlukUcret, Aciklama = "not düzeltmesi"
        });
        Assert.Equal(5750m, (await res.GetAsync(id))!.Tutar);

        // Fiyat-etkileyen girdi (tarih) değişince reprice — ama SURGE'SÜZ (Karar (4), 2026-09-25):
        // 3 günlük pencere doluluğu 8×3+2=26 / 30 → %86,67 ≥ 80 olsa da çarpan uygulanmaz → 1000×3 = 3000 (elle).
        await res.UpdateAsync(id, Input(3));
        Assert.Equal(3000m, (await res.GetAsync(id))!.Tutar);
    }

    [Fact]
    public async Task Rez_update_sonradan_eklenen_kurala_ragmen_surge_uygulamaz() // Karar (4)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var idleVehicle = await SeedFleetAsync(sp);
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Rez", Soyad = "K" });
        var res = sp.GetRequiredService<ReservationService>();

        BookingInput Input(int day) => new()
        { MusteriId = m, VehicleId = idleVehicle, BasTar = Start, BitTar = Start.AddDays(day), FiyatTuru = "Otomatik" };

        // Kural YOKKEN açılan rezervasyon: 1000×5 = 5000 (elle).
        var id = await res.CreateAsync(Input(5));
        Assert.Equal(5000m, (await res.GetAsync(id))!.Tutar);

        // Sonra uygulanabilir surge kuralı eklenir (eşik 80, +%15) — motor artık yeni teklifte surge'lü fiyat verir:
        // 4 günlük pencere doluluğu 8×4+2=34 / 40 → %85 ≥ 80 → 1150/gün (elle).
        await RuleAsync(sp, threshold: 80, multiplier: 15m);
        var q = await sp.GetRequiredService<RentalQuoteEngine>()
            .QuoteAsync(new QuoteRequest { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(4) });
        Assert.Equal(1150m, q.GunlukUcret);

        // Rezervasyon tarihi düzenlenir (aynı sınıf) → reprice surge'süz: 1000×4 = 4000 (elle), 4600 DEĞİL.
        await res.UpdateAsync(id, Input(4));
        var r = (await res.GetAsync(id))!;
        Assert.Equal(4000m, r.Tutar);
        Assert.Equal(1000m, r.GunlukUcret);
    }

    [Fact]
    public async Task Payda_kiralanabilir_filo_ve_rezervasyon_sinyali() // adversarial B1+B3+B5
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "E", GunlukKmLimiti = 300, AsimKmUcreti = 5m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m,
            OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
        });
        await RuleAsync(sp, threshold: 60, multiplier: 20m);
        var veh = sp.GetRequiredService<VehicleService>();

        // B1: 4 araçtan 2'si SATILMIŞ — payda kiralanabilir filodur (2). 2 aktif araç tam rezerve →
        // %100 ≥ 60 → surge (düzeltme öncesi payda 4 → %50 → surge SESSİZCE ölürdü).
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 DP 01", Grup = "EKO" });
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 DP 02", Grup = "EKO" });
        await veh.CreateAsync(new VehicleInput { Plaka = "34 DP 03", Grup = "EKO", Durum = VehicleStatus.Satildi });
        await veh.CreateAsync(new VehicleInput { Plaka = "34 DP 04", Grup = "EKO", Durum = VehicleStatus.Pasif });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "P", Soyad = "M" });

        // B3: doluluk sinyali REZERVASYONLARDAN da gelir (ileri tarihli talebin ana kaynağı) —
        // düzeltme öncesi tam rezerve grup %0 görünüyordu.
        var res = sp.GetRequiredService<ReservationService>();
        await res.CreateAsync(new BookingInput { MusteriId = m, VehicleId = v1, BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 100m });
        await res.CreateAsync(new BookingInput { MusteriId = m, VehicleId = v2, BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 100m });

        var engine = sp.GetRequiredService<RentalQuoteEngine>();
        var q = await engine.QuoteAsync(new QuoteRequest { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1200m, q.GunlukUcret);   // 1000 × 1.20 (elle)

        // B5: aynı-takvim-günü penceresi 1 gün sayılır (gece-yarısı atlatması yok) — %100 → surge.
        var q2 = await engine.QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Start.AddHours(10), BitTar = Start.AddHours(18) });
        Assert.Equal(1200m, q2.GunlukUcret);
    }

    [Fact]
    public async Task Erken_donus_hayalet_doluluk_uretmez() // adversarial B2
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "E", GunlukKmLimiti = 300, AsimKmUcreti = 5m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m,
            OnayDurumu = TariffApprovalStatus.Onayli, Onaylayan = "t"
        });
        await RuleAsync(sp, threshold: 40, multiplier: 15m);
        var veh = sp.GetRequiredService<VehicleService>();
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 DE 01", Grup = "EKO" });
        await veh.CreateAsync(new VehicleInput { Plaka = "34 DE 02", Grup = "EKO" });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "E", Soyad = "M" });
        var rentals = sp.GetRequiredService<RentalService>();

        // 5 günlük kira ANINDA erken dönüşle kapanır (efektif bitiş = başlangıç günü) — planlı BitTar
        // hayalet doluluk üretmez: %0 → surge YOK (düzeltme öncesi %50 ≥ 40 → yanlış +%15).
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v1, BasTar = Start, BitTar = Start.AddDays(5), GunlukUcret = 100m });
        await rentals.DeliverAsync(id, pickupKm: 0, pickupFuel: 8);
        await rentals.ReturnAsync(id, returnKm: 0, returnFuel: 8, Start);

        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Start, BitTar = Start.AddDays(5) });
        Assert.Equal(1000m, q.GunlukUcret);
        Assert.DoesNotContain(q.Notlar, n => n.Contains("Doluluk"));
    }

    [Fact]
    public async Task Db_check_pantolon_askisi_70_carpani_reddeder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Servis katmanı 0..50 doğrular (kemer #1).
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => sp.GetRequiredService<OccupancyPriceRuleService>().CreateAsync(new DolulukFiyatKuralInput
            { Kod = "D99", Ad = "Aşırı", EsikYuzde = 80, CarpanYuzde = 70m }));

        // Servis atlanıp repo'dan yazılsa bile DB CHECK reddeder (pantolon askısı; şemadaki ilk CHECK).
        var id = await RuleAsync(sp, threshold: 80, multiplier: 15m);
        var repo = sp.GetRequiredService<IOccupancyPriceRuleRepository>();
        await Assert.ThrowsAsync<DbUpdateException>(
            () => repo.UpdateAsync(id, k => k.CarpanYuzde = 70m));
        // (Uygulama kemeri min(carpan,50) motorda ek savunma olarak durur — DB'ye 50 üstü giremediğinden
        // normal akışta erişilmez.)
    }
}
