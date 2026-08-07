using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-62 — Cari bakiye raporu: ayrı Borç/Alacak kolonları + kart bilgileri + filtre paneli.
///
/// <para><b>Bağımsız oracle:</b> senaryolar elle kurulur ve beklenen üç değer (ToplamBorc,
/// ToplamAlacak, net Bakiye) testte ELLE hesaplanır — servisin kendi toplamından türetilmez.</para>
///
/// <para><b>Regresyon çiti:</b> net <c>Bakiye</c> hesabı bu fazda DEĞİŞMEDİ. Aşağıdaki testler
/// net'in hâlâ <c>Σ Borç − Σ Alacak</c> olduğunu ve filtresiz çağrının eski davranışı BİREBİR
/// koruduğunu ayrıca doğruluyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class CariBakiyeFiltreTests(PostgresFixture fx)
{
    private static async Task<Guid> CariAsync(IServiceScope scope, Action<Customer> kur)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var c = new Customer { Tip = CariType.Bireysel };
        kur(c);
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    private static async Task<Guid> AracAsync(IServiceScope scope, string plaka)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var v = new Vehicle { Plaka = plaka, Durum = VehicleStatus.Musait };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    [Fact]
    public async Task Brut_borc_ve_alacak_AYRI_net_bakiye_DEGISMEDI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var sales = sp.GetRequiredService<VehicleSaleService>();
        var cash = sp.GetRequiredService<CashService>();
        var reports = sp.GetRequiredService<ReportService>();

        var cari = await CariAsync(scope, c => { c.Ad = "Brut"; c.Soyad = "Test"; });
        var v1 = await AracAsync(scope, "34 BF 01");
        var v2 = await AracAsync(scope, "34 BF 02");

        // ELLE: 2 borç hareketi + 1 alacak hareketi.
        //   satış net 1000 @%20 → borç 1200
        //   satış net  500 @%20 → borç  600   → ToplamBorc = 1800
        //   tahsilat 300                       → ToplamAlacak = 300
        //   net bakiye = 1800 − 300 = 1500
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = v1, AliciCariId = cari, SatisNet = 1000m, KdvOrani = 0.20m });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = v2, AliciCariId = cari, SatisNet = 500m, KdvOrani = 0.20m });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 300m });

        var b = Assert.Single(await reports.GetCariBalancesAsync());
        Assert.Equal(1800m, b.ToplamBorc);
        Assert.Equal(300m, b.ToplamAlacak);
        Assert.Equal(1500m, b.Bakiye);
        // Net, brütlerin farkı olmalı — iki hesap birbirinden ayrışamaz.
        Assert.Equal(b.ToplamBorc - b.ToplamAlacak, b.Bakiye);
    }

    [Fact]
    public async Task Kart_bilgileri_kolonlara_TASINIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var cari = await CariAsync(scope, c =>
        {
            c.Tip = CariType.Kurumsal; c.Unvan = "Acme A.Ş.";
            c.CepTel = "05551112233"; c.Email = "muhasebe@acme.test";
            c.BankaAdi = "Ziraat"; c.Doviz = "EURO"; c.OzelCariTip = "Grup İçi"; c.Sinif = "Kurumsal";
        });
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = cari, Tutar = 100m });

        var b = Assert.Single(await sp.GetRequiredService<ReportService>().GetCariBalancesAsync());
        Assert.Equal("Acme A.Ş.", b.Ad);
        Assert.Equal("05551112233", b.Telefon);
        Assert.Equal("muhasebe@acme.test", b.Email);
        Assert.Equal("Ziraat", b.Banka);
        Assert.Equal("EURO", b.Doviz);
        Assert.Equal("Grup İçi", b.OzelKod);
        Assert.True(b.Kurumsal);
        Assert.Equal(-100m, b.Bakiye);      // yalnız tahsilat → alacaklı
        Assert.Equal(0m, b.ToplamBorc);
        Assert.Equal(100m, b.ToplamAlacak);
    }

    /// <summary>Üç farklı profilde cari kurar; her filtre testi bu tabandan beklenen ADI seçer.</summary>
    private static async Task<(Guid borclu, Guid alacakli, Guid kucuk)> SenaryoAsync(IServiceScope scope)
    {
        var sp = scope.ServiceProvider;
        var sales = sp.GetRequiredService<VehicleSaleService>();
        var cash = sp.GetRequiredService<CashService>();

        // A: kurumsal, TL, "Yurtiçi" — satış net 1000 @%20 → borç 1200 (borçlu)
        var a = await CariAsync(scope, c =>
        { c.Tip = CariType.Kurumsal; c.Unvan = "Alfa Lojistik"; c.Doviz = "TL"; c.OzelCariTip = "Yurtiçi"; c.CepTel = "05320001122"; });
        await sales.CreateAsync(new VehicleSaleInput
        { VehicleId = await AracAsync(scope, "34 FL 01"), AliciCariId = a, SatisNet = 1000m, KdvOrani = 0.20m });

        // B: bireysel, EURO, "Yurtdışı" — tahsilat 800 → bakiye −800 (alacaklı)
        var b = await CariAsync(scope, c =>
        { c.Ad = "Beta"; c.Soyad = "Yılmaz"; c.Doviz = "EURO"; c.OzelCariTip = "Yurtdışı"; c.Email = "beta@ornek.test"; });
        await cash.CollectAsync(new CashInput { CariId = b, Tutar = 800m });

        // C: bireysel, TL, "Yurtiçi" — tahsilat 50 → bakiye −50 (küçük)
        var c3 = await CariAsync(scope, c =>
        { c.Ad = "Cem"; c.Soyad = "Küçük"; c.Doviz = "TL"; c.OzelCariTip = "Yurtiçi"; });
        await cash.CollectAsync(new CashInput { CariId = c3, Tutar = 50m });

        return (a, b, c3);
    }

    [Fact]
    public async Task Filtresiz_cagri_ESKI_davranisi_birebir_korur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await SenaryoAsync(scope);
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();

        // Elle: 3 cari, hepsinin bakiyesi sıfırdan farklı → 3 satır, borçtan alacağa sıralı.
        var hepsi = await reports.GetCariBalancesAsync();
        Assert.Equal(3, hepsi.Count);
        Assert.Equal(1200m, hepsi[0].Bakiye);     // en yüksek önce
        Assert.Equal(-50m, hepsi[1].Bakiye);
        Assert.Equal(-800m, hepsi[2].Bakiye);
        // null filtre ile boş filtre nesnesi AYNI sonucu vermeli (filtre eklenmiş olması daraltmasın).
        Assert.Equal(3, (await reports.GetCariBalancesAsync(new CariBakiyeFilter())).Count);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (a, b, kucuk) = await SenaryoAsync(scope);
        var r = scope.ServiceProvider.GetRequiredService<ReportService>();

        // Metin araması: ada göre
        Assert.Equal(a, Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { Ara = "alfa" })).CariId);
        // …telefona göre (kısmi)
        Assert.Equal(a, Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { Ara = "0532" })).CariId);
        // …e-postaya göre
        Assert.Equal(b, Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { Ara = "beta@" })).CariId);

        // Özel kod: "Yurtiçi" → A ve C
        Assert.Equal(2, (await r.GetCariBalancesAsync(new CariBakiyeFilter { OzelKod = "Yurtiçi" })).Count);
        // Döviz: EURO → yalnız B
        Assert.Equal(b, Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { Doviz = "EURO" })).CariId);
        // Cari tipi: kurumsal → yalnız A
        Assert.Equal(a, Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { Kurumsal = true })).CariId);
        Assert.Equal(2, (await r.GetCariBalancesAsync(new CariBakiyeFilter { Kurumsal = false })).Count);

        // Bakiye türü
        Assert.Equal(a, Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { BakiyeTuru = "borclu" })).CariId);
        Assert.Equal(2, (await r.GetCariBalancesAsync(new CariBakiyeFilter { BakiyeTuru = "alacakli" })).Count);

        // Min tutar MUTLAK bakiyeye uygulanır: 100 → A (1200) ve B (−800) kalır, C (−50) düşer.
        var min100 = await r.GetCariBalancesAsync(new CariBakiyeFilter { MinTutar = 100m });
        Assert.Equal(2, min100.Count);
        Assert.DoesNotContain(min100, x => x.CariId == kucuk);

        // Birleşik filtre: alacaklı VE mutlak ≥100 → yalnız B.
        Assert.Equal(b, Assert.Single(await r.GetCariBalancesAsync(
            new CariBakiyeFilter { BakiyeTuru = "alacakli", MinTutar = 100m })).CariId);
    }

    [Fact]
    public async Task Kart_bilgisi_olmayan_cari_filtreyi_COKERTMEZ()
    {
        // Defterde satırı olan ama Customers'ta karşılığı silinmiş cari ("(bilinmeyen cari)") —
        // kart join'i null döner; kolonlar boş kalmalı, filtre patlamamalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(scope, c => { c.Ad = "Silinecek"; c.Soyad = "Cari"; });
        await sp.GetRequiredService<CashService>().CollectAsync(new CashInput { CariId = cari, Tutar = 250m });

        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Customers.Remove(await db.Customers.FirstAsync(c => c.Id == cari));
            await db.SaveChangesAsync();
        }

        var r = sp.GetRequiredService<ReportService>();
        var satir = Assert.Single(await r.GetCariBalancesAsync());
        Assert.Equal(-250m, satir.Bakiye);
        Assert.Null(satir.Telefon);
        Assert.Null(satir.Doviz);
        Assert.False(satir.Kurumsal);
        // Filtre yine çalışır (null alanlar eşleşmez, istisna atmaz).
        Assert.Empty(await r.GetCariBalancesAsync(new CariBakiyeFilter { Doviz = "TL" }));
        Assert.Single(await r.GetCariBalancesAsync(new CariBakiyeFilter { MinTutar = 100m }));
    }
}
