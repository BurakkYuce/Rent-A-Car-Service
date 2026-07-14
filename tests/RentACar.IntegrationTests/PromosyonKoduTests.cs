using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Pricing;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ3-A5 — Promosyon kodu: birebir eşleşme REPLACE (stacking yok), kapsam tutmazsa GÜRÜLTÜLÜ RED
/// (sessiz yutma yok), kod operatör talimatı olduğundan az avantajlıysa da uygulanır (+not).
/// BAĞIMSIZ ORACLE (elle): düz 1000/gün, 3 gün → kodsuz %10 = 2700; kod YAZ50 (%50) = 1500;
/// az-avantajlı kod AZ5 (%5) = 2850 + karşılaştırma notu; HediyeGun=10 &amp; 5 gün → hediye 5'e
/// kırpılır → 0. Redler: geçersiz kod; MinGun=7 kuralı 3 günde; süresi dolmuş kod REPRICE'ta
/// (rezervasyon güncellemesi alanı temizlemeye zorlar); manuel fiyat + kod.
/// </summary>
[Collection("postgres")]
public sealed class PromosyonKoduTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(5);

    private static async Task<(Guid m, Guid v)> SeedAsync(IServiceProvider sp)
    {
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Ekonomik", GunlukKmLimiti = 300, AsimKmUcreti = 5.00m });
        await sp.GetRequiredService<RateMatrixService>().CreateAsync(new RateMatrixInput
        {
            Kod = "EKO-STD", Ad = "Eko", AracGrupKod = "EKO", ParaBirimi = "TRY",
            Gun1 = 1000m, Gun2 = 1000m, Gun3 = 1000m, Gun4 = 1000m, Gun5 = 1000m,
            OnayDurumu = TarifeOnayDurumu.Onayli, Onaylayan = "t"
        });
        var rr = sp.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput { Kod = "GENEL", Ad = "Genel", Iskonto = 10m });
        await rr.CreateAsync(new RentalRuleInput
        { Kod = "KAMP50", Ad = "Yaz", Iskonto = 50m, KampanyaMi = true, KampanyaKodu = "YAZ50" });

        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 PK 01", Grup = "EKO" });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "PK", Soyad = "M" });
        return (m, v);
    }

    private static BookingInput Girdi(Guid m, Guid v, string? kod, int gun = 3) => new()
    { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(gun), FiyatTuru = "Otomatik", KampanyaKodu = kod };

    [Fact]
    public async Task Kod_replace_uygular_ve_iz_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        var rentals = sp.GetRequiredService<RentalService>();

        // Kodlu (küçük harf "yaz50" — case-insensitive): %50 REPLACE → 3000×0.50 = 1500 (elle).
        var id = await rentals.CreateDirectAsync(Girdi(m, v, "yaz50"));
        var c = (await rentals.GetAsync(id))!;
        Assert.Equal(1500.00m, c.Tutar);
        Assert.Equal("yaz50", c.KampanyaKodu);      // iz (girildiği gibi, Trim'li)

        // Kodsuz aynı kurulum: otomatik %10 → 2700 (kod-kapılı %50 A0 çitiyle otomatikte kapalı).
        var v2 = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 PK 02", Grup = "EKO" });
        var id2 = await rentals.CreateDirectAsync(Girdi(m, v2, null));
        Assert.Equal(2700.00m, (await rentals.GetAsync(id2))!.Tutar);
    }

    [Fact]
    public async Task Az_avantajli_kod_da_uygulanir_notla()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "AZ5", Ad = "Az", Iskonto = 5m, KampanyaMi = true, KampanyaKodu = "AZ5" });

        // Kod %5 < otomatik %10 — yine de KOD uygulanır (operatör talimatı) + karşılaştırma notu.
        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas.AddDays(3), KampanyaKodu = "AZ5" });
        Assert.Equal(2850.00m, q.GenelToplam);      // 3000 × 0.95 (elle)
        Assert.Contains(q.Notlar, n => n.Contains("daha avantajlıydı"));
    }

    [Fact]
    public async Task Hediye_gun_kiralama_gununе_kirpilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "BEDAVA", Ad = "Bedava", HediyeGun = 10, KampanyaMi = true, KampanyaKodu = "BEDAVA" });

        // HediyeGun=10 ama kira 5 gün → hediye 5'e kırpılır → faturalanan 0 → toplam 0 (negatif yok).
        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas.AddDays(5), KampanyaKodu = "BEDAVA" });
        Assert.Equal(5, q.HediyeGun);
        Assert.Equal(0, q.FaturalananGun);
        Assert.Equal(0.00m, q.GenelToplam);
    }

    [Fact]
    public async Task Gurultulu_redler()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        var rr = sp.GetRequiredService<RentalRuleService>();
        await rr.CreateAsync(new RentalRuleInput
        { Kod = "HAFTA", Ad = "Haftalık", Iskonto = 20m, MinGun = 7, KampanyaMi = true, KampanyaKodu = "HAFTA" });
        var rentals = sp.GetRequiredService<RentalService>();

        // Geçersiz kod: sessizce otomatiğe düşmek YOK — temiz red.
        var ex1 = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(Girdi(m, v, "YOK50")));
        Assert.Contains("geçersiz", ex1.Message);

        // Kapsam: MinGun=7 kuralı 3 günlük kirada — red mesajı gün şartını söyler.
        var ex2 = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(Girdi(m, v, "HAFTA", gun: 3)));
        Assert.Contains("en az 7 gün", ex2.Message);

        // Manuel fiyat + kod: motor çalışmaz → kod sessiz yutulurdu → gürültülü red.
        var ex3 = await Assert.ThrowsAsync<ValidationException>(
            () => rentals.CreateDirectAsync(new BookingInput
            { MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 500m, KampanyaKodu = "YAZ50" }));
        Assert.Contains("Otomatik", ex3.Message);
    }

    [Fact]
    public async Task Ratecard_fallback_kodu_yutamaz() // adversarial B1 (High) regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // MATRİS YOK — yalnız legacy RateCard (900/gün) + kodlu kural. Düzeltme öncesi: kod motora
        // gidip doğrulanıyor, sonra RateCard 2700 İNDİRİMSİZ fiyatlıyor + kod "iz" olarak yazılıyordu.
        await sp.GetRequiredService<VehicleGroupService>().CreateAsync(new VehicleGroupInput
        { Kod = "EKO", Ad = "Eko", GunlukKmLimiti = 300, AsimKmUcreti = 5m });
        await sp.GetRequiredService<RateCardService>().CreateAsync(new RateCardInput
        { Kod = "RC", Ad = "RC", Grup = "EKO", GunlukUcret = 900m });
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "KAMP50", Ad = "Yaz", Iskonto = 50m, KampanyaMi = true, KampanyaKodu = "YAZ50" });
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 PK 10", Grup = "EKO" });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "PK", Soyad = "R" });

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<RentalService>().CreateDirectAsync(Girdi(m, v, "YAZ50")));
        Assert.Contains("tarife", ex.Message);
    }

    [Fact]
    public async Task Ayni_kod_iki_kuralda_uyan_uygulanir() // adversarial B4 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp);
        // Servis benzersizliği artık ENGELLİYOR (aşağıda ayrıca doğrulanır) — eski/bozuk veriyi taklit
        // için ikinci kural REPO'dan (servis atlanarak) eklenir: kapsamı UYMAYAN İzmir %50 + uyan %10.
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        { Kod = "AAA", Ad = "İzmir", Iskonto = 50m, Sube = "IZMIR", KampanyaMi = true, KampanyaKodu = "DUP10" });
        await sp.GetRequiredService<IRentalRuleRepository>().CreateAsync(new Domain.Entities.RentalRule
        { Kod = "BBB", Ad = "Genel", Iskonto = 10m, KampanyaMi = true, KampanyaKodu = "DUP10", Aktif = true });

        // Şubesiz istekte İzmir kuralı uymaz — düzeltme öncesi YANLIŞ RED atılıyordu; artık uyan %10.
        var q = await sp.GetRequiredService<RentalQuoteEngine>().QuoteAsync(new QuoteRequest
        { AracGrupKod = "EKO", BasTar = Bas, BitTar = Bas.AddDays(3), KampanyaKodu = "DUP10" });
        Assert.Equal(2700.00m, q.GenelToplam);
        Assert.Contains(q.Notlar, n => n.Contains("birden çok kuralda"));

        // Servis katmanı: aynı kodla ÜÇÜNCÜ kural yaratılamaz (benzersizlik guard'ı).
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
            { Kod = "CCC", Ad = "Kopya", Iskonto = 5m, KampanyaKodu = "dup10" }));
        Assert.Contains("başka bir kuralda", ex.Message);
    }

    [Fact]
    public async Task Donusum_fiyat_taahhudunu_ve_kod_izini_tasir() // adversarial B3 regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        var rez = sp.GetRequiredService<ReservationService>();
        var rid = await rez.CreateAsync(Girdi(m, v, "YAZ50"));
        Assert.Equal(1500.00m, (await rez.GetAsync(rid))!.Tutar);

        // Dönüşüm REPRICE ETMEZ (fiyat taahhüdü taşınır) + kod/kaynak İZ olarak kiraya kopyalanır.
        var kiraId = await rez.ConvertToRentalAsync(rid);
        var c = (await sp.GetRequiredService<RentalService>().GetAsync(kiraId))!;
        Assert.Equal(1500.00m, c.Tutar);
        Assert.Equal("YAZ50", c.KampanyaKodu);
    }

    [Fact]
    public async Task Onizleme_kayitla_bit_es() // adversarial B5 regresyonu (önizleme == kayıt)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        var hesap = sp.GetRequiredService<KiraHesapService>();

        var onizleme = await hesap.HesaplaAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Bas, BitTar: Bas.AddDays(3), GunlukUcret: null,
            FiyatTuru: "Otomatik", Doviz: null, CikisOfisi: null, EkHizmetler: [],
            MusteriId: m, KampanyaKodu: "YAZ50"));
        Assert.True(onizleme.Ok);
        Assert.Equal(1500.00m, onizleme.Tutar);                    // canlı panel == kayıt

        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(Girdi(m, v, "YAZ50"));
        Assert.Equal(1500.00m, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.Tutar);

        // Geçersiz kod canlı hesapta NAZİK hata (ok:false) — exception değil (kullanıcı yazarken).
        var bozuk = await hesap.HesaplaAsync(new KiraHesapIstek(
            VehicleId: v, BasTar: Bas, BitTar: Bas.AddDays(3), GunlukUcret: null,
            FiyatTuru: "Otomatik", Doviz: null, CikisOfisi: null, EkHizmetler: [],
            MusteriId: m, KampanyaKodu: "YANLIS"));
        Assert.False(bozuk.Ok);
        Assert.Contains("geçersiz", bozuk.Hata);
    }

    [Fact]
    public async Task Uzun_kampanya_kodu_reddedilir() // adversarial B6 (Low) regresyonu
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
            { Kod = "UZUN", Ad = "Uzun", Iskonto = 5m, KampanyaKodu = new string('X', 65) }));
    }

    [Fact]
    public async Task Suresi_dolmus_kod_reprice_ta_gurultulu_red() // adversarial: alan temizlemeye zorlar
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (m, v) = await SeedAsync(sp);
        await sp.GetRequiredService<RentalRuleService>().CreateAsync(new RentalRuleInput
        {
            Kod = "ESKI10", Ad = "Eski", Iskonto = 10m, KampanyaMi = true, KampanyaKodu = "ESKI10",
            GecerlilikBit = Bas.AddDays(-1)                        // kira tarihinden önce bitmiş
        });
        var rez = sp.GetRequiredService<ReservationService>();
        var id = await rez.CreateAsync(Girdi(m, v, null));
        Assert.Equal(2700.00m, (await rez.GetAsync(id))!.Tutar);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => rez.UpdateAsync(id, Girdi(m, v, "ESKI10")));
        Assert.Contains("süresi doldu", ex.Message);
        Assert.Equal(2700.00m, (await rez.GetAsync(id))!.Tutar);   // fiyat/kayıt değişmedi
        Assert.Null((await rez.GetAsync(id))!.KampanyaKodu);
    }
}
