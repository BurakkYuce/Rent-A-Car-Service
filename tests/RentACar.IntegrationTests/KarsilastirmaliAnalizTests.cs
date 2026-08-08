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
    private static readonly DateTimeOffset Ocak = new(2026, 1, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Subat = new(2026, 2, 10, 9, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> AracAsync(IServiceProvider sp, string plaka, string grup)
        => await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plaka, Grup = grup });

    private static async Task<Guid> KiraAsync(
        IServiceProvider sp, Guid cari, Guid arac, DateTimeOffset bas, int gun,
        string? ofis = null, string? kaynak = null)
        => await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = bas, BitTar = bas.AddDays(gun),
            GunlukUcret = 1000m, CikisOfisi = ofis, Kaynak = kaynak
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
    private static async Task<Guid> SenaryoAsync(IServiceProvider sp)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Pivot A.Ş." });
        var eko1 = await AracAsync(sp, "34 PV 01", "Ekonomik");
        var eko2 = await AracAsync(sp, "34 PV 02", "Ekonomik");
        var orta = await AracAsync(sp, "34 PV 03", "Orta");
        var lux1 = await AracAsync(sp, "34 PV 04", "Lüks");
        var lux2 = await AracAsync(sp, "34 PV 05", "Lüks");

        var ilkMerkez = await KiraAsync(sp, cari, eko1, Ocak, 3, "Merkez", "Web");
        await KiraAsync(sp, cari, eko2, Ocak, 2, "Merkez", "Acente");
        await KiraAsync(sp, cari, eko1, Subat, 4, "Şube2", "Web");
        await KiraAsync(sp, cari, orta, Ocak, 1, "Merkez", "Web");
        await KiraAsync(sp, cari, lux1, Subat, 5, "Şube2", "Acente");
        await KiraAsync(sp, cari, lux2, Subat, 5, "Şube2", "Acente");
        return ilkMerkez;
    }

    private static KarsilastirmaliAnalizFilter Pencere(string kirilim = "AracGrubu", string veri = "Adet")
        => new()
        {
            Tablo = "Kira", VeriTuru = veri, Kirilim = kirilim,
            Bas = Ocak.AddDays(-9), Bit = Subat.AddDays(18)   // Ocak 1 – Şubat 28
        };

    [Fact]
    public async Task Adet_pivotu_ELLE_HESAPLANAN_hucreleri_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await SenaryoAsync(sp);

        var d = await sp.GetRequiredService<ReportService>().GetKarsilastirmaliAnalizAsync(Pencere());

        Assert.Equal("Kira", d.Tablo);
        Assert.Equal("Adet", d.VeriTuru);
        Assert.Equal(["2026-01", "2026-02"], d.AyAnahtarlari);
        Assert.Equal(3, d.Satirlar.Count);

        var eko = Assert.Single(d.Satirlar, x => x.Kirilim == "Ekonomik");
        Assert.Equal(2m, eko.Ay("2026-01"));
        Assert.Equal(1m, eko.Ay("2026-02"));
        Assert.Equal(3m, eko.Toplam);

        var orta = Assert.Single(d.Satirlar, x => x.Kirilim == "Orta");
        Assert.Equal(1m, orta.Ay("2026-01"));
        Assert.Equal(0m, orta.Ay("2026-02"));   // veri yok → 0

        var lux = Assert.Single(d.Satirlar, x => x.Kirilim == "Lüks");
        Assert.Equal(0m, lux.Ay("2026-01"));
        Assert.Equal(2m, lux.Ay("2026-02"));

        // Kolon ve genel toplamlar
        Assert.Equal(3m, d.AyToplami("2026-01"));
        Assert.Equal(3m, d.AyToplami("2026-02"));
        Assert.Equal(6m, d.GenelToplam);
    }

    [Fact]
    public async Task Gun_pivotu_ADETTEN_FARKLI_ve_elle_hesaplanabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await SenaryoAsync(sp);

        var d = await sp.GetRequiredService<ReportService>()
            .GetKarsilastirmaliAnalizAsync(Pencere(veri: "Gun"));

        Assert.Equal("Gun", d.VeriTuru);
        var eko = Assert.Single(d.Satirlar, x => x.Kirilim == "Ekonomik");
        Assert.Equal(5m, eko.Ay("2026-01"));    // 3 + 2
        Assert.Equal(4m, eko.Ay("2026-02"));
        Assert.Equal(10m, Assert.Single(d.Satirlar, x => x.Kirilim == "Lüks").Ay("2026-02"));  // 5 + 5
        Assert.Equal(20m, d.GenelToplam);       // adet modunda 6 idi
    }

    [Fact]
    public async Task Kirilim_boyutu_degisince_SATIRLAR_degisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await SenaryoAsync(sp);
        var rapor = sp.GetRequiredService<ReportService>();

        // Rezervasyon kaynağı: Web ×3 (Ocak 2 + Şubat 1), Acente ×3 (Ocak 1 + Şubat 2)
        var kaynak = await rapor.GetKarsilastirmaliAnalizAsync(Pencere(kirilim: "RezKaynagi"));
        Assert.Equal(2, kaynak.Satirlar.Count);
        var web = Assert.Single(kaynak.Satirlar, x => x.Kirilim == "Web");
        Assert.Equal(2m, web.Ay("2026-01"));
        Assert.Equal(1m, web.Ay("2026-02"));
        Assert.Equal(3m, Assert.Single(kaynak.Satirlar, x => x.Kirilim == "Acente").Toplam);

        // Çıkış noktası: Merkez ×3 (hepsi Ocak), Şube2 ×3 (hepsi Şubat)
        var ofis = await rapor.GetKarsilastirmaliAnalizAsync(Pencere(kirilim: "CikisNoktasi"));
        var merkez = Assert.Single(ofis.Satirlar, x => x.Kirilim == "Merkez");
        Assert.Equal(3m, merkez.Ay("2026-01"));
        Assert.Equal(0m, merkez.Ay("2026-02"));
        Assert.Equal(3m, Assert.Single(ofis.Satirlar, x => x.Kirilim == "Şube2").Ay("2026-02"));
    }

    [Fact]
    public async Task Bos_ay_KOLON_olarak_KALIR_veriden_turetilmiyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await SenaryoAsync(sp);

        // Pencereyi Mart'a kadar uzat: Mart'ta HİÇ kira yok ama kolon görünmeli.
        var d = await sp.GetRequiredService<ReportService>().GetKarsilastirmaliAnalizAsync(
            new KarsilastirmaliAnalizFilter
            { Bas = Ocak.AddDays(-9), Bit = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero) });

        Assert.Equal(["2026-01", "2026-02", "2026-03"], d.AyAnahtarlari);
        Assert.Equal(0m, d.AyToplami("2026-03"));    // boş ay kolonu duruyor
        Assert.Equal(6m, d.GenelToplam);             // toplam değişmedi
    }

    [Fact]
    public async Task Sube_filtresi_ve_iptal_kirasi_dogru_daraltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var ilkMerkez = await SenaryoAsync(sp);
        var rapor = sp.GetRequiredService<ReportService>();

        var f = Pencere();
        f.Ofis = "Merkez";
        var merkez = await rapor.GetKarsilastirmaliAnalizAsync(f);
        Assert.Equal(3m, merkez.GenelToplam);        // Merkez'den 3 kira
        Assert.Equal(0m, merkez.AyToplami("2026-02"));

        // İptal edilen kira sayılmamalı.
        await sp.GetRequiredService<RentalService>().CancelAsync(ilkMerkez);

        var sonra = await rapor.GetKarsilastirmaliAnalizAsync(Pencere());
        Assert.Equal(5m, sonra.GenelToplam);         // 6 → 5
    }

    [Fact]
    public async Task Rezervasyon_tablosu_KIRADAN_bagimsiz_sayar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await SenaryoAsync(sp);

        // Kiralar var ama hiç rezervasyon yok → Rezervasyon tablosu BOŞ dönmeli.
        var f = Pencere();
        f.Tablo = "Rezervasyon";
        var d = await sp.GetRequiredService<ReportService>().GetKarsilastirmaliAnalizAsync(f);
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
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Grupsuz" });
        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 NG 01" });   // grup YOK
        await KiraAsync(sp, cari, arac, Ocak, 2);

        var d = await sp.GetRequiredService<ReportService>().GetKarsilastirmaliAnalizAsync(Pencere());
        // Grupsuz araç sessizce DÜŞMEMELİ — ayrı bir satırda görünmeli.
        Assert.Equal("(belirtilmemiş)", Assert.Single(d.Satirlar).Kirilim);
        Assert.Equal(1m, d.GenelToplam);
    }

    [Fact]
    public async Task Pivot_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid())) await SenaryoAsync(s1.ServiceProvider);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var d = await s2.ServiceProvider.GetRequiredService<ReportService>()
            .GetKarsilastirmaliAnalizAsync(Pencere());
        Assert.Empty(d.Satirlar);
        Assert.Equal(0m, d.GenelToplam);
    }
}
