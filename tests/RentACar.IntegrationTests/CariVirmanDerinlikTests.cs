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
    private static async Task<Guid> CustomerAsync(IServiceProvider sp, string title)
        => await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = title });

    [Fact]
    public async Task Kunye_alanlari_round_trip_ve_BAKIYE_ETKISI_AYNI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muhasebeci");
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "Kaynak A.Ş.");
        var b = await CustomerAsync(sp, "Hedef Ltd.");

        var date = TestZaman.Now().AddDays(-3);
        var due = date.AddDays(30);
        await cash.TransferBetweenAccountsAsync(a, b, 2500m,
            description: "Grup içi mahsup", date: date, due: due,
            receiptNo: " MKB-77 ", branch: " Merkez ");

        // ELLE: kaynak −2500 (alacaklandı), hedef +2500 (borçlandı). Toplam etki 0 (dengeli).
        Assert.Equal(-2500m, await cash.GetAccountBalanceAsync(a));
        Assert.Equal(2500m, await cash.GetAccountBalanceAsync(b));

        var v = Assert.Single(await cash.ListAccountTransfersAsync());
        Assert.Equal(a, v.KaynakCariId);
        Assert.Equal("Kaynak A.Ş.", v.KaynakCariAd);
        Assert.Equal(b, v.HedefCariId);
        Assert.Equal("Hedef Ltd.", v.HedefCariAd);
        Assert.Equal(date, v.Tarih);
        Assert.Equal(due, v.Vade);
        Assert.Equal("MKB-77", v.MakbuzNo);          // trim
        Assert.Equal("Merkez", v.Sube);
        Assert.Equal("Grup içi mahsup", v.Aciklama);
        Assert.Equal("muhasebeci", v.IslemYapan);    // oturumdan, formdan DEĞİL

        // Tutar DEFTERDEN geliyor: listedeki rakam ekstredekiyle AYNI olmalı.
        Assert.Equal(2500m, v.Tutar);
        Assert.Equal(2500m, v.TutarTl);
        var statement = await cash.GetStatementAsync(b);
        Assert.Equal(2500m, Assert.Single(statement.Satirlar, e => e.SourceType == "CariVirman").Amount.Amount);
    }

    [Fact]
    public async Task Manuel_tarih_DEFTERE_de_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        var history = TestZaman.Now().AddDays(-20);
        await cash.TransferBetweenAccountsAsync(a, b, 100m, date: history);

        // Defter satırının tarihi verilen tarih olmalı — künye ile defter ayrışamaz.
        var row = Assert.Single((await cash.GetStatementAsync(b)).Satirlar);
        Assert.Equal(history, row.EntryDateUtc);
        Assert.Equal(history, Assert.Single(await cash.ListAccountTransfersAsync()).Tarih);
    }

    [Fact]
    public async Task Gelecek_tarih_ve_ters_vade_reddedilir_kayit_OLUSMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenAccountsAsync(
            a, b, 100m, date: TestZaman.Now().AddDays(10)));

        var t = TestZaman.Now();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => cash.TransferBetweenAccountsAsync(
            a, b, 100m, date: t, due: t.AddDays(-5)));
        Assert.Contains("Vade tarihi", ex.Message);

        // Reddedilen çağrılar ne deftere ne künyeye yazmış olmalı.
        Assert.Empty(await cash.ListAccountTransfersAsync());
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(a));
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(b));
    }

    [Fact]
    public async Task Kunyesiz_cagri_ESKI_davranisi_korur()
    {
        // Yeni parametrelerin hiçbiri verilmeden eski çağrı biçimi çalışmalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        await cash.TransferBetweenAccountsAsync(a, b, 750m);

        Assert.Equal(-750m, await cash.GetAccountBalanceAsync(a));
        Assert.Equal(750m, await cash.GetAccountBalanceAsync(b));
        var v = Assert.Single(await cash.ListAccountTransfersAsync());
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
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        var token = Guid.NewGuid();
        await cash.TransferBetweenAccountsAsync(a, b, 300m, operationKey: token, receiptNo: "MK-1");
        await cash.TransferBetweenAccountsAsync(a, b, 300m, operationKey: token, receiptNo: "MK-1");

        // Defter TEK kere borçlandırmalı…
        Assert.Equal(300m, await cash.GetAccountBalanceAsync(b));
        // …künye de TEK satır olmalı (defterle aynı transaction'da yazıldığı için ikisi ayrışamaz).
        Assert.Single(await cash.ListAccountTransfersAsync());
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "Alfa");
        var b = await CustomerAsync(sp, "Beta");
        var c = await CustomerAsync(sp, "Gama");

        var t = TestZaman.Now();
        await cash.TransferBetweenAccountsAsync(a, b, 100m, date: t.AddDays(-30), receiptNo: "MK-A", branch: "Merkez");
        await cash.TransferBetweenAccountsAsync(b, c, 200m, date: t.AddDays(-10), receiptNo: "MK-B", branch: "Şube2");
        await cash.TransferBetweenAccountsAsync(c, a, 300m, date: t.AddDays(-1), receiptNo: "MK-C", description: "son virman");

        // ELLE: 3 virman.
        Assert.Equal(3, (await cash.ListAccountTransfersAsync()).Count);
        Assert.Equal(3, (await cash.ListAccountTransfersAsync(new CariVirmanFilter())).Count);

        // Cari filtresi KAYNAK ya da HEDEF olmayı kapsar: a → 1. ve 3. virman.
        Assert.Equal(2, (await cash.ListAccountTransfersAsync(new CariVirmanFilter { CariId = a })).Count);
        Assert.Equal(2, (await cash.ListAccountTransfersAsync(new CariVirmanFilter { CariId = b })).Count);

        // Metin: makbuz / açıklama / şube
        Assert.Equal("MK-B", Assert.Single(await cash.ListAccountTransfersAsync(
            new CariVirmanFilter { Ara = "mk-b" })).MakbuzNo);
        Assert.Equal("Şube2", Assert.Single(await cash.ListAccountTransfersAsync(
            new CariVirmanFilter { Ara = "şube2" })).Sube);
        Assert.Equal("son virman", Assert.Single(await cash.ListAccountTransfersAsync(
            new CariVirmanFilter { Ara = "son vir" })).Aciklama);

        // Tarih: son 15 gün → 2 kayıt
        Assert.Equal(2, (await cash.ListAccountTransfersAsync(new CariVirmanFilter { Bas = t.AddDays(-15) })).Count);
        // Kapalı aralık → yalnız ortadaki
        Assert.Equal("MK-B", Assert.Single(await cash.ListAccountTransfersAsync(
            new CariVirmanFilter { Bas = t.AddDays(-15), Bit = t.AddDays(-5) })).MakbuzNo);

        // Sıralama: en yeni önce.
        var all = await cash.ListAccountTransfersAsync();
        Assert.Equal("MK-C", all[0].MakbuzNo);
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
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        await cash.TransferBetweenAccountsAsync(a, b, 500m);
        // Künyeyi sil → "eski kayıt" durumunu taklit et.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.CariVirmanBilgileri.RemoveRange(await db.CariVirmanBilgileri.ToListAsync());
            await db.SaveChangesAsync();
        }

        Assert.Empty(await cash.ListAccountTransfersAsync());
        // Defter DOKUNULMAMIŞ: bakiye ve ekstre yerinde.
        Assert.Equal(500m, await cash.GetAccountBalanceAsync(b));
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
            await sp.GetRequiredService<CashService>().TransferBetweenAccountsAsync(
                await CustomerAsync(sp, "Gizli A"), await CustomerAsync(sp, "Gizli B"), 100m, receiptNo: "GIZLI");
        }

        using (var s2 = host.ScopeFor(Guid.NewGuid()))
        {
            var cash = s2.ServiceProvider.GetRequiredService<CashService>();
            Assert.Empty(await cash.ListAccountTransfersAsync());
            Assert.Empty(await cash.ListAccountTransfersAsync(new CariVirmanFilter { Ara = "GIZLI" }));
        }

        // Operatör: ViewReports yok → göremez.
        using var op = host.ScopeFor(t1, Guid.NewGuid(), "op", UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(
            () => op.ServiceProvider.GetRequiredService<CashService>().ListAccountTransfersAsync());
    }
}
