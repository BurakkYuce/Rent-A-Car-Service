using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-57 — kasa/banka hareket listesi derinliği: belge künyesi (cari/evrak/şube/kanal) + döviz,
/// işlem türü, şube süzgeçleri + devir (açılış bakiyesi).
///
/// <para><b>Bağımsız oracle:</b> her senaryoda beklenen satır sayısı ve bakiye ELLE kurulan
/// işlemlerden sayılır (ör. "300 tahsilat − 100 gider → 200"), servis kodundan türetilmez.</para>
///
/// <para>Kilitlenen sözleşmeler: (1) cari GENERİK çözülür — belge türü listesi bakımı gerektirmez,
/// (2) her süzgeç daraltır ve BOŞKEN daraltmaz, (3) devir aynı süzgeçlerle hesaplanır (yoksa
/// yürüyen bakiye ilk satırdan itibaren yanlış olur), (4) süzgeçler para toplamlarını değiştirmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class KasaBankaHareketTests(PostgresFixture fx)
{
    /// <summary>PG timestamptz mikrosaniye, .NET tick 100ns — CI'da eşitlik düşmesin diye
    /// tarih tabanı TAM SANİYEYE hizalanır.</summary>
    private static DateTimeOffset Day(int difference)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(difference), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> CustomerAsync(IServiceScope s, string name)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });

    private static Task<Guid> AccountAsync(IServiceScope s, string code, string name, string type, string? iban = null)
        => s.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = code, Ad = name, Tur = type, Iban = iban });

    // ---------------------------------------------------------------- Belge künyesi

    [Fact]
    public async Task Cari_belge_no_ve_kanal_hareket_satirina_cozuluyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Ahmet");
        var cashAccount = await AccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount, Kanal = "Mobil" });

        var row = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa));
        // Cari, belge türüne özel okuma YAPILMADAN, dengeli kümenin Cari bacağından çözülür.
        Assert.Equal("Ahmet Test", row.CariAd);
        DocumentNoOracle.OneOfExpected(5, 1, row.BelgeNo);   // 05 = Tahsilat
        Assert.Equal("Mobil", row.Kanal);
        Assert.Equal(300m, row.Borc);
    }

    [Fact]
    public async Task Gider_evrak_no_ve_sube_cozuluyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cash = await AccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await expenses.CreateAsync(new ExpenseInput
        {
            NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit,
            FinansalHesapId = cash, EvrakNo = "EVR-7", Sube = "Kadıköy"
        });

        var row = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa));
        Assert.Equal("EVR-7", row.BelgeNo);
        Assert.Equal("Kadıköy", row.Sube);
        Assert.Equal(100m, row.Alacak);
    }

    // ---------------------------------------------------------------- Süzgeçler

    [Fact]
    public async Task Doviz_islem_turu_ve_sube_suzgecleri_daraltir_boşken_daraltmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Suzgec");
        var cashAccount = await AccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // ELLE: 1 tahsilat (TRY) + 1 gider (TRY, Kadıköy şubesi) + 1 ödeme (TRY).
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });
        await expenses.CreateAsync(new ExpenseInput
        {
            NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit,
            FinansalHesapId = cashAccount, Sube = "Kadıköy"
        });
        await cash.PayAsync(new CashInput
        { CariId = account, Tutar = 50m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });

        // Süzgeçsiz: 3 satır.
        Assert.Equal(3, (await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa)).Count);

        // İşlem türü: yalnız tahsilat.
        var collection = Assert.Single(await reports.GetAccountLedgerAsync(
            LedgerAccountType.Kasa, transactionType: "Tahsilat"));
        Assert.Equal(300m, collection.Borc);

        // Şube: yalnız künyesinde Kadıköy yazan gider. Künyesi OLMAYAN satırlar gizlenir —
        // "şubesi bilinmeyen" ile "o şubeye ait" karıştırılmaz.
        var withBranch = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, branch: "Kadıköy"));
        Assert.Equal(100m, withBranch.Alacak);

        // Döviz: hepsi TRY → 3; olmayan dövizde 0.
        Assert.Equal(3, (await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, currency: "TRY")).Count);
        Assert.Empty(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, currency: "USD"));

        // Boş süzgeç daraltmaz.
        Assert.Equal(3, (await reports.GetAccountLedgerAsync(
            LedgerAccountType.Kasa, currency: "", transactionType: "", branch: "")).Count);
    }

    [Fact]
    public async Task Suzgec_para_toplamlarini_DEGISTIRMEZ()
    {
        // Kırılgan regresyon: süzgeç yalnız GÖSTERİMİ daraltır; hesap-bazlı özet ve
        // kasa/banka toplamı süzgeçten etkilenmez (rapor iki farklı sayı göstermemeli).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Toplam");
        var cashAccount = await AccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });
        await cash.PayAsync(new CashInput
        { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });

        var summaryBefore = await reports.GetCashBankSummaryAsync();
        // Elle: 300 − 100 = 200.
        Assert.Equal(200m, summaryBefore.KasaBakiye);

        // Süzgeçli liste yalnız 1 satır gösterse de özet AYNI kalır.
        Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, transactionType: "Odeme"));
        var summaryAfter = await reports.GetCashBankSummaryAsync();
        Assert.Equal(summaryBefore.KasaBakiye, summaryAfter.KasaBakiye);
        Assert.Equal(200m, summaryAfter.KasaBakiye);
    }

    // ---------------------------------------------------------------- Devir

    [Fact]
    public async Task Devir_acilis_bakiyesini_gosterir_ve_yuruyen_bakiye_ondan_baslar()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Devir");
        var cashAccount = await AccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // ELLE: 10 gün önce 500 tahsilat (dönem ÖNCESİ), bugün 200 tahsilat (dönem İÇİ).
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 500m, Tarih = Day(-10), Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 200m, Tarih = Day(0), Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });

        var start = Day(-1);

        // Devir KAPALI: yalnız dönem içi satır, bakiye 200'den başlar.
        var withoutCarryForward = await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, from: start);
        Assert.Single(withoutCarryForward);
        Assert.Equal(200m, withoutCarryForward[0].YuruyenBakiye);

        // Devir AÇIK: başta devir satırı (500) + hareket; kapanış 700.
        var withCarryForward = await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, from: start, carryForward: true);
        Assert.Equal(2, withCarryForward.Count);
        Assert.True(withCarryForward[0].DevirMi);
        Assert.Equal(500m, withCarryForward[0].YuruyenBakiye);
        Assert.Equal(700m, withCarryForward[^1].YuruyenBakiye);
    }

    [Fact]
    public async Task Devir_AYNI_suzgeclerle_hesaplanir()
    {
        // Devir liste ile FARKLI küme toplasaydı yürüyen bakiye ilk satırdan itibaren yanlış olurdu.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "DevirSuzgec");
        var a = await AccountAsync(scope, "A", "Kasa A", "Kasa");
        var b = await AccountAsync(scope, "B", "Kasa B", "Kasa");

        // ELLE (dönem ÖNCESİ): A'ya 500, B'ye 900. Dönem İÇİ: A'ya 100.
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 500m, Tarih = Day(-10), Hesap = LedgerAccountType.Kasa, HesapId = a });
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 900m, Tarih = Day(-10), Hesap = LedgerAccountType.Kasa, HesapId = b });
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 100m, Tarih = Day(0), Hesap = LedgerAccountType.Kasa, HesapId = a });

        // A hesabıyla süzülünce devir YALNIZ A'nın 500'ü olmalı (1400 değil), kapanış 600.
        var rows = await reports.GetAccountLedgerAsync(
            LedgerAccountType.Kasa, from: Day(-1), accountId: a, carryForward: true);
        Assert.Equal(500m, rows[0].YuruyenBakiye);
        Assert.Equal(600m, rows[^1].YuruyenBakiye);
    }

    [Fact]
    public async Task Devir_alt_sinir_yoksa_eklenmez()
    {
        // "Başlangıçtan önce ne vardı" sorusu tarih alt sınırı olmadan anlamsızdır.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Sinirsiz");
        var cashAccount = await AccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 250m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });

        var rows = await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, carryForward: true);
        Assert.DoesNotContain(rows, x => x.DevirMi);
        Assert.Equal(250m, Assert.Single(rows).YuruyenBakiye);
    }

    // ---------------------------------------------------------------- İzolasyon

    [Fact]
    public async Task Hareket_listesi_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var cash = s1.ServiceProvider.GetRequiredService<CashService>();
            var account = await CustomerAsync(s1, "T1 Cari");
            var cashAccount = await AccountAsync(s1, "MRK", "Merkez Kasa", "Kasa");
            await cash.CollectAsync(new CashInput
            { CariId = account, Tutar = 400m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });
        }

        // racar_app ile bağlanan T2 bağlamı T1'in hareketini ve cari adını GÖRMEZ.
        using var s2 = host.ScopeFor(t2);
        var reports2 = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty(await reports2.GetAccountLedgerAsync(LedgerAccountType.Kasa));
        // Devir açıkken satır ÜRETİLİR ama açılış bakiyesi SIFIRDIR — başka tenant'ın 400'ü
        // devire de sızmaz (devir kendi tenant'ının geçmişini toplar).
        var withCarryForward = await reports2.GetAccountLedgerAsync(LedgerAccountType.Kasa, carryForward: true, from: Day(-1));
        Assert.True(Assert.Single(withCarryForward).DevirMi);
        Assert.Equal(0m, withCarryForward[0].YuruyenBakiye);
    }
}
