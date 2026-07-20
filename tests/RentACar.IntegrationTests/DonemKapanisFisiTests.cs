using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-A dönem-sonu kapanış fişi (close-lite). BAĞIMSIZ ORACLE: gelir 1000 − gider 300 = 700 (elle kurulmuş,
/// koddan DEĞİL). Kapanış fişi Gelir/Gider'i sıfırlar, net'i DonemSonucu'na taşır; P&amp;L raporları (GelirGider,
/// Karlılık) kapanıştan ETKİLENMEZ (fiş iç virman → hariç tutulur); çift-taraflı defter dengesi korunur; idempotent.
/// </summary>
[Collection("postgres")]
public sealed class DonemKapanisFisiTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset IsTarih = new(2026, 6, 10, 9, 0, 0, TimeSpan.Zero); // işlem tarihi
    private static readonly DateTimeOffset Kapanis = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero); // kapanış tarihi

    /// <summary>Gelir 1000 (KDV 0 → Gelir defter 1000) + Gider 300 (KDV 0 → Gider 300) tohumu.</summary>
    private static async Task SeedAsync(IServiceProvider sp, decimal gelirNet, decimal giderNet)
    {
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Kapanış Test" });
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = cari, NetTutar = gelirNet, KdvOrani = 0m, Tarih = IsTarih });
        await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = giderNet, KdvOrani = 0m, Doviz = "TRY", Kur = 1m,
            OdemeYontemi = OdemeYontemi.Nakit, Tarih = IsTarih
        });
    }

    [Fact]
    public async Task Kapanis_gelir_gider_sifirlar_net_donem_sonucuna_tasir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 1000m, 300m);
        var rapor = sp.GetRequiredService<ReportService>();

        // Kapanış ÖNCESİ P&L (elle: 1000 − 300 = 700).
        var ggOnce = await rapor.GetGelirGiderAsync();
        Assert.Equal(1000m, ggOnce.GelirToplam);
        Assert.Equal(300m, ggOnce.GiderToplam);
        Assert.Equal(700m, ggOnce.NetKar);

        await sp.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        // Mizan: Gelir/Gider sıfırlanmış, DonemSonucu = −700 (Credit bakiye = kâr → özkaynak).
        var mizan = await rapor.GetMizanAsync();
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gider));
        Assert.Equal(-700m, Bakiye(mizan, LedgerAccountType.DonemSonucu));
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye)); // çift-taraflı defter dengesi

        // P&L raporları kapanıştan ETKİLENMEZ (fiş SourceType='DonemKapanis' → hariç).
        var ggSonra = await rapor.GetGelirGiderAsync();
        Assert.Equal(1000m, ggSonra.GelirToplam);
        Assert.Equal(300m, ggSonra.GiderToplam);
        Assert.Equal(700m, ggSonra.NetKar);
        Assert.Equal(700m, (await rapor.GetKarlilikAsync()).ToplamNetKar);

        // Dönem kilitli.
        var kilit = await sp.GetRequiredService<DonemKilidiService>().GetClosingDateAsync();
        Assert.Equal(Kapanis.Date, kilit!.Value.Date);
    }

    [Fact]
    public async Task Kapanis_idempotent_yeniden_kapatma_cift_saymaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 1000m, 300m);
        var kapanis = sp.GetRequiredService<DonemKapanisFisiService>();
        var donem = sp.GetRequiredService<DonemKilidiService>();
        var rapor = sp.GetRequiredService<ReportService>();

        await kapanis.KapatAsync(Kapanis);
        await donem.UnlockAsync();
        await kapanis.KapatAsync(Kapanis); // AYNI tarih tekrar → fiş NO-OP (deterministik SourceId + unique index)

        var mizan = await rapor.GetMizanAsync();
        Assert.Equal(-700m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // −1400 DEĞİL (çift saymadı)
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));
    }

    [Fact]
    public async Task Zarar_donemi_donem_sonucu_borc_bakiye()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 300m, 1000m); // gelir 300 < gider 1000 → zarar 700

        await sp.GetRequiredService<DonemKapanisFisiService>().KapatAsync(Kapanis);

        var mizan = await sp.GetRequiredService<ReportService>().GetMizanAsync();
        Assert.Equal(700m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // zarar → Borç bakiye +700
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));
    }

    [Fact]
    public async Task Cok_donem_kapanis_delta_yakalar_cift_saymaz()
    {
        // ADVERSARIAL regresyon: art arda iki dönem kapatılınca ikinci kapanış YALNIZ yeni delta'yı yakalamalı
        // (ilk kapanış o güne dek olanı zaten sıfırladı). Aksi halde ilk dönem gelirini çift sayardı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Çok Dönem" });
        var inv = sp.GetRequiredService<InvoiceService>();
        var exp = sp.GetRequiredService<ExpenseService>();
        var kapanis = sp.GetRequiredService<DonemKapanisFisiService>();
        var rapor = sp.GetRequiredService<ReportService>();

        var mayisTarih = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero);
        var mayisKapanis = new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero);
        var haziranTarih = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var haziranKapanis = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

        // Mayıs: gelir 1000 → kapat (kâr 1000).
        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 1000m, KdvOrani = 0m, Tarih = mayisTarih });
        await kapanis.KapatAsync(mayisKapanis);
        Assert.Equal(-1000m, Bakiye(await rapor.GetMizanAsync(), LedgerAccountType.DonemSonucu));

        // Haziran: gelir 500 − gider 200 = 300 → kapat. DonemSonucu = −(1000 + 300) = −1300.
        await inv.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 500m, KdvOrani = 0m, Tarih = haziranTarih });
        await exp.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 200m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m, OdemeYontemi = OdemeYontemi.Nakit, Tarih = haziranTarih });
        await kapanis.KapatAsync(haziranKapanis);

        var mizan = await rapor.GetMizanAsync();
        Assert.Equal(-1300m, Bakiye(mizan, LedgerAccountType.DonemSonucu)); // −2300 (çift-sayım) DEĞİL
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gelir));
        Assert.Equal(0m, Bakiye(mizan, LedgerAccountType.Gider));
        Assert.Equal(0m, mizan.Sum(m => m.Bakiye));
        // P&L toplam kapanışlardan etkilenmez: gelir 1500, gider 200, net 1300.
        Assert.Equal(1300m, (await rapor.GetGelirGiderAsync()).NetKar);
    }

    [Fact]
    public async Task Zaten_kapali_donem_yeniden_kapatilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SeedAsync(sp, 1000m, 300m);
        var kapanis = sp.GetRequiredService<DonemKapanisFisiService>();

        await kapanis.KapatAsync(Kapanis);
        // Kilit dururken aynı/önceki tarihi tekrar kapatmak reddedilir (yanlış çift-kapanış önlenir).
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() => kapanis.KapatAsync(Kapanis));
    }

    private static decimal Bakiye(IReadOnlyList<MizanSatirDto> mizan, LedgerAccountType t)
        => mizan.FirstOrDefault(m => m.Tip == t)?.Bakiye ?? 0m;
}
