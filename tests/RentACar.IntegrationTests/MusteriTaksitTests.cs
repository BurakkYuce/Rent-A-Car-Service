using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.MusteriTaksitleri;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-66 — müşteri taksit takibi (canlı <c>kredi_takip_listesi.aspx</c>).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler ELLE kurulan senaryodan yazılır, servisten
/// türetilmez. Örn. 10.000 / 3 taksit → 3333,33 + 3333,33 + <b>3333,34</b> (kalan-yöntemi);
/// toplam tam 10.000. 12 aylık plan → 12. vade ilk vadeden 11 ay sonra.</para>
///
/// <para><b>DEFTERE YAZMAZ:</b> "Ödendi" işaretlemek cari bakiyeyi DEĞİŞTİRMEZ — bu bir takip
/// kaydıdır. Test bunu ampirik olarak kilitler (defter satır sayısı sabit kalır).</para>
/// </summary>
[Collection("postgres")]
public sealed class MusteriTaksitTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int y, int m, int g) => new(y, m, g, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Guid> CariAsync(IServiceProvider sp, string ad = "Taksitli Müşteri")
        => await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test" });

    [Fact]
    public async Task Plan_ureti_kalan_yontemiyle_TAM_toplama_esit()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari = await CariAsync(sp);

        // ELLE: 10.000 TL, 3 taksit → 10000/3 = 3333,333… → 3333,33 · 3333,33 · KALAN 3333,34
        var adet = await svc.PlanUretAsync(new TaksitPlanInput
        { CariId = cari, ToplamTutar = 10_000m, TaksitSayisi = 3, IlkVade = D(2026, 9, 15) });
        Assert.Equal(3, adet);

        var rows = (await svc.SearchAsync(new MusteriTaksitFilter { CariId = cari })).OrderBy(x => x.Sira).ToList();
        Assert.Equal([3333.33m, 3333.33m, 3333.34m], rows.Select(x => x.TaksitTutari));
        Assert.Equal(10_000m, rows.Sum(x => x.TaksitTutari));   // kuruş kayması YOK
        Assert.Equal([1, 2, 3], rows.Select(x => x.Sira));

        // Vadeler AYLIK: 15.09 / 15.10 / 15.11 (elle)
        Assert.Equal([D(2026, 9, 15), D(2026, 10, 15), D(2026, 11, 15)], rows.Select(x => x.Vade));
        Assert.All(rows, r => Assert.Equal(TaksitDurum.Bekliyor, r.Durum));
        Assert.All(rows, r => Assert.Null(r.OdemeTarihi));
    }

    [Fact]
    public async Task Plan_12_aylik_son_vade_11_ay_sonra_ve_sira_devam_eder()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari = await CariAsync(sp);

        await svc.PlanUretAsync(new TaksitPlanInput
        { CariId = cari, ToplamTutar = 120_000m, TaksitSayisi = 12, IlkVade = D(2026, 1, 31) });

        var rows = (await svc.SearchAsync(new MusteriTaksitFilter { CariId = cari })).OrderBy(x => x.Sira).ToList();
        Assert.Equal(12, rows.Count);
        Assert.All(rows, r => Assert.Equal(10_000m, r.TaksitTutari));   // 120000/12 tam bölünür
        // ELLE: 31 Ocak + 1 ay = 28 Şubat (AddMonths ay-sonu kırpar) — 12. taksit 31 Aralık.
        Assert.Equal(D(2026, 2, 28), rows[1].Vade);
        Assert.Equal(D(2026, 12, 31), rows[11].Vade);

        // İKİNCİ plan aynı cari+araçsız → sıra 13'ten devam eder (13..15), 1'e dönmez.
        await svc.PlanUretAsync(new TaksitPlanInput
        { CariId = cari, ToplamTutar = 300m, TaksitSayisi = 3, IlkVade = D(2027, 1, 15) });
        var hepsi = (await svc.SearchAsync(new MusteriTaksitFilter { CariId = cari })).ToList();
        Assert.Equal(15, hepsi.Count);
        Assert.Equal([13, 14, 15], hepsi.Where(x => x.TaksitTutari == 100m).OrderBy(x => x.Sira).Select(x => x.Sira));
    }

    [Fact]
    public async Task Gecikme_TURETILIR_kolon_degil_ve_odeme_isareti_geri_alinabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari = await CariAsync(sp);
        var bugun = DateTimeOffset.UtcNow;

        // ELLE: biri 10 gün GEÇMİŞ vadeli, biri 10 gün SONRA.
        var gecmis = await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = bugun.AddDays(-10), TaksitTutari = 500m });
        var gelecek = await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = bugun.AddDays(10), TaksitTutari = 500m });

        Assert.True((await svc.GetAsync(gecmis))!.Gecikti);
        Assert.False((await svc.GetAsync(gelecek))!.Gecikti);

        // Filtre: yalnız gecikenler → 1 satır (bellekte süzülür, kolon yok)
        var gecikenler = await svc.SearchAsync(new MusteriTaksitFilter { SadeceGecikmis = true });
        Assert.Equal(gecmis, Assert.Single(gecikenler).Id);

        // Ödendi işaretlenince GECİKMİŞ SAYILMAZ — vadesi geçmiş olsa bile.
        Assert.True(await svc.OdemeIsaretleAsync(gecmis, odendi: true, tarih: bugun.AddDays(-1)));
        var odenmis = (await svc.GetAsync(gecmis))!;
        Assert.Equal(TaksitDurum.Odendi, odenmis.Durum);
        Assert.False(odenmis.Gecikti);
        Assert.NotNull(odenmis.OdemeTarihi);
        Assert.Empty(await svc.SearchAsync(new MusteriTaksitFilter { SadeceGecikmis = true }));

        // GERİ AL: durum Bekliyor'a döner VE ödeme tarihi TEMİZLENİR (çelişkili satır kalmaz).
        Assert.True(await svc.OdemeIsaretleAsync(gecmis, odendi: false));
        var geri = (await svc.GetAsync(gecmis))!;
        Assert.Equal(TaksitDurum.Bekliyor, geri.Durum);
        Assert.Null(geri.OdemeTarihi);
        Assert.True(geri.Gecikti);      // tekrar gecikmiş
    }

    [Fact]
    public async Task Ozet_BAZ_para_uzerinden_hesaplanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari = await CariAsync(sp);
        var bugun = DateTimeOffset.UtcNow;

        // ELLE: 1000 TRY (kur 1) + 100 EUR (kur 40) = 1000 + 4000 = 5000 baz.
        var a = await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = bugun.AddDays(-5), TaksitTutari = 1000m });
        await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = bugun.AddDays(20), TaksitTutari = 100m, Currency = "eur", Kur = 40m });

        await svc.OdemeIsaretleAsync(a, odendi: true, tarih: bugun.AddDays(-2));

        var ozet = MusteriTaksitService.Ozet(await svc.SearchAsync());
        Assert.Equal(2, ozet.Adet);
        Assert.Equal(1, ozet.OdenenAdet);
        Assert.Equal(0, ozet.GecikenAdet);        // geçmiş vadeli olan ÖDENDİ → geciken yok
        Assert.Equal(5000m, ozet.ToplamBaz);
        Assert.Equal(1000m, ozet.OdenenBaz);
        Assert.Equal(4000m, ozet.KalanBaz);

        // Para birimi büyük harfe normalize edilir ("eur" → "EUR").
        var eur = (await svc.SearchAsync()).Single(x => x.TaksitTutari == 100m);
        Assert.Equal("EUR", eur.Currency);
        Assert.Equal(4000m, eur.TutarBaz);
    }

    [Fact]
    public async Task Dogrulamalar_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari = await CariAsync(sp);
        var bugun = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new MusteriTaksitInput
        { CariId = Guid.Empty, Vade = bugun, TaksitTutari = 100m }));                    // müşterisiz
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = bugun, TaksitTutari = 0m }));                            // tutar 0
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = null, TaksitTutari = 100m }));                           // vadesiz
        // Kur 0 → baz tutar sıfırlanırdı; negatif kur işareti çevirirdi.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = bugun, TaksitTutari = 100m, Kur = 0m }));
        // GELECEK tarihli ödeme reddi (TarihPolitikasi.ParaTarihi) — para bilgisi simetrisi.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new MusteriTaksitInput
        {
            CariId = cari, Vade = bugun, TaksitTutari = 100m,
            Durum = TaksitDurum.Odendi, OdemeTarihi = bugun.AddDays(30)
        }));

        // Plan sınırları
        await Assert.ThrowsAsync<ValidationException>(() => svc.PlanUretAsync(new TaksitPlanInput
        { CariId = cari, ToplamTutar = 1000m, TaksitSayisi = 0, IlkVade = bugun }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.PlanUretAsync(new TaksitPlanInput
        { CariId = cari, ToplamTutar = 1000m, TaksitSayisi = MusteriTaksitService.MaxTaksit + 1, IlkVade = bugun }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.PlanUretAsync(new TaksitPlanInput
        { CariId = cari, ToplamTutar = 0m, TaksitSayisi = 3, IlkVade = bugun }));

        // Hiçbiri yazılmadı.
        Assert.Empty(await svc.SearchAsync());
    }

    [Fact]
    public async Task Odeme_isareti_DEFTERE_YAZMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari = await CariAsync(sp);

        var once = await LedgerSayimAsync(sp);
        var id = await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = DateTimeOffset.UtcNow, TaksitTutari = 7500m });
        await svc.OdemeIsaretleAsync(id, odendi: true);

        // Takip kaydı — defterde TEK satır bile oluşmamalı; oluşsaydı Kasa/Banka tahsilatıyla
        // birlikte cari bakiye ÇİFT düşerdi.
        Assert.Equal(once, await LedgerSayimAsync(sp));
    }

    [Fact]
    public async Task Arac_bagi_ve_filtreler_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MusteriTaksitService>();
        var cari1 = await CariAsync(sp, "Ali");
        var cari2 = await CariAsync(sp, "Veli");
        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TK 66" });
        var bugun = DateTimeOffset.UtcNow;

        await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari1, VehicleId = arac, Vade = D(2026, 3, 10), TaksitTutari = 100m });
        await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari1, Vade = D(2026, 6, 10), TaksitTutari = 200m });
        await svc.CreateAsync(new MusteriTaksitInput
        { CariId = cari2, Vade = D(2026, 3, 20), TaksitTutari = 300m });

        Assert.Equal(3, (await svc.SearchAsync()).Count);
        Assert.Equal(2, (await svc.SearchAsync(new MusteriTaksitFilter { CariId = cari1 })).Count);
        Assert.Equal(100m, Assert.Single(await svc.SearchAsync(new MusteriTaksitFilter { VehicleId = arac })).TaksitTutari);

        // Vade aralığı: Mart ayı → 2 satır (10 ve 20 Mart), Haziran hariç.
        var mart = await svc.SearchAsync(new MusteriTaksitFilter
        { VadeMin = D(2026, 3, 1), VadeMax = D(2026, 3, 31) });
        Assert.Equal(2, mart.Count);
        Assert.DoesNotContain(mart, x => x.TaksitTutari == 200m);

        // Araçlı taksit AYRI sıra dizisinde: cari1'in araçsız ve araçlı kayıtları ikisi de 1.
        Assert.All(await svc.SearchAsync(new MusteriTaksitFilter { CariId = cari1 }), r => Assert.Equal(1, r.Sira));

        // Silme
        var sil = (await svc.SearchAsync(new MusteriTaksitFilter { CariId = cari2 })).Single();
        Assert.True(await svc.DeleteAsync(sil.Id));
        Assert.False(await svc.DeleteAsync(sil.Id));      // ikinci kez false
        Assert.Equal(2, (await svc.SearchAsync()).Count);
    }

    [Fact]
    public async Task Yetki_operator_goremez_muhasebe_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid cari;
        using (var admin = host.ScopeFor(tenant)) cari = await CariAsync(admin.ServiceProvider);

        // Operatör: ne FinanceWrite ne ViewReports → OKUYAMAZ da (para bilgisi).
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var svc = op.ServiceProvider.GetRequiredService<MusteriTaksitService>();
            await Assert.ThrowsAsync<YetkiYokException>(() => svc.SearchAsync());
            await Assert.ThrowsAsync<YetkiYokException>(() => svc.CreateAsync(new MusteriTaksitInput
            { CariId = cari, Vade = DateTimeOffset.UtcNow, TaksitTutari = 100m }));
        }

        // Muhasebe: FinanceWrite → yazar VE okur (yazan rol okuyabilmeli).
        using var mh = host.ScopeFor(tenant, Guid.NewGuid(), "mh", UserRole.Muhasebe);
        var m = mh.ServiceProvider.GetRequiredService<MusteriTaksitService>();
        await m.CreateAsync(new MusteriTaksitInput
        { CariId = cari, Vade = DateTimeOffset.UtcNow, TaksitTutari = 100m });
        Assert.Single(await m.SearchAsync());
    }

    [Fact]
    public async Task Tenant_izolasyonu_racar_app_ile()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = a.ServiceProvider;
            var cari = await CariAsync(sp, "Gizli");
            await sp.GetRequiredService<MusteriTaksitService>().CreateAsync(new MusteriTaksitInput
            { CariId = cari, Vade = DateTimeOffset.UtcNow, TaksitTutari = 99_999m });
        }

        using var b = host.ScopeFor(Guid.NewGuid());
        var svc = b.ServiceProvider.GetRequiredService<MusteriTaksitService>();
        Assert.Empty(await svc.SearchAsync());
        Assert.Equal(0m, MusteriTaksitService.Ozet(await svc.SearchAsync()).ToplamBaz);
    }

    private static async Task<int> LedgerSayimAsync(IServiceProvider sp)
    {
        var factory = sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<
            RentACar.Infrastructure.Persistence.AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .CountAsync(db.AccountLedgerEntries);
    }
}
