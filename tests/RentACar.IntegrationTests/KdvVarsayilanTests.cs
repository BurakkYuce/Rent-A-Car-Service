using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A6 — Tenant varsayılan KDV bağlama. Oran zinciri: kdvRate ?? OzelKdvOran ?? (net-modda
/// KdvOranSnapshot) ?? TenantSettings.VarsayilanKdvOrani ?? 0.20. BAĞIMSIZ ORACLE (elle): tenant
/// %10, "Günlük" NET 100 × 3g → gross-up 110 → Tutar 330; fatura SNAPSHOT %10'dan ayrışır →
/// 300 net / 30 KDV — tenant oranı SONRADAN %20'ye çekilse bile (snapshot tutarlılık çiti; eski
/// sabit-0.20 guard'ı bozulurdu). Brüt kirada parametresiz fatura tenant oranından ayrışır:
/// 360 brüt @ %10 → 327,27/32,73. Özel oran zinciri üstün kalır. Net-mod çiti geçerli varsayılanla
/// karşılaştırır (%10 tenant'ta OzelKdv %10 çelişki DEĞİL, %18 red). AYAR YOKKEN davranış eski
/// paketle bayt-özdeş (tam paket regresyon kapısı).
/// </summary>
[Collection("postgres")]
public sealed class KdvVarsayilanTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static Task TenantKdvAsync(IServiceProvider sp, decimal? oran) =>
        sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(s => s.VarsayilanKdvOrani = oran);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Kdv", Soyad = "M" });
        return (m, v);
    }

    [Fact]
    public async Task Net_mod_tenant_oranindan_grossup_ve_snapshot_fatura()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await TenantKdvAsync(sp, 0.10m);
        var (m, v) = await SeedAsync(sp, "34 KV 01");
        var rentals = sp.GetRequiredService<RentalService>();

        // "Günlük" NET 100 → brüt 110 (tenant %10) × 3g = 330 (elle).
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, FiyatTuru = "Günlük" });
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(330m, c.Tutar);
        Assert.Equal(0.10m, c.KdvOranSnapshot);

        // Tenant oranı SONRADAN değişir → fatura yine SNAPSHOT %10'dan ayrışır (guard kilitlemez).
        await TenantKdvAsync(sp, 0.20m);
        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!;
        Assert.Equal(300m, inv.NetTutar);   // 330 / 1.10 (elle)
        Assert.Equal(30m, inv.KdvTutar);
        Assert.Equal(330m, inv.GenelToplam);
    }

    [Fact]
    public async Task Brut_kirada_fatura_tenant_oranindan_ayrisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await TenantKdvAsync(sp, 0.10m);
        var (m, v) = await SeedAsync(sp, "34 KV 02");

        // Brüt (varsayılan mod) 120×3 = 360; parametresiz fatura → tenant %10: 327,27/32,73 (elle).
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 120m });
        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!;
        Assert.Equal(327.27m, inv.NetTutar);
        Assert.Equal(32.73m, inv.KdvTutar);

        // Özel oran zinciri ÜSTÜN kalır: OzelKdvOran %18'li kira tenant oranını ezer.
        var v2 = (await SeedAsync(sp, "34 KV 03")).v;
        var id2 = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v2, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 118m, OzelKdvOran = 0.18m });
        var inv2Id = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id2);
        var inv2 = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(inv2Id))!;
        Assert.Equal(300m, inv2.NetTutar);  // 354 / 1.18 (elle)
        Assert.Equal(54m, inv2.KdvTutar);
    }

    [Fact]
    public async Task Net_mod_citi_gecerli_varsayilanla_karsilastirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await TenantKdvAsync(sp, 0.10m);
        var (m, v) = await SeedAsync(sp, "34 KV 04");
        var rentals = sp.GetRequiredService<RentalService>();

        // Tenant %10 iken net-mod + OzelKdv %10 = ÇELİŞKİ DEĞİL (eski sabit-0.20 çiti reddederdi).
        var id = await rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 100m, FiyatTuru = "Günlük", OzelKdvOran = 0.10m
        });
        Assert.Equal(330m, (await rentals.GetAsync(id))!.Tutar);

        // %18 hâlâ çelişki → giriş noktasında red.
        var v2 = (await SeedAsync(sp, "34 KV 05")).v;
        await Assert.ThrowsAsync<ValidationException>(() => rentals.CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v2, BasTar = Bas, BitTar = Bas.AddDays(3),
            GunlukUcret = 100m, FiyatTuru = "Günlük", OzelKdvOran = 0.18m
        }));
    }

    [Fact]
    public async Task Onizleme_ve_sistem_tanimlari_tenant_oranini_kullanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await TenantKdvAsync(sp, 0.10m);
        var (m, v) = await SeedAsync(sp, "34 KV 06");

        // Önizleme: "Günlük" NET 100 × 3g → 330 (gross-up %10) — kayıtla bit-eş; Net/Kdv %10'dan.
        var oniz = await sp.GetRequiredService<RentalCalculationService>().CalculateAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Bas, BitTar: Bas.AddDays(3), GunlukUcret: 100m,
            FiyatTuru: "Günlük", Doviz: null, CikisOfisi: null, EkHizmetler: [], MusteriId: m));
        Assert.Equal(330m, oniz.GenelToplam);
        Assert.Equal(300m, oniz.Net);
        Assert.Equal(30m, oniz.Kdv);

        // Sistem ücret tanımı tenant KDV'siyle doğar: genç sürücü 100 NET/gün × 3g → 300 net + %10 = 330.
        await sp.GetRequiredService<RentACar.Application.VehicleGroups.VehicleGroupService>()
            .CreateAsync(new RentACar.Application.VehicleGroups.VehicleGroupInput
            { Kod = "EKO", Ad = "E", GencSurucuYas = 25, GencSurucuUcretGunluk = 100m });
        var vg = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 KV 07", Grup = "EKO" });
        var genc = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Bireysel, Ad = "Genc", Soyad = "K", DogumTarihi = Bas.AddYears(-22).AddDays(-1) });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = genc, VehicleId = vg, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 1000m });
        var satir = (await sp.GetRequiredService<RentACar.Application.RentalAddOns.RentalAddOnService>()
            .ListAsync(id)).Single();
        Assert.Equal(300m, satir.NetTutar);
        Assert.Equal(30m, satir.KdvTutar);   // %10 (tenant) — %20 değil
        Assert.Equal(330m, satir.Toplam);
    }

    [Fact]
    public async Task Eski_net_mod_kirada_citler_ayni_tabana_bakar() // adversarial A6-B1 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp, "34 KV 08");
        var rentals = sp.GetRequiredService<RentalService>();

        // Pre-A6 net-mod kira simülasyonu: snapshot NULL'a çekilir (0.20 gross-up'lı eski veri).
        var id = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, FiyatTuru = "Günlük" });
        await sp.GetRequiredService<IBookingRepository>().UpdateRentalAsync(id, c => c.KdvOranSnapshot = null);
        await TenantKdvAsync(sp, 0.10m); // tenant oranı sonradan %10

        // Düzeltme öncesi: güncelleme çiti tenant-%10'a bakıp 0.20'yi REDDEDİYOR, 0.10'u kabul edip
        // faturayı KİLİTLİYORDU. Artık iki çit de aynı tabana (snapshot ?? 0.20) bakar:
        await rentals.UpdateOpenAsync(id, new RentalUpdateInput { OzelKdvOran = 0.20m });   // gerçek gross-up oranı KABUL
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.UpdateOpenAsync(id, new RentalUpdateInput { OzelKdvOran = 0.10m })); // tenant oranı RED (çelişki)

        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(id);  // kilit yok
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!;
        Assert.Equal(300m, inv.NetTutar);   // 360 / 1.20 (eski gross-up oranından — elle)
        Assert.Equal(60m, inv.KdvTutar);
    }

    [Fact]
    public async Task Rez_ve_teklif_zinciri_net_mod_niyetini_tasir() // adversarial A6-B2 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await TenantKdvAsync(sp, 0.10m);
        var (m, v) = await SeedAsync(sp, "34 KV 09");

        // Rez "Günlük" NET 100×3 → 330; niyet 300 net + 30 KDV (%10 snapshot rezde persist).
        var rez = sp.GetRequiredService<ReservationService>();
        var rid = await rez.CreateAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m, FiyatTuru = "Günlük" });
        var r = (await rez.GetAsync(rid))!;
        Assert.Equal(330m, r.Tutar);
        Assert.Equal(0.10m, r.KdvOranSnapshot);
        Assert.Equal("Günlük", r.FiyatTuru);

        // Tenant oranı DEĞİŞİR → dönüşüm → fatura yine SNAPSHOT %10'dan ayrışır.
        // (Düzeltme öncesi: kira brüt-mod muamelesi görüp %20'den 275/55 ayrışıyordu — sessiz sapma.)
        await TenantKdvAsync(sp, 0.20m);
        var kiraId = await rez.ConvertToRentalAsync(rid);
        var c = (await sp.GetRequiredService<RentalService>().GetAsync(kiraId))!;
        Assert.Equal(0.10m, c.KdvOranSnapshot);
        Assert.Equal("Günlük", c.FiyatTuru);
        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(kiraId);
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!;
        Assert.Equal(300m, inv.NetTutar);
        Assert.Equal(30m, inv.KdvTutar);
    }

    [Fact]
    public async Task Ayar_dogrulamasi_ve_bozuk_deger_guvenli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Bozuk kayıt (aralık-dışı; eski/elle veri) fiyatı saptırmaz — 0.20'ye düşer.
        await TenantKdvAsync(sp, 5m);
        Assert.Equal(0.20m, await sp.GetRequiredService<VatDefault>().RateAsync());

        await TenantKdvAsync(sp, 0.10m);
        Assert.Equal(0.10m, await sp.GetRequiredService<VatDefault>().RateAsync());
    }
}
