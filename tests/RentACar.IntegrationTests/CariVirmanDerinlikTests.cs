using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-59 — Cari virman künye alanları + geçmiş listesi.
///
/// <para><b>Bu fazın kırmızı çizgisi:</b> dengeli çift kayıt mantığı DEĞİŞMEYECEK. Künye ayrı bir
/// tabloda ve PARA TAŞIMIYOR; liste tutarı her zaman DEFTERDEN okuyor. Aşağıdaki testler hem
/// bakiye etkisinin aynı kaldığını hem de listedeki tutarın ekstredekiyle ayrışmadığını
/// doğruluyor.</para>
///
/// <para><b>Tarih artık elle giriliyor</b> (önce her zaman "şimdi"ydi) → dönem kilidi de VERİLEN
/// tarihe göre kontrol edilmeli; ayrı test bunu kilitliyor.</para>
///
/// <para>Bağımsız oracle: tutarlar ve beklenen bakiyeler testte elle hesaplanır.</para>
/// </summary>
[Collection("postgres")]
public sealed class CariVirmanDerinlikTests(PostgresFixture fx)
{
    private static async Task<Guid> CariAsync(IServiceProvider sp, string unvan)
        => await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = unvan });

    [Fact]
    public async Task Kunye_alanlari_round_trip_ve_BAKIYE_ETKISI_AYNI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muhasebeci");
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "Kaynak A.Ş.");
        var b = await CariAsync(sp, "Hedef Ltd.");

        var tarih = TestZaman.Simdi().AddDays(-3);
        var vade = tarih.AddDays(30);
        await cash.TransferBetweenCariAsync(a, b, 2500m,
            aciklama: "Grup içi mahsup", tarih: tarih, vade: vade,
            makbuzNo: " MKB-77 ", sube: " Merkez ");

        // ELLE: kaynak −2500 (alacaklandı), hedef +2500 (borçlandı). Toplam etki 0 (dengeli).
        Assert.Equal(-2500m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(2500m, await cash.GetCariBalanceAsync(b));

        var v = Assert.Single(await cash.ListCariVirmanlarAsync());
        Assert.Equal(a, v.KaynakCariId);
        Assert.Equal("Kaynak A.Ş.", v.KaynakCariAd);
        Assert.Equal(b, v.HedefCariId);
        Assert.Equal("Hedef Ltd.", v.HedefCariAd);
        Assert.Equal(tarih, v.Tarih);
        Assert.Equal(vade, v.Vade);
        Assert.Equal("MKB-77", v.MakbuzNo);          // trim
        Assert.Equal("Merkez", v.Sube);
        Assert.Equal("Grup içi mahsup", v.Aciklama);
        Assert.Equal("muhasebeci", v.IslemYapan);    // oturumdan, formdan DEĞİL

        // Tutar DEFTERDEN geliyor: listedeki rakam ekstredekiyle AYNI olmalı.
        Assert.Equal(2500m, v.Tutar);
        Assert.Equal(2500m, v.TutarTl);
        var ekstre = await cash.GetStatementAsync(b);
        Assert.Equal(2500m, Assert.Single(ekstre.Satirlar, e => e.SourceType == "CariVirman").Amount.Amount);
    }

    [Fact]
    public async Task Manuel_tarih_DEFTERE_de_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var b = await CariAsync(sp, "B");

        var gecmis = TestZaman.Simdi().AddDays(-20);
        await cash.TransferBetweenCariAsync(a, b, 100m, tarih: gecmis);

        // Defter satırının tarihi verilen tarih olmalı — künye ile defter ayrışamaz.
        var satir = Assert.Single((await cash.GetStatementAsync(b)).Satirlar);
        Assert.Equal(gecmis, satir.EntryDateUtc);
        Assert.Equal(gecmis, Assert.Single(await cash.ListCariVirmanlarAsync()).Tarih);
    }

    [Fact]
    public async Task Gelecek_tarih_ve_ters_vade_reddedilir_kayit_OLUSMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var b = await CariAsync(sp, "B");

        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(
            a, b, 100m, tarih: TestZaman.Simdi().AddDays(10)));

        var t = TestZaman.Simdi();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenCariAsync(
            a, b, 100m, tarih: t, vade: t.AddDays(-5)));
        Assert.Contains("Vade tarihi", ex.Message);

        // Reddedilen çağrılar ne deftere ne künyeye yazmış olmalı.
        Assert.Empty(await cash.ListCariVirmanlarAsync());
        Assert.Equal(0m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(b));
    }

    [Fact]
    public async Task Kunyesiz_cagri_ESKI_davranisi_korur()
    {
        // Yeni parametrelerin hiçbiri verilmeden eski çağrı biçimi çalışmalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var b = await CariAsync(sp, "B");

        await cash.TransferBetweenCariAsync(a, b, 750m);

        Assert.Equal(-750m, await cash.GetCariBalanceAsync(a));
        Assert.Equal(750m, await cash.GetCariBalanceAsync(b));
        var v = Assert.Single(await cash.ListCariVirmanlarAsync());
        Assert.Null(v.Vade);
        Assert.Null(v.MakbuzNo);
        Assert.Null(v.Sube);
        Assert.True(v.Tarih <= DateTimeOffset.UtcNow.AddMinutes(1));   // "şimdi"ye düştü
    }

    [Fact]
    public async Task Cift_submit_TEK_virman_ve_TEK_kunye_birakir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var b = await CariAsync(sp, "B");

        var token = Guid.NewGuid();
        await cash.TransferBetweenCariAsync(a, b, 300m, islemAnahtari: token, makbuzNo: "MK-1");
        await cash.TransferBetweenCariAsync(a, b, 300m, islemAnahtari: token, makbuzNo: "MK-1");

        // Defter TEK kere borçlandırmalı…
        Assert.Equal(300m, await cash.GetCariBalanceAsync(b));
        // …künye de TEK satır olmalı (defterle aynı transaction'da yazıldığı için ikisi ayrışamaz).
        Assert.Single(await cash.ListCariVirmanlarAsync());
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "Alfa");
        var b = await CariAsync(sp, "Beta");
        var c = await CariAsync(sp, "Gama");

        var t = TestZaman.Simdi();
        await cash.TransferBetweenCariAsync(a, b, 100m, tarih: t.AddDays(-30), makbuzNo: "MK-A", sube: "Merkez");
        await cash.TransferBetweenCariAsync(b, c, 200m, tarih: t.AddDays(-10), makbuzNo: "MK-B", sube: "Şube2");
        await cash.TransferBetweenCariAsync(c, a, 300m, tarih: t.AddDays(-1), makbuzNo: "MK-C", aciklama: "son virman");

        // ELLE: 3 virman.
        Assert.Equal(3, (await cash.ListCariVirmanlarAsync()).Count);
        Assert.Equal(3, (await cash.ListCariVirmanlarAsync(new CariVirmanFilter())).Count);

        // Cari filtresi KAYNAK ya da HEDEF olmayı kapsar: a → 1. ve 3. virman.
        Assert.Equal(2, (await cash.ListCariVirmanlarAsync(new CariVirmanFilter { CariId = a })).Count);
        Assert.Equal(2, (await cash.ListCariVirmanlarAsync(new CariVirmanFilter { CariId = b })).Count);

        // Metin: makbuz / açıklama / şube
        Assert.Equal("MK-B", Assert.Single(await cash.ListCariVirmanlarAsync(
            new CariVirmanFilter { Ara = "mk-b" })).MakbuzNo);
        Assert.Equal("Şube2", Assert.Single(await cash.ListCariVirmanlarAsync(
            new CariVirmanFilter { Ara = "şube2" })).Sube);
        Assert.Equal("son virman", Assert.Single(await cash.ListCariVirmanlarAsync(
            new CariVirmanFilter { Ara = "son vir" })).Aciklama);

        // Tarih: son 15 gün → 2 kayıt
        Assert.Equal(2, (await cash.ListCariVirmanlarAsync(new CariVirmanFilter { Bas = t.AddDays(-15) })).Count);
        // Kapalı aralık → yalnız ortadaki
        Assert.Equal("MK-B", Assert.Single(await cash.ListCariVirmanlarAsync(
            new CariVirmanFilter { Bas = t.AddDays(-15), Bit = t.AddDays(-5) })).MakbuzNo);

        // Sıralama: en yeni önce.
        var hepsi = await cash.ListCariVirmanlarAsync();
        Assert.Equal("MK-C", hepsi[0].MakbuzNo);
    }

    [Fact]
    public async Task Kunyesiz_ESKI_defter_kaydi_listede_GORUNMEZ_ama_ekstrede_DURUR()
    {
        // Bu faz öncesi yazılmış virmanların künyesi yok. Onları listede göstermek için künye
        // uydurmak (vade/makbuz/şube) yanlış olurdu; ekstrede olduğu gibi duruyorlar.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CariAsync(sp, "A");
        var b = await CariAsync(sp, "B");

        await cash.TransferBetweenCariAsync(a, b, 500m);
        // Künyeyi sil → "eski kayıt" durumunu taklit et.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CariVirmanBilgileri.RemoveRange(await db.CariVirmanBilgileri.ToListAsync());
            await db.SaveChangesAsync();
        }

        Assert.Empty(await cash.ListCariVirmanlarAsync());
        // Defter DOKUNULMAMIŞ: bakiye ve ekstre yerinde.
        Assert.Equal(500m, await cash.GetCariBalanceAsync(b));
        Assert.Single((await cash.GetStatementAsync(b)).Satirlar);
    }

    [Fact]
    public async Task Virman_gecmisi_tenant_izolasyonlu_ve_yetki_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
        {
            var sp = s1.ServiceProvider;
            await sp.GetRequiredService<CashService>().TransferBetweenCariAsync(
                await CariAsync(sp, "Gizli A"), await CariAsync(sp, "Gizli B"), 100m, makbuzNo: "GIZLI");
        }

        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var cash = s2.ServiceProvider.GetRequiredService<CashService>();
            Assert.Empty(await cash.ListCariVirmanlarAsync());
            Assert.Empty(await cash.ListCariVirmanlarAsync(new CariVirmanFilter { Ara = "GIZLI" }));
        }

        // Operatör: ViewReports yok → göremez.
        using var op = host.ScopeFor(t1, Guid.NewGuid(), "op", UserRole.Operator);
        await Assert.ThrowsAsync<YetkiYokException>(
            () => op.ServiceProvider.GetRequiredService<CashService>().ListCariVirmanlarAsync());
    }
}
