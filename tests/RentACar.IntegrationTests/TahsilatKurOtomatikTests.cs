using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ1-1.1b — En yüksek trafikli FX yazma yolları (tahsilat/ödeme/virmanlar + gider) KurCozucu'ya
/// bağlandı (1.1 adversarial kapsam-notu). SÖZLEŞME 1.1 ile aynı: açık kur aynen; boş → TRY=1 /
/// döviz sabit-kur/TCMB; bulunamazsa NET RED sıfır yan etki. BAĞIMSIZ ORACLE (elle): 100 EUR × 40 = 4000.
/// Toplu yollarda satır-bazlı çözüm + (kod,gün) önbelleği; atomiklik korunur (SEK'li satır → HİÇBİRİ yazılmaz).
/// </summary>
[Collection("postgres")]
public sealed class TahsilatKurOtomatikTests(PostgresFixture fx)
{
    private static Task SabitKurAsync(IServiceProvider sp, string kod, decimal kur)
        => sp.GetRequiredService<SabitKurService>().UpsertAsync(new SabitKurInput { Kod = kod, Kur = kur, Aktif = true });

    private static Task<Guid> CariAsync(IServiceProvider sp, string ad = "Fx") =>
        sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Cari" });

    [Fact]
    public async Task Tahsilat_eur_otomatik_ve_acik_kur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var cari = await CariAsync(sp);
        var cash = sp.GetRequiredService<CashService>();

        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Doviz = "EUR" }); // kur boş
        Assert.Equal(-4000m, await cash.GetCariBalanceAsync(cari));   // 100×40 tahsilat → cari −4000 (elle)

        await cash.PayAsync(new CashInput { CariId = cari, Tutar = 10m, Doviz = "EUR", Kur = 35m }); // açık kur
        Assert.Equal(-3650m, await cash.GetCariBalanceAsync(cari));   // −4000 + 350
    }

    [Fact]
    public async Task Tahsilat_kursuz_doviz_net_red_sifir_yan_etki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await CariAsync(sp);
        var cash = sp.GetRequiredService<CashService>();

        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Doviz = "SEK" }));
        Assert.Equal(0m, await cash.GetCariBalanceAsync(cari));
        Assert.Empty(await cash.ListAsync()); // CashTransaction bile yazılmadı
    }

    [Fact]
    public async Task Toplu_tahsilat_karisik_satirlar_ve_atomik_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var c1 = await CariAsync(sp, "T1");
        var c2 = await CariAsync(sp, "T2");
        var cash = sp.GetRequiredService<CashService>();

        // Karışık: TRY (boş kur→1) + EUR (boş→40) + EUR açık 35 — elle: 100 + 4000 + 3500.
        await cash.BatchCollectAsync(
        [
            new CashInput { CariId = c1, Tutar = 100m },                                // TRY
            new CashInput { CariId = c1, Tutar = 100m, Doviz = "EUR" },                 // oto 40
            new CashInput { CariId = c2, Tutar = 100m, Doviz = "EUR", Kur = 35m }       // açık
        ]);
        Assert.Equal(-4100m, await cash.GetCariBalanceAsync(c1));
        Assert.Equal(-3500m, await cash.GetCariBalanceAsync(c2));

        // Atomiklik: 2. satır SEK (çözülemez) → HİÇBİR satır yazılmaz.
        await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync(
        [
            new CashInput { CariId = c1, Tutar = 50m },
            new CashInput { CariId = c2, Tutar = 50m, Doviz = "SEK" }
        ]));
        Assert.Equal(-4100m, await cash.GetCariBalanceAsync(c1)); // değişmedi
        Assert.Equal(-3500m, await cash.GetCariBalanceAsync(c2));
    }

    [Fact]
    public async Task Virmanlar_eur_otomatik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var cash = sp.GetRequiredService<CashService>();
        var rs = sp.GetRequiredService<ReportService>();

        // Kasa→Banka 10 EUR (oto 40): kasa −400 / banka +400 (elle).
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 10m, doviz: "EUR");
        var ozet = await rs.GetKasaBankaSummaryAsync();
        Assert.Equal(-400m, ozet.KasaBakiye);
        Assert.Equal(400m, ozet.BankaBakiye);

        // Cari↔cari 5 EUR (oto 40): kaynak −200 / hedef +200.
        var k = await CariAsync(sp, "K");
        var h = await CariAsync(sp, "H");
        await cash.TransferBetweenCariAsync(k, h, 5m, doviz: "EUR");
        Assert.Equal(-200m, await cash.GetCariBalanceAsync(k));
        Assert.Equal(200m, await cash.GetCariBalanceAsync(h));

        // Kur'suz döviz virmanı → red (bakiyeler değişmez).
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.TransferBetweenCariAsync(k, h, 1m, doviz: "SEK"));
        Assert.Equal(-200m, await cash.GetCariBalanceAsync(k));
    }

    [Fact]
    public async Task Api_cash_request_kur_bos_otomatik_cozulur() // adversarial Medium: JSON API sessiz kur=1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var cari = await CariAsync(sp, "Api");
        var cash = sp.GetRequiredService<CashService>();

        // JSON gövdesinde Kur alanı hiç gönderilmedi → null → otomatik 40 (eski DTO default'u 1m'di).
        var req = new RentACar.Api.Dtos.CashRequest { CariId = cari, Tutar = 100m, Doviz = "EUR" };
        await cash.CollectAsync(req.ToInput());
        Assert.Equal(-4000m, await cash.GetCariBalanceAsync(cari)); // 100×40 (elle) — 100 DEĞİL
    }

    [Fact]
    public async Task Gider_eur_otomatik_ve_toplu_atomik()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var exp = sp.GetRequiredService<ExpenseService>();
        var rs = sp.GetRequiredService<ReportService>();

        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, Doviz = "EUR", OdemeYontemi = OdemeYontemi.Nakit });
        Assert.Equal(4000m, (await rs.GetGelirGiderAsync()).GiderToplam); // 100×40 elle

        // Toplu: TRY 50 + EUR 10 (oto 40 → 400) = +450 → 4450.
        await exp.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 50m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.Nakit },
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 10m, KdvOrani = 0m, Doviz = "EUR", OdemeYontemi = OdemeYontemi.Nakit }
        ]);
        Assert.Equal(4450m, (await rs.GetGelirGiderAsync()).GiderToplam);

        // Toplu içinde SEK → atomik red, toplam değişmez.
        await Assert.ThrowsAsync<ValidationException>(() => exp.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 5m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.Nakit },
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 5m, KdvOrani = 0m, Doviz = "SEK", OdemeYontemi = OdemeYontemi.Nakit }
        ]));
        Assert.Equal(4450m, (await rs.GetGelirGiderAsync()).GiderToplam);
    }
}
