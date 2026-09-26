using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-27 — Karşılaştırmalı durum analizi (hacim pivotu).
///
/// <para><b>Bağımsız oracle:</b> senaryo elle kurulur ve beklenen HÜCRE değerleri testte tek tek
/// yazılır ("Ekonomik grubunda Ocak'ta 2 kira, Şubat'ta 1"). Değerler servisin dönüşünden
/// türetilmez.</para>
///
/// <para><b>Bu rapor TUTAR ÜRETMEZ</b> — yalnız adet/gün sayar. Ayrı bir test aynı veri kümesinde
/// "Adet" ve "Gün" modlarının farklı ve elle hesaplanabilir sonuçlar verdiğini doğruluyor.</para>
///
/// <para>Ay kolonlarının VERİDEN değil PENCEREDEN üretildiği de kilitleniyor: hiç iş olmayan ay
/// grid'den sessizce düşerse "o ay boş" bilgisi kaybolur.</para>
/// </summary>
[Collection("postgres")]
public sealed class KarsilastirmaliAnalizTests(PostgresFixture fx)
{
    // Sabit, geçmişte ve ay sınırlarından uzak iki ay (ay-sonu kayması olmasın).
    private static readonly DateTimeOffset January = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset February = new(2026, 2, 10, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> VehicleAsync(IServiceProvider sp, string plate, string group)
        => await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Grup = group });

    private static async Task<Guid> RentalAsync(
        IServiceProvider sp, Guid account, Guid vehicle, DateTimeOffset start, int day,
        string? office = null, string? source = null)
        => await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(day),
            GunlukUcret = 1000m, CikisOfisi = office, Kaynak = source
        });

    /// <summary>
    /// 6 kira:
    ///   Ekonomik — Ocak ×2 (3 gün + 2 gün), Şubat ×1 (4 gün)
    ///   Orta     — Ocak ×1 (1 gün)
    ///   Lüks     — Şubat ×2 (5 gün + 5 gün)
    /// Adet: Ekonomik 2/1, Orta 1/0, Lüks 0/2   → genel 6
    /// Gün : Ekonomik 5/4, Orta 1/0, Lüks 0/10  → genel 20
    /// </summary>
    /// <returns>İlk (Merkez / Ocak / Ekonomik) kiranın kimliği — iptal senaryosunda kullanılır.</returns>
    private static async Task<Guid> ScenarioAsync(IServiceProvider sp)
    {
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Pivot A.Ş." });
        var eko1 = await VehicleAsync(sp, "34 PV 01", "Ekonomik");
        var eko2 = await VehicleAsync(sp, "34 PV 02", "Ekonomik");
        var middle = await VehicleAsync(sp, "34 PV 03", "Orta");
        var lux1 = await VehicleAsync(sp, "34 PV 04", "Lüks");
        var lux2 = await VehicleAsync(sp, "34 PV 05", "Lüks");

        var firstCenter = await RentalAsync(sp, account, eko1, January, 3, "Merkez", "Web");
        await RentalAsync(sp, account, eko2, January, 2, "Merkez", "Acente");
        await RentalAsync(sp, account, eko1, February, 4, "Şube2", "Web");
        await RentalAsync(sp, account, middle, January, 1, "Merkez", "Web");
        await RentalAsync(sp, account, lux1, February, 5, "Şube2", "Acente");
        await RentalAsync(sp, account, lux2, February, 5, "Şube2", "Acente");
        return firstCenter;
    }

    private static KarsilastirmaliAnalizFilter Window(string breakdown = "AracGrubu", string data = "Adet")
        => new()
        {
            Tablo = "Kira", VeriTuru = data, Kirilim = breakdown,
            Bas = January.AddDays(-9), Bit = February.AddDays(18)   // Ocak 1 – Şubat 28
        };

    [Fact]
    public async Task Adet_pivotu_ELLE_HESAPLANAN_hucreleri_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await ScenarioAsync(sp);

        var d = await sp.GetRequiredService<ReportService>().GetComparativeAnalysisAsync(Window());

        Assert.Equal("Kira", d.Tablo);
        Assert.Equal("Adet", d.VeriTuru);
        Assert.Equal(["2026-01", "2026-02"], d.AyAnahtarlari);
        Assert.Equal(3, d.Satirlar.Count);

        var eko = Assert.Single(d.Satirlar, x => x.Kirilim == "Ekonomik");
        Assert.Equal(2m, eko.Month("2026-01"));
        Assert.Equal(1m, eko.Month("2026-02"));
        Assert.Equal(3m, eko.Toplam);

        var middle = Assert.Single(d.Satirlar, x => x.Kirilim == "Orta");
        Assert.Equal(1m, middle.Month("2026-01"));
        Assert.Equal(0m, middle.Month("2026-02"));   // veri yok → 0

        var lux = Assert.Single(d.Satirlar, x => x.Kirilim == "Lüks");
        Assert.Equal(0m, lux.Month("2026-01"));
        Assert.Equal(2m, lux.Month("2026-02"));

        // Kolon ve genel toplamlar
        Assert.Equal(3m, d.MonthTotal("2026-01"));
        Assert.Equal(3m, d.MonthTotal("2026-02"));
        Assert.Equal(6m, d.GenelToplam);
    }

    [Fact]
    public async Task Gun_pivotu_ADETTEN_FARKLI_ve_elle_hesaplanabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await ScenarioAsync(sp);

        var d = await sp.GetRequiredService<ReportService>()
            .GetComparativeAnalysisAsync(Window(data: "Gun"));

        Assert.Equal("Gun", d.VeriTuru);
        var eko = Assert.Single(d.Satirlar, x => x.Kirilim == "Ekonomik");
        Assert.Equal(5m, eko.Month("2026-01"));    // 3 + 2
        Assert.Equal(4m, eko.Month("2026-02"));
        Assert.Equal(10m, Assert.Single(d.Satirlar, x => x.Kirilim == "Lüks").Month("2026-02"));  // 5 + 5
        Assert.Equal(20m, d.GenelToplam);       // adet modunda 6 idi
    }

    [Fact]
    public async Task Kirilim_boyutu_degisince_SATIRLAR_degisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await ScenarioAsync(sp);
        var report = sp.GetRequiredService<ReportService>();

        // Rezervasyon kaynağı: Web ×3 (Ocak 2 + Şubat 1), Acente ×3 (Ocak 1 + Şubat 2)
        var source = await report.GetComparativeAnalysisAsync(Window(breakdown: "RezKaynagi"));
        Assert.Equal(2, source.Satirlar.Count);
        var web = Assert.Single(source.Satirlar, x => x.Kirilim == "Web");
        Assert.Equal(2m, web.Month("2026-01"));
        Assert.Equal(1m, web.Month("2026-02"));
        Assert.Equal(3m, Assert.Single(source.Satirlar, x => x.Kirilim == "Acente").Toplam);

        // Çıkış noktası: Merkez ×3 (hepsi Ocak), Şube2 ×3 (hepsi Şubat)
        var office = await report.GetComparativeAnalysisAsync(Window(breakdown: "CikisNoktasi"));
        var headOffice = Assert.Single(office.Satirlar, x => x.Kirilim == "Merkez");
        Assert.Equal(3m, headOffice.Month("2026-01"));
        Assert.Equal(0m, headOffice.Month("2026-02"));
        Assert.Equal(3m, Assert.Single(office.Satirlar, x => x.Kirilim == "Şube2").Month("2026-02"));
    }

    [Fact]
    public async Task Bos_ay_KOLON_olarak_KALIR_veriden_turetilmiyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await ScenarioAsync(sp);

        // Pencereyi Mart'a kadar uzat: Mart'ta HİÇ kira yok ama kolon görünmeli.
        var d = await sp.GetRequiredService<ReportService>().GetComparativeAnalysisAsync(
            new KarsilastirmaliAnalizFilter
            { Bas = January.AddDays(-9), Bit = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero) });

        Assert.Equal(["2026-01", "2026-02", "2026-03"], d.AyAnahtarlari);
        Assert.Equal(0m, d.MonthTotal("2026-03"));    // boş ay kolonu duruyor
        Assert.Equal(6m, d.GenelToplam);             // toplam değişmedi
    }

    [Fact]
    public async Task Sube_filtresi_ve_iptal_kirasi_dogru_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var firstCenter = await ScenarioAsync(sp);
        var report = sp.GetRequiredService<ReportService>();

        var f = Window();
        f.Ofis = "Merkez";
        var headOffice = await report.GetComparativeAnalysisAsync(f);
        Assert.Equal(3m, headOffice.GenelToplam);        // Merkez'den 3 kira
        Assert.Equal(0m, headOffice.MonthTotal("2026-02"));

        // İptal edilen kira sayılmamalı.
        await sp.GetRequiredService<RentalService>().CancelAsync(firstCenter);

        var after = await report.GetComparativeAnalysisAsync(Window());
        Assert.Equal(5m, after.GenelToplam);         // 6 → 5
    }

    [Fact]
    public async Task Rezervasyon_tablosu_KIRADAN_bagimsiz_sayar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await ScenarioAsync(sp);

        // Kiralar var ama hiç rezervasyon yok → Rezervasyon tablosu BOŞ dönmeli.
        var f = Window();
        f.Tablo = "Rezervasyon";
        var d = await sp.GetRequiredService<ReportService>().GetComparativeAnalysisAsync(f);
        Assert.Equal("Rezervasyon", d.Tablo);
        Assert.Empty(d.Satirlar);
        Assert.Equal(0m, d.GenelToplam);
        // Ay kolonları yine pencereden gelir (veri olmasa da).
        Assert.Equal(2, d.AyAnahtarlari.Count);
    }

    [Fact]
    public async Task Grupsuz_arac_BELIRTILMEMIS_satirinda_toplanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Grupsuz" });
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 NG 01" });   // grup YOK
        await RentalAsync(sp, account, vehicle, January, 2);

        var d = await sp.GetRequiredService<ReportService>().GetComparativeAnalysisAsync(Window());
        // Grupsuz araç sessizce DÜŞMEMELİ — ayrı bir satırda görünmeli.
        Assert.Equal("(belirtilmemiş)", Assert.Single(d.Satirlar).Kirilim);
        Assert.Equal(1m, d.GenelToplam);
    }

    [Fact]
    public async Task Pivot_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid())) await ScenarioAsync(s1.ServiceProvider);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var d = await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetComparativeAnalysisAsync(Window());
        Assert.Empty(d.Satirlar);
        Assert.Equal(0m, d.GenelToplam);
    }
}
