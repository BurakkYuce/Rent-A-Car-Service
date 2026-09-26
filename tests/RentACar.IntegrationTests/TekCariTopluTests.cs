using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-29 — toplu gider derinliği (plaka/cari/vade/hesap) + tek-cari toplu kapatma.
///
/// <para><b>BAĞIMSIZ ORACLE:</b> tutarlar elle kurulan senaryodan yazılır. Her senaryoda
/// <b>Σ Borç(base) == Σ Alacak(base)</b> ayrıca doğrulanır — defter dengesi servis koduna değil
/// kayıtların kendisine sorulur.</para>
///
/// <para><b>ÇİFT KAPATMA ÇİTİ:</b> sistemde allocation (açık-kalem) modeli yok; aynı kalemleri iki
/// kez kapatmayı BAKİYE çiti engeller. Test bunu ampirik kilitler.</para>
/// </summary>
[Collection("postgres")]
public sealed class TekCariTopluTests(PostgresFixture fx)
{
    private static async Task<Guid> CustomerAsync(IServiceProvider sp, string name = "Toplu") =>
        await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Cari" });

    /// <summary>Cariye borç yazan en yalın yol: AÇIK HESAP gideri (Alacak Cari) tersi olduğundan
    /// borç için ÖDEME (tediye) kullanılır — Borç Cari / Alacak Kasa.</summary>
    private static async Task DebitAsync(IServiceProvider sp, Guid customerId, decimal amount, string description)
        => await sp.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = customerId, Tutar = amount, Hesap = LedgerAccountType.Kasa, Aciklama = description });

    private static async Task<(decimal Borc, decimal Alacak)> LedgerAsync(IServiceProvider sp)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        return (rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase),
                rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase));
    }

    // ------------------------------------------------------------------ toplu gider derinliği

    [Fact]
    public async Task Toplu_gider_PLAKA_bazli_satirlari_dogru_araclara_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicles = sp.GetRequiredService<VehicleService>();

        var a = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 TG 01" });
        var b = await vehicles.CreateAsync(new VehicleInput { Plaka = "34 TG 02" });

        // ELLE: 3 satır — 1000 (a), 500 (b), 250 (araçsız). KDV 0 → net = brüt.
        await sp.GetRequiredService<ExpenseService>().BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Arac, NetTutar = 1000m, KdvOrani = 0m, VehicleId = a, Aciklama = "Yakıt" },
            new ExpenseInput { Tip = ExpenseType.Arac, NetTutar = 500m, KdvOrani = 0m, VehicleId = b, Aciklama = "Bakım" },
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 250m, KdvOrani = 0m, Aciklama = "Kırtasiye" }
        ]);

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var expenseLines = await db.AccountLedgerEntries.AsNoTracking()
            .Where(x => x.AccountType == LedgerAccountType.Gider).ToListAsync();

        // ELLE: aracın AccountRef'i o aracın Id'si; araçsız satırda AccountRef null.
        Assert.Equal(1000m, expenseLines.Where(x => x.AccountRef == a).Sum(x => x.Amount.AmountInBase));
        Assert.Equal(500m, expenseLines.Where(x => x.AccountRef == b).Sum(x => x.Amount.AmountInBase));
        Assert.Equal(250m, expenseLines.Where(x => x.AccountRef == null).Sum(x => x.Amount.AmountInBase));

        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(1750m, debit);          // ELLE: 1000+500+250, KDV yok
        Assert.Equal(debit, credit);         // DENGE
    }

    /// <summary>
    /// ADVERSARIAL M4 — plaka çözümlemesinin sözleşmesi. Eskiden bu kural web ucunda KOPYA olarak
    /// duruyordu ve hiç test edilmiyordu; kopya olduğu sürece iki kural zamanla ayrışır ve kullanıcı
    /// listede gördüğü plakayı yazdığında "araç bulunamadı" alırdı. Kural artık
    /// <see cref="VehicleService.PlateKey"/> — tek kaynak.
    /// </summary>
    [Theory]
    [InlineData("34 ABC 34", "34ABC34")]
    [InlineData("34abc34", "34ABC34")]
    [InlineData("  34 abc 34  ", "34ABC34")]
    [InlineData("06 XYZ 06", "06XYZ06")]
    [InlineData(null, "")]
    public void Plaka_anahtari_bosluk_ve_harf_duyarsizdir(string? input, string expected)
        => Assert.Equal(expected, VehicleService.PlateKey(input));

    [Fact]
    public async Task Plaka_anahtari_KAYIT_ile_ARAMA_yolunda_ayni_araci_bulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Araç "34 TG 09" diye kaydedilir; kullanıcı toplu giderde "34tg09" yazar.
        var id = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TG 09" });

        var index = (await sp.GetRequiredService<VehicleService>().ListAsync())
            .ToDictionary(v => VehicleService.PlateKey(v.Plaka), v => v.Id);

        Assert.Equal(id, index[VehicleService.PlateKey("34tg09")]);
        Assert.Equal(id, index[VehicleService.PlateKey(" 34 TG 09 ")]);
        Assert.False(index.ContainsKey(VehicleService.PlateKey("34 TG 99")));  // yok → uç red verir
    }

    [Fact]
    public async Task Toplu_gider_cari_vade_hesap_bilgisini_belgeye_yazar_deftere_YAZMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = await CustomerAsync(sp, "Tedarikci");
        var accountEntry = await sp.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "ZR-TL", Ad = "Ziraat TL", Tur = "Banka" });
        var due = DateTimeOffset.UtcNow.AddDays(30);

        await sp.GetRequiredService<ExpenseService>().BatchCreateAsync(
        [
            new ExpenseInput
            {
                Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m,
                OdemeYontemi = PaymentMethod.AcikHesap, CariId = account,
                Vade = due, FinansalHesapId = accountEntry, Aciklama = "Vadeli alım"
            }
        ]);

        var expense = Assert.Single(await sp.GetRequiredService<ExpenseService>().ListAsync());
        Assert.Equal(account, expense.CariId);
        Assert.Equal(accountEntry, expense.FinansalHesapId);
        Assert.Equal(due.UtcDateTime.Date, expense.Vade!.Value.UtcDateTime.Date);

        // DEFTER: vade/hesap seçimi kayıt kümesini DEĞİŞTİRMEZ. Açık hesapta karşı hesap Cari'dir
        // (Kasa/Banka değil) ve tutar brüttür.
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        Assert.Equal(1200m, rows.Where(x => x.AccountType == LedgerAccountType.Cari)
            .Sum(x => x.Amount.AmountInBase));                      // ELLE: 1000 + %20 KDV
        Assert.Equal(1000m, rows.Where(x => x.AccountType == LedgerAccountType.Gider)
            .Sum(x => x.Amount.AmountInBase));                      // net
        Assert.Equal(200m, rows.Where(x => x.AccountType == LedgerAccountType.Kdv)
            .Sum(x => x.Amount.AmountInBase));
        Assert.DoesNotContain(rows, x => x.AccountRef == accountEntry); // hesap defterde REFERANS DEĞİL

        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task Toplu_gider_cok_dovizli_satir_BAZ_tutara_cevrilir_ve_dengeli_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // ELLE: 100 EUR × 40 = 4000 baz + 1000 TRY = 5000 baz gider. KDV 0.
        await sp.GetRequiredService<ExpenseService>().BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, Doviz = "EUR", Kur = 40m },
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0m, Doviz = "TRY", Kur = 1m }
        ]);

        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(5000m, debit);
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task Toplu_gider_ayni_anahtarla_iki_kez_gonderilirse_TEK_kez_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<ExpenseService>();
        var token = Guid.NewGuid();

        ExpenseInput[] Items() =>
        [
            new() { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m, Aciklama = "A" },
            new() { Tip = ExpenseType.Genel, NetTutar = 700m, KdvOrani = 0m, Aciklama = "B" }
        ];

        await svc.BatchCreateAsync(Items(), token);
        // ÇİFT SUBMIT: aynı token → ikinci parti tümüyle geri alınır (atomik).
        await Assert.ThrowsAnyAsync<Exception>(() => svc.BatchCreateAsync(Items(), token));

        Assert.Equal(2, (await svc.ListAsync()).Count);      // 4 değil 2
        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(1000m, debit);                            // ELLE: 300+700, çift yazım YOK
        Assert.Equal(debit, credit);
    }

    // ------------------------------------------------------------------ tek-cari toplu kapatma

    [Fact]
    public async Task Tek_cari_secilen_kalemleri_kapatir_bakiye_ELLE_hesaplanan_farka_iner()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: 4 borç kalemi — 100, 250, 400, 750 → toplam 1500 borç.
        foreach (var (amount, name) in new[] { (100m, "K1"), (250m, "K2"), (400m, "K3"), (750m, "K4") })
            await DebitAsync(sp, account, amount, name);
        Assert.Equal(1500m, await cash.GetAccountBalanceAsync(account));

        var debts = (await cash.GetStatementAsync(account)).Satirlar
            .Where(x => x.Direction == LedgerDirection.Debit && x.AccountType == LedgerAccountType.Cari)
            .OrderBy(x => x.Amount.Amount).ToList();
        Assert.Equal(4, debts.Count);

        // 3 kalem seç: 100 + 250 + 400 = 750 (elle).
        var selected = debts.Take(3).Select(x => x.Id).ToList();
        var collect = await cash.CloseSingleAccountBulkAsync(account, selected, LedgerAccountType.Kasa);
        Assert.Equal(750m, collect);

        // Bakiye 1500 − 750 = 750 (elle).
        Assert.Equal(750m, await cash.GetAccountBalanceAsync(account));

        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(debit, credit);                     // DENGE korunur
    }

    /// <summary>
    /// ADVERSARIAL H1 kalıcı kilidi. Bu senaryo bir kez GERÇEKTEN kırıktı: "bakiye çiti çift
    /// kapatmayı engeller" iddiası yalnız carinin TEK borcu varken tutuyordu. BAŞKA açık borç
    /// varken aynı kalem tekrar tekrar kapatılabiliyor, alınmamış tahsilat yazılıyordu.
    /// Çözüm kalem-bazlı tahsis kaydı (KapatmaTahsis) — asıl çit odur.
    /// </summary>
    [Fact]
    public async Task Tek_cari_ayni_kalem_BASKA_BORC_VARKEN_de_ikinci_kez_kapatilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: 100 + 900 = 1000 borç. Kritik nokta: 100 kapatıldıktan sonra bakiye 900 kalır,
        // yani "tutar bakiyeyi aşmıyor" çiti aynı kalemi TEKRAR geçirirdi.
        await DebitAsync(sp, account, 100m, "K1");
        await DebitAsync(sp, account, 900m, "K2");
        var k1 = (await cash.GetStatementAsync(account)).Satirlar
            .First(x => x.Direction == LedgerDirection.Debit && x.Amount.Amount == 100m).Id;

        Assert.Equal(100m, await cash.CloseSingleAccountBulkAsync(account, [k1], LedgerAccountType.Kasa));
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(account));

        // AYNI kalem ikinci kez: tahsis çiti reddeder (bakiye hâlâ 900, eski çit geçirirdi).
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(account, [k1], LedgerAccountType.Kasa));
        Assert.Contains("kapatılmış", ex.Message);
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(account));   // alınmamış tahsilat YAZILMADI

        // Kapanan kalem "kapalı" olarak raporlanır (ekran bunu gösterir).
        Assert.Equal(100m, (await cash.SettledAmountsAsync([k1]))[k1]);
    }

    [Fact]
    public async Task Tek_cari_KISMI_kapatma_kalani_acik_birakir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: 1000'lik tek borç; önce 400, sonra 600 kapatılır.
        await DebitAsync(sp, account, 1000m, "Fatura");
        var k = (await cash.GetStatementAsync(account)).Satirlar
            .First(x => x.Direction == LedgerDirection.Debit && x.AccountType == LedgerAccountType.Cari).Id;

        Assert.Equal(400m, await cash.CloseSingleAccountBulkAsync(
            account, new Dictionary<Guid, decimal?> { [k] = 400m }, LedgerAccountType.Kasa));
        Assert.Equal(600m, await cash.GetAccountBalanceAsync(account));
        Assert.Equal(400m, (await cash.SettledAmountsAsync([k]))[k]);

        // Kalanı aşan istek reddedilir (601 > 600).
        await Assert.ThrowsAsync<ValidationException>(() => cash.CloseSingleAccountBulkAsync(
            account, new Dictionary<Guid, decimal?> { [k] = 601m }, LedgerAccountType.Kasa));

        // Tutar verilmezse KALAN kapatılır → 600.
        Assert.Equal(600m, await cash.CloseSingleAccountBulkAsync(account, [k], LedgerAccountType.Kasa));
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));

        // Artık tamamen kapalı: üçüncü deneme reddedilir.
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(account, [k], LedgerAccountType.Kasa));
    }

    /// <summary>ADVERSARIAL M3 — dövizli kalemde yuvarlama bakiyeyi EKSİYE düşürüyordu
    /// (100 EUR × 35,123456 = 3512,3456 → iki taraf da yukarı yuvarlanınca 3512,35 postlanıp
    /// bakiye −0,0044 kalıyordu). Artık aşağı yuvarlanır: bakiye asla eksiye düşmez.</summary>
    [Fact]
    public async Task Tek_cari_dovizli_kalemde_bakiye_EKSIYE_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: 100 EUR × 35,123456 = 3512,3456 baz borç (kuruşa tam bölünmez).
        await sp.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = account, Tutar = 100m, Doviz = "EUR", Kur = 35.123456m, Hesap = LedgerAccountType.Kasa });

        var k = (await cash.GetStatementAsync(account)).Satirlar
            .First(x => x.Direction == LedgerDirection.Debit && x.AccountType == LedgerAccountType.Cari).Id;

        var collect = await cash.CloseSingleAccountBulkAsync(account, [k], LedgerAccountType.Kasa);
        Assert.Equal(3512.34m, collect);                              // AŞAĞI yuvarlandı (3512,35 değil)
        Assert.True(await cash.GetAccountBalanceAsync(account) >= 0m);     // bakiye EKSİYE düşmedi
    }

    [Fact]
    public async Task Tek_cari_kismi_odenmis_borcta_ASIRI_secim_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: 1000 borç, sonra 800 normal tahsilat → bakiye 200.
        await DebitAsync(sp, account, 1000m, "Borç");
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 800m, Hesap = LedgerAccountType.Kasa });
        Assert.Equal(200m, await cash.GetAccountBalanceAsync(account));

        // 1000'lik kalemi kapatmaya çalışmak bakiyeyi aşar → red (kalem kısmen ödenmiş).
        var item = (await cash.GetStatementAsync(account)).Satirlar
            .Single(x => x.Direction == LedgerDirection.Debit && x.AccountType == LedgerAccountType.Cari).Id;
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(account, [item], LedgerAccountType.Kasa));
        Assert.Contains("aşıyor", ex.Message);
        Assert.Equal(200m, await cash.GetAccountBalanceAsync(account));
    }

    [Fact]
    public async Task Tek_cari_yabanci_ve_ALACAK_kalem_secimi_GURULTULU_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        await DebitAsync(sp, a, 300m, "A borç");
        await DebitAsync(sp, b, 400m, "B borç");
        await cash.CollectAsync(new CashInput { CariId = a, Tutar = 100m, Hesap = LedgerAccountType.Kasa });

        var aRows = (await cash.GetStatementAsync(a)).Satirlar.ToList();
        var bDebit = (await cash.GetStatementAsync(b)).Satirlar
            .First(x => x.Direction == LedgerDirection.Debit && x.AccountType == LedgerAccountType.Cari).Id;
        var aCredit = aRows.First(x => x.Direction == LedgerDirection.Credit).Id;

        // BAŞKA carinin kalemi: sessizce atlamak, kullanıcının seçtiğinden farklı tutar tahsil ederdi.
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(a, [bDebit], LedgerAccountType.Kasa));
        // ALACAK kalemi kapatılamaz (o zaten ödemedir).
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(a, [aCredit], LedgerAccountType.Kasa));
        // Boş seçim.
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(a, [], LedgerAccountType.Kasa));

        Assert.Equal(200m, await cash.GetAccountBalanceAsync(a));   // ELLE: 300 − 100, hiçbir şey yazılmadı
    }

    [Fact]
    public async Task Tek_cari_ayni_token_ile_cift_submit_TEK_tahsilat_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: 1000 borç; 400'lük kalemi kapatacağız (ikinci kalem 600 açık kalır).
        await DebitAsync(sp, account, 400m, "K1");
        await DebitAsync(sp, account, 600m, "K2");
        var k1 = (await cash.GetStatementAsync(account)).Satirlar
            .First(x => x.Direction == LedgerDirection.Debit && x.Amount.Amount == 400m).Id;

        var token = Guid.NewGuid();
        Assert.Equal(400m, await cash.CloseSingleAccountBulkAsync(account, [k1], LedgerAccountType.Kasa, operationKey: token));
        // ÇİFT SUBMIT (aynı token) → ikinci kayıt yazılmaz.
        await Assert.ThrowsAnyAsync<Exception>(
            () => cash.CloseSingleAccountBulkAsync(account, [k1], LedgerAccountType.Kasa, operationKey: token));

        Assert.Equal(600m, await cash.GetAccountBalanceAsync(account));   // 1000 − 400, İKİ KEZ düşmedi
        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(debit, credit);
    }

    [Fact]
    public async Task Tek_cari_kapatma_yetki_ve_tenant_citli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account;
        List<Guid> selected;
        using (var admin = host.ScopeFor(tenant))
        {
            var sp = admin.ServiceProvider;
            account = await CustomerAsync(sp);
            await DebitAsync(sp, account, 500m, "Borç");
            selected = (await sp.GetRequiredService<CashService>().GetStatementAsync(account)).Satirlar
                .Where(x => x.Direction == LedgerDirection.Debit && x.AccountType == LedgerAccountType.Cari)
                .Select(x => x.Id).ToList();
        }

        // Operatör FinanceWrite taşımaz.
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
            await Assert.ThrowsAsync<NoPermissionException>(() => op.ServiceProvider
                .GetRequiredService<CashService>().CloseSingleAccountBulkAsync(account, selected, LedgerAccountType.Kasa));

        // BAŞKA tenant: cari de kalemler de görünmez → "bulunamadı" (RLS + query filter).
        using (var other = host.ScopeFor(Guid.NewGuid()))
            await Assert.ThrowsAsync<ValidationException>(() => other.ServiceProvider
                .GetRequiredService<CashService>().CloseSingleAccountBulkAsync(account, selected, LedgerAccountType.Kasa));

        using var back = host.ScopeFor(tenant);
        Assert.Equal(500m, await back.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(account));
    }

    [Fact]
    public async Task Cok_cari_toplu_tahsilat_modu_REGRESYONSUZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CustomerAsync(sp, "A");
        var b = await CustomerAsync(sp, "B");

        await DebitAsync(sp, a, 1000m, "A");
        await DebitAsync(sp, b, 2000m, "B");

        // Mevcut çok-cari modu: satır başına tutar YAZILIR (kalem seçimi yok).
        await cash.BatchCollectAsync(
        [
            new CashInput { CariId = a, Tutar = 400m, Hesap = LedgerAccountType.Kasa },
            new CashInput { CariId = b, Tutar = 500m, Hesap = LedgerAccountType.Banka }
        ], Guid.NewGuid());

        Assert.Equal(600m, await cash.GetAccountBalanceAsync(a));    // ELLE: 1000 − 400
        Assert.Equal(1500m, await cash.GetAccountBalanceAsync(b));   // ELLE: 2000 − 500
        var (debit, credit) = await LedgerAsync(sp);
        Assert.Equal(debit, credit);
    }
}
