using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-10 — araç kayıt kartı alan derinliği (canlı <c>arac_kayit.aspx</c>).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler testte ELLE sabitlenir; servis/entity kodundan
/// türetilmez.</para>
///
/// <para><b>KUR ALANLARI SALT BİLGİ:</b> araç karnesi ve karlılık P&amp;L'i CLAUDE.md §4 gereği
/// YALNIZ DEFTERDEN hesaplanır. Bu dosyadaki regresyon testi bunu ampirik kilitler: aynı senaryo
/// bir kez kur alanları BOŞ, bir kez uçuk değerlerle DOLU kurulur ve rapor sayıları birebir aynı
/// çıkar. İleride biri kuru bir hesaba bağlarsa bu test kırılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class VehicleKartDerinlikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Settlement = new(2026, 4, 30, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Planned = new(2027, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>ELLE seçilmiş sabitler — 20 alanın tamamı doludur.</summary>
    private static VehicleInput Filled(string plate) => new()
    {
        Plaka = plate,
        TsrbMarkaKodu = "TS-M-42", TsrbTipKodu = "TS-T-07", AltGrupAdi = "Eko Alt",
        EntegrasyonKodu = "ENT-9001", TeypKodu = "TEYP-55",
        TakipMarka = "Arvento", TakipNo = "GPS-123456",
        SahipGrup = "Kendi Filo", AracSahibiNo = "SH-0007", AracSahibi2 = "Ortak A.Ş.",
        KrediFirma = "X Bank Finans",
        KapatmaTarih = Settlement, CikmasiPlananTarih = Planned, AracSatisKm = 148_500,
        Aciklama = "Kart derinliği testi", Konum = "Merkez otopark B2",
        AlimBedeliKur = 34.2500m, Arac2FiyatKur = 36.1000m, SimdiKur = 41.7500m,
        AylikMaliyetDoviz = 250.00m
    };

    private static void ValidateFilled(RentACar.Domain.Entities.Vehicle v)
    {
        Assert.Equal("TS-M-42", v.TsrbMarkaKodu);
        Assert.Equal("TS-T-07", v.TsrbTipKodu);
        Assert.Equal("Eko Alt", v.AltGrupAdi);
        Assert.Equal("ENT-9001", v.EntegrasyonKodu);
        Assert.Equal("TEYP-55", v.TeypKodu);
        Assert.Equal("Arvento", v.TakipMarka);
        Assert.Equal("GPS-123456", v.TakipNo);
        Assert.Equal("Kendi Filo", v.SahipGrup);
        Assert.Equal("SH-0007", v.AracSahibiNo);
        Assert.Equal("Ortak A.Ş.", v.AracSahibi2);
        Assert.Equal("X Bank Finans", v.KrediFirma);
        Assert.Equal(Settlement, v.KapatmaTarih);
        Assert.Equal(Planned, v.CikmasiPlananTarih);
        Assert.Equal(148_500, v.AracSatisKm);
        Assert.Equal("Kart derinliği testi", v.Aciklama);
        Assert.Equal("Merkez otopark B2", v.Konum);
        Assert.Equal(34.2500m, v.AlimBedeliKur);
        Assert.Equal(36.1000m, v.Arac2FiyatKur);
        Assert.Equal(41.7500m, v.SimdiKur);
        Assert.Equal(250.00m, v.AylikMaliyetDoviz);
    }

    [Fact]
    public async Task Yirmi_alan_create_yolunda_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(Filled("34 KD 01"));
        ValidateFilled((await svc.GetAsync(id))!);
    }

    [Fact]
    public async Task Yirmi_alan_UPDATE_yolunda_da_yazilir_ve_temizlenebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        // BOŞ oluştur → UPDATE ile doldur. Yalnız create'e map edilmiş bir alan burada yakalanır
        // (kopya-kurucu tuzağı: iki yoldan birine yazmayı unutmak alanı sessizce düşürür).
        var id = await svc.CreateAsync(new VehicleInput { Plaka = "34 KD 02" });
        var empty = (await svc.GetAsync(id))!;
        Assert.Null(empty.TakipNo);
        Assert.Null(empty.SimdiKur);

        Assert.True(await svc.UpdateAsync(id, Filled("34 KD 02")));
        ValidateFilled((await svc.GetAsync(id))!);

        // Temizleme de bir güncellemedir: boş gönderilen metin alanı null'a döner (Trim deseni).
        Assert.True(await svc.UpdateAsync(id, new VehicleInput { Plaka = "34 KD 02", Konum = "   " }));
        var last = (await svc.GetAsync(id))!;
        Assert.Null(last.Konum);
        Assert.Null(last.TakipNo);
        Assert.Null(last.AlimBedeliKur);
    }

    [Fact]
    public async Task Son_uc_km_kaydi_en_yenilerdir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();
        var id = await svc.CreateAsync(new VehicleInput { Plaka = "34 KD 03" });

        // ELLE: 5 manuel KM girişi, artan sırada. Odometre geriye gidemez.
        int[] kms = [1000, 2000, 3000, 4000, 5000];
        foreach (var km in kms) await svc.EnterManualKmAsync(id, km);

        var last3 = await svc.KmLogsAsync(id, 3);
        Assert.Equal(3, last3.Count);
        // EN YENİ 3: 5000, 4000, 3000 (elle) — sıra en yeniden eskiye.
        Assert.Equal([5000, 4000, 3000], last3.Select(x => x.Km));
        Assert.All(last3, k => Assert.Equal(KmLogSource.Manuel, k.Kaynak));

        // Araç kartındaki Km alanı da son değere gelmiş olmalı.
        Assert.Equal(5000, (await svc.GetAsync(id))!.Km);
    }

    /// <summary>
    /// KARAR KİLİDİ — kur alanları hiçbir rapora girmez. Aynı senaryo iki araçta kurulur; tek fark
    /// biri kur alanlarını UÇUK değerlerle doldurur. Karne P&amp;L'i ve karlılık satırı BİREBİR
    /// aynı olmalı (P&amp;L yalnız defterden).
    /// </summary>
    [Fact]
    public async Task Kur_alanlari_KARNE_ve_KARLILIGA_girmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicles = sp.GetRequiredService<VehicleService>();
        var expenses = sp.GetRequiredService<RentACar.Application.Expenses.ExpenseService>();
        var report = sp.GetRequiredService<ReportService>();
        var date = DateTimeOffset.UtcNow.AddDays(-2);

        // İki araç, AYNI alım bedeli ve AYNI gider — tek fark kur alanları.
        var sade = await vehicles.CreateAsync(new VehicleInput
        { Plaka = "34 KD 10", AlimBedeli = 500_000m, AylikMaliyet = 1_000m });
        var withExchangeRate = await vehicles.CreateAsync(new VehicleInput
        {
            Plaka = "34 KD 11", AlimBedeli = 500_000m, AylikMaliyet = 1_000m,
            AlimBedeliKur = 99.9999m, Arac2FiyatKur = 88.8888m, SimdiKur = 77.7777m,
            AylikMaliyetDoviz = 999_999.9999m      // uçuk: bir hesaba girseydi tabloyu uçururdu
        });

        // ELLE: net 2000 + %25 KDV = 2500 brüt gider. KDV gidere girmez → karne gideri 2000.
        foreach (var v in new[] { sade, withExchangeRate })
            await expenses.CreateAsync(new RentACar.Application.Expenses.ExpenseInput
            {
                Tip = ExpenseType.Arac, NetTutar = 2_000m, KdvOrani = 0.25m, Tarih = date,
                KasaBankaHesap = LedgerAccountType.Kasa, VehicleId = v, Aciklama = "Bakım"
            });

        var kSade = await report.GetVehicleScorecardAsync(sade);
        var kWithRate = await report.GetVehicleScorecardAsync(withExchangeRate);

        Assert.NotNull(kSade);
        Assert.NotNull(kWithRate);
        Assert.Equal(2_000m, kSade!.ToplamGider);                  // ELLE: yalnız defterdeki NET gider
        Assert.Equal(kSade.ToplamGelir, kWithRate!.ToplamGelir);
        Assert.Equal(kSade.ToplamGider, kWithRate.ToplamGider);
        Assert.Equal(kSade.ToplamNetKar, kWithRate.ToplamNetKar);

        // Karlılık satırı da aynı — iki yol da kurdan habersiz.
        var profitability = await report.GetProfitabilityAsync();
        var rSade = profitability.Satirlar.Single(r => r.Plaka == "34KD10");
        var rWithRate = profitability.Satirlar.Single(r => r.Plaka == "34KD11");
        Assert.Equal(rSade.Gelir, rWithRate.Gelir);
        Assert.Equal(rSade.Gider, rWithRate.Gider);
        Assert.Equal(rSade.NetKar, rWithRate.NetKar);
        Assert.Equal(-2_000m, rWithRate.NetKar);                      // ELLE: 0 gelir − 2000 gider
    }

    /// <summary>
    /// Araç formunun <b>GÜN ROUND-TRIP</b> sözleşmesi — canlı duman testinde yakalanan mevcut hata.
    ///
    /// <para>Form "2026-04-30" gönderir; <c>FormParse.Date</c> bunu SUNUCU YERELİNDE çözüp UTC'ye
    /// çevirir (+03:00 → 29 Nisan 21:00Z). Ekran prefill'i UTC gününü basarsa alan bir gün GERİ
    /// kayar ve her kaydetmede bir gün daha geriler (Tescil/Alım/Filo Giriş-Çıkış/Son Bakım
    /// alanlarının hepsi böyleydi). Doğru prefill LOCAL gündür.</para>
    ///
    /// <para>Test saat diliminden bağımsız: UTC'nin ilerisinde de gerisinde de aynı iddia geçerli.</para>
    /// </summary>
    [Theory]
    [InlineData("2026-04-30")]
    [InlineData("2026-01-01")]
    [InlineData("2026-12-31")]
    public void Form_gunu_LOCAL_prefill_ile_birebir_round_trip_eder(string day)
    {
        var resolved = RentACar.Web.FormParse.Date(day);
        Assert.NotNull(resolved);

        // Ekranın bastığı değer (düzeltme sonrası): LocalDateTime.
        Assert.Equal(day, resolved!.Value.LocalDateTime.ToString("yyyy-MM-dd"));

        // Round-trip: basılan değeri tekrar göndermek aynı anı üretmeli (gün kaymaz).
        Assert.Equal(resolved, RentACar.Web.FormParse.Date(resolved.Value.LocalDateTime.ToString("yyyy-MM-dd")));
    }

    /// <summary>
    /// Araç formunun <b>ONDALIK ROUND-TRIP</b> sözleşmesi — canlı duman testinde yakalanan mevcut
    /// hata: <c>value="@decimal"</c> tr-TR'de "1250,5000" basıyor, tarayıcı <c>type=number</c>
    /// alanını geçersiz sayıp BOŞALTIYOR ve sonraki kaydetme değeri NULL'luyordu (kaydedilmiş alım
    /// bedeli/kira fiyatı sessizce siliniyordu). Doğru basım InvariantCulture'dır.
    /// </summary>
    [Fact]
    public void Form_ondaligi_INVARIANT_basilmali_yoksa_deger_kaybolur()
    {
        var tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        const decimal value = 1250.5000m;

        // Hatalı yol: kültüre bağlı basım virgül üretir. Tarayıcı bunu type=number'da geçersiz
        // sayıp alanı boşaltır (pratikte değer NULL'lanır); sunucuya ham gelse bile virgül
        // Invariant'ta BİNLİK AYIRICIDIR → 1250,5000 = 12.505.000 gibi felaket bir okuma çıkar.
        // İki sonuç da yanlış; bu yüzden basım daima Invariant olmalı.
        var cultural = value.ToString(tr);
        Assert.Contains(",", cultural);
        Assert.NotEqual(value, RentACar.Web.FormParse.Dec(cultural));
        Assert.Null(RentACar.Web.FormParse.Dec(""));      // tarayıcının boşalttığı alan → null

        // Doğru yol: Invariant basım → aynı değer geri gelir.
        var invariant = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.DoesNotContain(",", invariant);
        Assert.Equal(value, RentACar.Web.FormParse.Dec(invariant));
    }

    [Fact]
    public async Task Yeni_alanlar_tenant_izolasyonunu_bozmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
            await a.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(Filled("34 KD 20"));

        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<VehicleService>().ListAsync());
    }
}
