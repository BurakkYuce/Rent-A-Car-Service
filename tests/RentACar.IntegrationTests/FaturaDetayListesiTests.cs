using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-52 — Fatura detay (satır) listesi.
///
/// <para><b>Bağımsız oracle:</b> faturalar elle kesilir; beklenen satır sayıları ve tutarlar testte
/// SABİT yazılır (ör. "net 1.000 @%20 → satır toplamı 1.200"), servisin dönüşünden türetilmez.</para>
///
/// <para><b>Bu ekranın üç yapısal riski, üçü de ayrı testte kilitli:</b></para>
/// <list type="number">
///   <item>Kiraya bağlı OLMAYAN manuel faturaların JOIN'de düşmesi (LEFT JOIN olmalı).</item>
///   <item><b>İade faturalarının toplamı ARTIRMASI.</b> İade faturaları DB'de POZİTİF tutarla
///   saklanıyor (<c>CreateIadeAsync</c> satırları kaynak faturadan aynen aynalıyor; ters yön yalnız
///   defterde). Tüketici işareti kendisi vermezse bir iade ciroyu düşürmek yerine artırır.</item>
///   <item>Çok-dövizli satırların TL'ye çevrilmeden toplanması.</item>
/// </list>
/// </summary>
[Collection("postgres")]
public sealed class FaturaDetayListesiTests(PostgresFixture fx)
{
    private static async Task<Guid> CariAsync(IServiceProvider sp, string unvan, string? il = null, string? mail = null)
        => await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        { Tip = CustomerType.Kurumsal, Unvan = unvan, Il = il, Email = mail });

    private static Task<Guid> ManuelAsync(
        IServiceProvider sp, Guid cari, string aciklama, decimal net,
        decimal kdv = 0.20m, string? doviz = null, decimal? kur = null)
    {
        var input = new ManualInvoiceInput { CariId = cari, Aciklama = aciklama, NetTutar = net, KdvOrani = kdv };
        return sp.GetRequiredService<InvoiceService>().CreateManualAsync(input);
    }

    [Fact]
    public async Task Manuel_fatura_satiri_KIRASIZ_da_listelenir_ve_cari_bilgisi_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cari = await CariAsync(sp, "Alfa A.Ş.", "İstanbul", "alfa@ornek.test");

        // ELLE: net 1.000, KDV %20 → satır 1.000 net / 200 kdv / 1.200 toplam.
        await ManuelAsync(sp, cari, "Danışmanlık", 1000m);

        var satir = Assert.Single(await sp.GetRequiredService<InvoiceService>().ListLinesAsync());
        Assert.Equal(1000m, satir.SatirNet);
        Assert.Equal(200m, satir.SatirKdv);
        Assert.Equal(1200m, satir.SatirToplam);
        Assert.Equal("Alfa A.Ş.", satir.CariAd);
        Assert.Equal("İstanbul", satir.CariSehir);
        Assert.Equal("alfa@ornek.test", satir.CariEmail);
        Assert.True(satir.ManuelMi);

        // KRİTİK: kirası olmayan fatura JOIN'de DÜŞMEMELİ (LEFT JOIN).
        Assert.Null(satir.RentalId);
        Assert.Null(satir.Plaka);
        Assert.Null(satir.SozlesmeNo);
        Assert.Null(satir.CikisOfisi);
    }

    [Fact]
    public async Task Kiraya_bagli_faturada_plaka_sozlesme_ve_ofis_COZULUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cari = await CariAsync(sp, "Beta Turizm");
        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 FD 01" });

        var bas = TestZaman.Simdi().AddDays(-10);
        var kiraId = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = cari, VehicleId = arac, BasTar = bas, BitTar = bas.AddDays(3),
            GunlukUcret = 1000m, CikisOfisi = "Merkez Ofis"
        });
        await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(kiraId);

        var satirlar = await sp.GetRequiredService<InvoiceService>().ListLinesAsync();
        Assert.NotEmpty(satirlar);
        Assert.All(satirlar, r =>
        {
            Assert.Equal(kiraId, r.RentalId);
            Assert.Equal("34FD01", r.Plaka);           // plaka DB'de normalize saklanıyor
            Assert.Equal("Merkez Ofis", r.CikisOfisi);
            Assert.False(string.IsNullOrWhiteSpace(r.SozlesmeNo));
            Assert.False(r.Iptal);
            Assert.False(r.IadeMi);
        });
    }

    [Fact]
    public async Task IADE_satirlari_toplami_DUSURUR_artirmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var inv = sp.GetRequiredService<InvoiceService>();
        var cari = await CariAsync(sp, "Gama");

        // ELLE: 2 fatura — 1.000 net (brüt 1.200) ve 500 net (brüt 600). İkincisi iade edilir.
        await ManuelAsync(sp, cari, "Kalır", 1000m);
        var iadeEdilecek = await ManuelAsync(sp, cari, "İade edilecek", 500m);
        await inv.CreateRefundAsync(iadeEdilecek);

        var satirlar = await inv.ListLinesAsync();
        Assert.Equal(3, satirlar.Count);                                   // 2 fatura + 1 iade
        var iade = Assert.Single(satirlar, r => r.IadeMi);

        // İade satırı DB'de POZİTİF saklanıyor — ham tutar kaynağın aynısı.
        Assert.Equal(600m, iade.SatirToplam);
        // …ama işaretli toplamda NEGATİF görünmeli.
        Assert.Equal(-600m, iade.IsaretliToplamTl);
        Assert.Equal(-500m, iade.IsaretliNetTl);
        Assert.Equal(-100m, iade.IsaretliKdvTl);

        // Ekrandaki toplam: 1.200 + 600 − 600 = 1.200. İşaret verilmeseydi 2.400 çıkardı.
        Assert.Equal(1200m, satirlar.Where(r => !r.Iptal).Sum(r => r.IsaretliToplamTl));
        Assert.Equal(1000m, satirlar.Where(r => !r.Iptal).Sum(r => r.IsaretliNetTl));
        Assert.Equal(200m, satirlar.Where(r => !r.Iptal).Sum(r => r.IsaretliKdvTl));
    }

    [Fact]
    public async Task Dovizli_satir_TL_baza_cevrilerek_toplanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cari = await CariAsync(sp, "Delta");

        // TRY fatura: net 100 @%20 → brüt 120, kur 1 → TL 120.
        await ManuelAsync(sp, cari, "TL kalem", 100m);

        var satir = Assert.Single(await sp.GetRequiredService<InvoiceService>().ListLinesAsync());
        Assert.Equal("TRY", satir.Doviz);
        Assert.Equal(1m, satir.Kur);
        // Kur 1 olduğu için ham ile TL-baz eşit; formülün kuru ÇARPTIĞI burada kilitlenir.
        Assert.Equal(satir.SatirToplam * satir.Kur, satir.IsaretliToplamTl);
        Assert.Equal(120m, satir.IsaretliToplamTl);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var inv = sp.GetRequiredService<InvoiceService>();

        var a = await CariAsync(sp, "Alfa Lojistik");
        var b = await CariAsync(sp, "Beta Turizm");
        await ManuelAsync(sp, a, "Kiralama bedeli", 1000m);
        await ManuelAsync(sp, b, "Temizlik", 200m);
        await ManuelAsync(sp, b, "Yakıt farkı", 300m);

        // ELLE: 3 fatura → 3 kalem (manuel fatura tek satırlıdır).
        Assert.Equal(3, (await inv.ListLinesAsync()).Count);
        Assert.Equal(3, (await inv.ListLinesAsync(new FaturaSatirFilter())).Count);

        Assert.Single(await inv.ListLinesAsync(new FaturaSatirFilter { CariId = a }));
        Assert.Equal(2, (await inv.ListLinesAsync(new FaturaSatirFilter { CariId = b })).Count);

        // Metin araması satır açıklamasında da çalışmalı (küçük-büyük harf duyarsız).
        Assert.Contains("Yakıt",
            Assert.Single(await inv.ListLinesAsync(new FaturaSatirFilter { Ara = "yakıt" })).Aciklama);
        Assert.Empty(await inv.ListLinesAsync(new FaturaSatirFilter { Ara = "yok-boyle-bir-sey" }));

        var bugun = TestZaman.Simdi();
        Assert.Equal(3, (await inv.ListLinesAsync(new FaturaSatirFilter { Bas = bugun.AddDays(-1) })).Count);
        Assert.Empty(await inv.ListLinesAsync(new FaturaSatirFilter { Bit = bugun.AddDays(-1) }));

        // Eşleşmeyen plaka/ofis: manuel faturaların kirası yok → bu filtreler hepsini eler.
        Assert.Empty(await inv.ListLinesAsync(new FaturaSatirFilter { Plaka = "34" }));
        Assert.Empty(await inv.ListLinesAsync(new FaturaSatirFilter { Ofis = "Merkez" }));
    }

    [Fact]
    public async Task Export_kolon_sozlesmesi_ve_satir_sayisi_SABIT()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cari = await CariAsync(sp, "Epsilon", "Ankara");
        await ManuelAsync(sp, cari, "Kalem 1", 100m);
        await ManuelAsync(sp, cari, "Kalem 2", 150m);

        var t = ListExportCatalog.FaturaDetaylari(await sp.GetRequiredService<InvoiceService>().ListLinesAsync());
        Assert.Equal("Fatura Detay", t.Sheet);
        Assert.Equal(21, t.Headers.Count);
        Assert.Equal("Fatura No", t.Headers[0]);
        Assert.Equal("Rez. Kaynağı", t.Headers[^1]);
        Assert.Equal(2, t.Rows.Count);
        // Her satır başlık sayısı kadar hücre taşımalı (kolon eklerken en sık hata bu).
        Assert.All(t.Rows, r => Assert.Equal(t.Headers.Count, r.Length));
    }

    [Fact]
    public async Task Yetkisiz_rol_detay_listesini_GOREMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
            await ManuelAsync(admin.ServiceProvider, await CariAsync(admin.ServiceProvider, "Zeta"), "X", 10m);

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(
            () => op.ServiceProvider.GetRequiredService<InvoiceService>().ListLinesAsync());
    }

    [Fact]
    public async Task Detay_listesi_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            await ManuelAsync(s1.ServiceProvider, await CariAsync(s1.ServiceProvider, "Gizli"), "Gizli kalem", 999m);

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var inv = s2.ServiceProvider.GetRequiredService<InvoiceService>();
        Assert.Empty(await inv.ListLinesAsync());
        Assert.Empty(await inv.ListLinesAsync(new FaturaSatirFilter { Ara = "Gizli" }));
    }
}
