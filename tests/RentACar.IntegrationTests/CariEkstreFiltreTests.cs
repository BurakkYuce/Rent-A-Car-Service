using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-65 — Cari ekstre filtre/görünüm paneli (canlı <c>hesap_extresi.aspx</c>).
///
/// <para><b>Fazın asıl riski DEVİR'dir.</b> Ekstre yürüyen bakiye gösterir; tarih filtresi
/// uygulandığında yürüyen bakiye sıfırdan başlasaydı ekranda YANLIŞ BİR BAKİYE görünürdü. Devir,
/// kapsam dışında kalan önceki hareketlerin netidir ve şu değişmezi sağlamalıdır:
/// <c>devir + Σ(görünen SignedBase) == filtresiz toplam bakiye</c>. Aşağıdaki testler bunu
/// senaryodan bağımsız olarak elle kurulmuş sabit değerlerle doğrular.</para>
///
/// <para>Bağımsız oracle: 4 hareket elle kurulur (2 döviz, 3 farklı tarih) ve beklenen tutarlar
/// testte elle hesaplanır — servisin dönüşünden türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class CariEkstreFiltreTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Now().AddDays(-60);

    private static async Task<Guid> CustomerAsync(IServiceScope s, string name = "Ekstre", string soyad = "Testi")
        => await s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = soyad });

    /// <summary>
    /// Defter satırını DOĞRUDAN yazar. Neden servis değil: tahsilat/gider servisleri tarihi "şimdi"ye
    /// düşürür ve tutar/döviz kombinasyonunu serbestçe kurmaya izin vermez; burada gereken şey
    /// TARİH ve DÖVİZ üzerinde tam kontrol. Denge (Σ borç == Σ alacak) tenant içinde korunur:
    /// her cari satırının karşı bacağı Kasa'ya yazılır.
    /// </summary>
    private static async Task MovementAsync(
        IServiceScope s, Guid customerId, DateTimeOffset date, LedgerDirection yon,
        decimal amount, string currency, decimal exchangeRate, string sourceType, Guid? sourceId = null)
    {
        var factory = s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var money = new Money(amount, currency, exchangeRate);
        var src = sourceId ?? Guid.NewGuid();
        db.AccountLedgerEntries.AddRange(
            new AccountLedgerEntry
            {
                EntryDateUtc = date, AccountType = LedgerAccountType.Cari, AccountRef = customerId,
                Direction = yon, Amount = money, SourceType = sourceType, SourceId = src,
                Description = $"{sourceType} {amount} {currency}"
            },
            new AccountLedgerEntry
            {
                EntryDateUtc = date, AccountType = LedgerAccountType.Kasa, AccountRef = null,
                Direction = yon == LedgerDirection.Debit ? LedgerDirection.Credit : LedgerDirection.Debit,
                Amount = money, SourceType = sourceType, SourceId = src, Description = "karşı bacak"
            });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 4 hareket:
    ///   T0+0   Borç   1.000 TRY (kur 1)   → base +1000  [Fatura]
    ///   T0+10  Alacak   400 TRY (kur 1)   → base  −400  [Tahsilat]
    ///   T0+20  Borç     100 USD (kur 30)  → base +3000  [Fatura]
    ///   T0+30  Alacak    50 USD (kur 30)  → base −1500  [Tahsilat]
    /// Net bakiye = 1000 − 400 + 3000 − 1500 = 2.100
    /// </summary>
    private static async Task ScenarioAsync(IServiceScope s, Guid account)
    {
        await MovementAsync(s, account, T0, LedgerDirection.Debit, 1000m, "TRY", 1m, "Fatura");
        await MovementAsync(s, account, T0.AddDays(10), LedgerDirection.Credit, 400m, "TRY", 1m, "Tahsilat");
        await MovementAsync(s, account, T0.AddDays(20), LedgerDirection.Debit, 100m, "USD", 30m, "Fatura");
        await MovementAsync(s, account, T0.AddDays(30), LedgerDirection.Credit, 50m, "USD", 30m, "Tahsilat");
    }

    [Fact]
    public async Task Filtresiz_ekstre_ESKI_davranisi_korur_devir_sifir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(s);
        await ScenarioAsync(s, account);
        var cash = s.ServiceProvider.GetRequiredService<CashService>();

        var result = await cash.GetStatementAsync(account);
        Assert.Equal(0m, result.Devir);
        Assert.Equal(4, result.Satirlar.Count);
        Assert.Equal(2100m, result.Satirlar.Sum(e => e.SignedBase));      // elle: 1000−400+3000−1500
        Assert.Equal(2100m, await cash.GetAccountBalanceAsync(account));        // bakiye ile birebir
        // Tarihe göre ARTAN sıralama (eski davranış).
        Assert.True(result.Satirlar.Zip(result.Satirlar.Skip(1)).All(p => p.First.EntryDateUtc <= p.Second.EntryDateUtc));
        // Boş filtre nesnesi de daraltmamalı.
        Assert.Equal(4, (await cash.GetStatementAsync(account, new CariEkstreFilter())).Satirlar.Count);
    }

    [Fact]
    public async Task Tarih_filtresinde_DEVIR_dogru_ve_toplam_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(s);
        await ScenarioAsync(s, account);
        var cash = s.ServiceProvider.GetRequiredService<CashService>();

        // T0+15'ten itibaren: görünen 2 satır (+3000, −1500); devir = 1000−400 = 600.
        var result = await cash.GetStatementAsync(account, new CariEkstreFilter { Bas = T0.AddDays(15) });
        Assert.Equal(600m, result.Devir);
        Assert.Equal(2, result.Satirlar.Count);
        Assert.Equal(1500m, result.Satirlar.Sum(e => e.SignedBase));

        // DEĞİŞMEZ: devir + görünenler == gerçek bakiye. Devir olmasaydı ekran 1.500 gösterirdi.
        Assert.Equal(2100m, result.Devir + result.Satirlar.Sum(e => e.SignedBase));

        // Üst sınır: T0+15'e kadar → ilk 2 satır, devir yok.
        var firstHalf = await cash.GetStatementAsync(account, new CariEkstreFilter { Bit = T0.AddDays(15) });
        Assert.Equal(0m, firstHalf.Devir);
        Assert.Equal(2, firstHalf.Satirlar.Count);
        Assert.Equal(600m, firstHalf.Satirlar.Sum(e => e.SignedBase));

        // Kapalı aralık [T0+5, T0+25]: görünen 2 satır (−400, +3000); devir = 1000.
        var middle = await cash.GetStatementAsync(account,
            new CariEkstreFilter { Bas = T0.AddDays(5), Bit = T0.AddDays(25) });
        Assert.Equal(1000m, middle.Devir);
        Assert.Equal(2, middle.Satirlar.Count);
        Assert.Equal(2600m, middle.Satirlar.Sum(e => e.SignedBase));
    }

    [Fact]
    public async Task Doviz_filtresi_ve_devir_AYNI_suzgecten_gecer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(s);
        await ScenarioAsync(s, account);
        var cash = s.ServiceProvider.GetRequiredService<CashService>();

        // USD hareketleri: +3000 ve −1500 (base).
        var usd = await cash.GetStatementAsync(account, new CariEkstreFilter { Doviz = "usd" });  // küçük harf de çalışmalı
        Assert.Equal(2, usd.Satirlar.Count);
        Assert.All(usd.Satirlar, e => Assert.Equal("USD", e.Amount.Currency));
        Assert.Equal(1500m, usd.Satirlar.Sum(e => e.SignedBase));
        Assert.Equal(0m, usd.Devir);   // tarih sınırı yok → devir yok

        // KRİTİK: devir tarih-DIŞI filtrelerden de geçmeli. USD + T0+25 sonrası → görünen yalnız
        // −1500; devir yalnız USD borç (+3000) olmalı, TRY hareketleri KARIŞMAMALI.
        var usdAfter = await cash.GetStatementAsync(account,
            new CariEkstreFilter { Doviz = "USD", Bas = T0.AddDays(25) });
        Assert.Equal(3000m, usdAfter.Devir);
        Assert.Equal(-1500m, Assert.Single(usdAfter.Satirlar).SignedBase);
        Assert.Equal(1500m, usdAfter.Devir + usdAfter.Satirlar.Sum(e => e.SignedBase));  // USD neti

        // TRY tarafı
        var tryv = await cash.GetStatementAsync(account, new CariEkstreFilter { Doviz = "TRY" });
        Assert.Equal(2, tryv.Satirlar.Count);
        Assert.Equal(600m, tryv.Satirlar.Sum(e => e.SignedBase));
    }

    [Fact]
    public async Task Kaynak_turu_filtresi_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var account = await CustomerAsync(s);
        await ScenarioAsync(s, account);
        var cash = s.ServiceProvider.GetRequiredService<CashService>();

        var invoice = await cash.GetStatementAsync(account, new CariEkstreFilter { SourceType = "Fatura" });
        Assert.Equal(2, invoice.Satirlar.Count);
        Assert.Equal(4000m, invoice.Satirlar.Sum(e => e.SignedBase));      // 1000 + 3000

        var collection = await cash.GetStatementAsync(account, new CariEkstreFilter { SourceType = "Tahsilat" });
        Assert.Equal(2, collection.Satirlar.Count);
        Assert.Equal(-1900m, collection.Satirlar.Sum(e => e.SignedBase));   // −400 − 1500

        // Birleşik: Fatura + USD → yalnız 1 satır (+3000)
        var invoiceUsd = await cash.GetStatementAsync(account,
            new CariEkstreFilter { SourceType = "Fatura", Doviz = "USD" });
        Assert.Equal(3000m, Assert.Single(invoiceUsd.Satirlar).SignedBase);

        Assert.Empty((await cash.GetStatementAsync(account, new CariEkstreFilter { SourceType = "YokBoyleBirSey" })).Satirlar);
    }

    [Fact]
    public async Task Sozlesme_durumu_filtresi_yalnız_KIRAYA_BAGLI_tahsilati_gosterir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var account = await CustomerAsync(s);
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // İki kira: biri Kirada, biri Tamamlandı. Her birine bağlı BİRER tahsilat kaydı;
        // ayrıca kiraya BAĞLI OLMAYAN bir fatura satırı.
        Guid txOnRent, txOk;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var onRent = new RentalContract { SozlesmeNo = "KR-1", MusteriId = account, Durum = RentalStatus.Kirada };
            var ok = new RentalContract { SozlesmeNo = "KR-2", MusteriId = account, Durum = RentalStatus.Tamamlandi };
            db.Rentals.AddRange(onRent, ok);
            var t1 = new CashTransaction
            { No = "T-1", CariId = account, RentalId = onRent.Id, Amount = new Money(200m, "TRY", 1m) };
            var t2 = new CashTransaction
            { No = "T-2", CariId = account, RentalId = ok.Id, Amount = new Money(300m, "TRY", 1m) };
            db.CashTransactions.AddRange(t1, t2);
            await db.SaveChangesAsync();
            txOnRent = t1.Id; txOk = t2.Id;
        }

        await MovementAsync(s, account, T0, LedgerDirection.Credit, 200m, "TRY", 1m, "Tahsilat", txOnRent);
        await MovementAsync(s, account, T0.AddDays(1), LedgerDirection.Credit, 300m, "TRY", 1m, "Tahsilat", txOk);
        await MovementAsync(s, account, T0.AddDays(2), LedgerDirection.Debit, 900m, "TRY", 1m, "Fatura");

        var cash = sp.GetRequiredService<CashService>();
        Assert.Equal(3, (await cash.GetStatementAsync(account)).Satirlar.Count);

        // Kirada → yalnız 200'lük tahsilat.
        var onRentRows = (await cash.GetStatementAsync(account,
            new CariEkstreFilter { KiraDurum = RentalStatus.Kirada })).Satirlar;
        Assert.Equal(-200m, Assert.Single(onRentRows).SignedBase);

        // Tamamlandı → yalnız 300'lük tahsilat.
        var okRows = (await cash.GetStatementAsync(account,
            new CariEkstreFilter { KiraDurum = RentalStatus.Tamamlandi })).Satirlar;
        Assert.Equal(-300m, Assert.Single(okRows).SignedBase);

        // Kira bağı OLMAYAN fatura satırı hiçbir durum filtresinde görünmemeli (kasıtlı davranış).
        Assert.DoesNotContain(onRentRows, e => e.SourceType == "Fatura");
        Assert.DoesNotContain(okRows, e => e.SourceType == "Fatura");
        Assert.Empty((await cash.GetStatementAsync(account,
            new CariEkstreFilter { KiraDurum = RentalStatus.Iptal })).Satirlar);
    }

    [Fact]
    public async Task Ekstre_tenant_izolasyonlu_filtreyle_de()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid account;
        using (var s1 = host.ScopeFor(t1))
        {
            account = await CustomerAsync(s1);
            await ScenarioAsync(s1, account);
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var cash = s2.ServiceProvider.GetRequiredService<CashService>();
        // Başka tenant AYNI cari id'sini bilse bile hiçbir satır göremez.
        Assert.Empty((await cash.GetStatementAsync(account)).Satirlar);
        var filtered = await cash.GetStatementAsync(account, new CariEkstreFilter { Bas = T0.AddDays(15) });
        Assert.Empty(filtered.Satirlar);
        Assert.Equal(0m, filtered.Devir);   // devir de sızmamalı
    }
}
