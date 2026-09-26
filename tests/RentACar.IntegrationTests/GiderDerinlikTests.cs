using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-64 — gider bilgi alanları + KISMİ ÖDEME TAKİBİ.
///
/// <para><b>KARARLAR.md FAZ-64 kararı (i):</b> gider ilk girişte TAM tutarıyla deftere yazılır;
/// "ödenen/kalan" YALNIZ TAKİP alanıdır. Ödeme kaydı deftere HİÇBİR ŞEY yazmaz — gerçek nakit
/// çıkışı tedarikçiye yapılan cari ödemesiyle yürür; buraya defter bağlamak o hareketi İKİNCİ kez
/// saydırırdı. Buradaki "defter değişmedi" testleri o kararı kalıcı kılıyor.</para>
///
/// <para><b>Spec'ten sapma (kayda değer):</b> spec `Expense`'e `OdenenTutar` kolonu eklemeyi
/// öneriyordu; `Expenses` tablosu DB-DEĞİŞMEZ (<c>expenses_immutable</c> trigger) olduğu için o
/// kolon bir daha asla güncellenemezdi. Ödemeler MTV/muayene (FAZ-14) ve ceza (FAZ-60) ile AYNI
/// desende append-only satırlar olarak tutuluyor.</para>
///
/// <para><b>Bağımsız oracle:</b> beklenen kalan/toplamlar elle kurulan senaryodan (1200'lük gidere
/// 400 → kalan 800), servis kodundan DEĞİL.</para>
/// </summary>
[Collection("postgres")]
public sealed class GiderDerinlikTests(PostgresFixture fx)
{
    private static DateTimeOffset Day(int difference)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(difference), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> CustomerAsync(IServiceScope s, string name)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = name });

    /// <summary>Açık hesap gideri: 1000 net + %20 KDV = 1200 borç.</summary>
    private static async Task<Guid> OpenAccountExpenseAsync(IServiceScope s, Guid account, decimal net = 1000m)
    {
        await s.ServiceProvider.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = net, KdvOrani = 0.20m,
            OdemeYontemi = PaymentMethod.AcikHesap, CariId = account
        });
        var list = await s.ServiceProvider.GetRequiredService<ExpenseService>().ListAsync();
        return list.OrderByDescending(x => x.CreatedAtUtc).First().Id;
    }

    private static async Task<(int Satir, decimal Borc, decimal Alacak)> LedgerAsync(
        TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync();
        return (rows.Count,
            rows.Where(x => x.Direction == LedgerDirection.Debit).Sum(x => x.A * x.R),
            rows.Where(x => x.Direction == LedgerDirection.Credit).Sum(x => x.A * x.R));
    }

    // ---------------------------------------------------------------- Bilgi alanları

    [Fact]
    public async Task Bilgi_alanlari_round_trip_ve_deftere_GIRMEZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cash = await scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa" });

        var paymentDate = Day(-2);
        var rentalId = Guid.NewGuid();   // gevşek referans (FK yok) — bilgi
        await expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m,
            OdemeYontemi = PaymentMethod.Nakit, FinansalHesapId = cash,
            OdemeTarihi = paymentDate, HazirAciklama = "Yakıt", RentalId = rentalId
        });

        var expense = Assert.Single(await expenses.ListAsync());
        Assert.Equal(paymentDate, expense.OdemeTarihi);
        Assert.Equal("Yakıt", expense.HazirAciklama);
        Assert.Equal(rentalId, expense.RentalId);

        // Defter: elle 100 Gider Borç / 100 Kasa Alacak — bilgi alanları hiçbir satır üretmedi.
        var (row, debit, credit) = await LedgerAsync(host, tenant);
        Assert.Equal(2, row);
        Assert.Equal(100m, debit);
        Assert.Equal(100m, credit);
    }

    // ---------------------------------------------------------------- Kısmi ödeme

    [Fact]
    public async Task Kismi_odeme_kalani_dusurur_ve_DEFTERE_YAZMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var account = await CustomerAsync(scope, "Tedarikçi");
        var expenseId = await OpenAccountExpenseAsync(scope, account);

        var ledgerBefore = await LedgerAsync(host, tenant);

        // ELLE: 1200 borca 400 ödeme → kalan 800.
        await expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 400m });

        var status = (await expenses.PaymentStatusesAsync(await expenses.ListAsync()))[expenseId];
        Assert.True(status.TakipEdilir);
        Assert.Equal(1200m, status.GenelToplam);
        Assert.Equal(400m, status.Odenen);
        Assert.Equal(800m, status.Kalan);
        Assert.False(status.TamamenOdendi);

        // KRİTİK: defter BİT-BİREBİR aynı (karar (i) — ödeme takip alanıdır).
        Assert.Equal(ledgerBefore, await LedgerAsync(host, tenant));

        // İkinci ödeme kalanı kapatır.
        await expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 800m });
        var status2 = (await expenses.PaymentStatusesAsync(await expenses.ListAsync()))[expenseId];
        Assert.Equal(0m, status2.Kalan);
        Assert.True(status2.TamamenOdendi);
        Assert.Equal(ledgerBefore, await LedgerAsync(host, tenant));
    }

    [Fact]
    public async Task Tutar_verilmezse_kalanin_tamami_odenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var account = await CustomerAsync(scope, "Tek Tık");
        var expenseId = await OpenAccountExpenseAsync(scope, account);

        await expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 200m });
        await expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId });   // kalanın tamamı

        var status = (await expenses.PaymentStatusesAsync(await expenses.ListAsync()))[expenseId];
        Assert.Equal(1200m, status.Odenen);
        Assert.Equal(0m, status.Kalan);
    }

    [Fact]
    public async Task Nakit_banka_giderinde_takip_YOK_borc_da_yok()
    {
        // Para kayıt anında kasadan çıktı (defter öyle yazıldı) → "kalan" kavramı yok.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cash = await scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa" });
        await expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = 500m, KdvOrani = 0m,
            OdemeYontemi = PaymentMethod.Nakit, FinansalHesapId = cash
        });
        var expense = Assert.Single(await expenses.ListAsync());

        var status = (await expenses.PaymentStatusesAsync([expense]))[expense.Id];
        Assert.False(status.TakipEdilir);
        Assert.Equal(500m, status.Odenen);
        Assert.Equal(0m, status.Kalan);

        // Nakit gidere ödeme kaydı GÜRÜLTÜLÜ reddedilir — sessizce kabul etmek sahte borç yaratırdı.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expense.Id, Tutar = 100m }));
        Assert.Contains("açık hesap", ex.Message);
    }

    // ---------------------------------------------------------------- Adversarial kilitler

    [Fact]
    public async Task Asiri_odeme_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var account = await CustomerAsync(scope, "Asiri");
        var expenseId = await OpenAccountExpenseAsync(scope, account);

        // Tek seferde aşırı.
        await Assert.ThrowsAsync<ValidationException>(() =>
            expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 1200.01m }));

        // Kısmi ödemeden SONRA aşırı (kalan 200 iken 300).
        await expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 1000m });
        await Assert.ThrowsAsync<ValidationException>(() =>
            expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 300m }));

        var status = (await expenses.PaymentStatusesAsync(await expenses.ListAsync()))[expenseId];
        Assert.Equal(1000m, status.Odenen);   // reddedilen ödeme hiç yazılmadı
    }

    [Fact]
    public async Task Cift_submit_yutulur_kalan_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var account = await CustomerAsync(scope, "CiftSubmit");
        var expenseId = await OpenAccountExpenseAsync(scope, account);

        var key = Guid.NewGuid();
        var first = await expenses.AddPaymentAsync(new GiderOdemeInput
        { ExpenseId = expenseId, Tutar = 300m, IslemAnahtari = key });
        var second = await expenses.AddPaymentAsync(new GiderOdemeInput
        { ExpenseId = expenseId, Tutar = 300m, IslemAnahtari = key });

        Assert.NotNull(first);
        Assert.Null(second);   // yutuldu
        var status = (await expenses.PaymentStatusesAsync(await expenses.ListAsync()))[expenseId];
        Assert.Equal(300m, status.Odenen);   // 600 DEĞİL
    }

    [Fact]
    public async Task Escanzamanli_odemeler_kalani_asamaz()
    {
        // TOCTOU: kilitsiz "önce oku sonra yaz" olsaydı üç istek birlikte geçip 900 yazardı.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid expenseId;
        using (var scope = host.ScopeFor(tenant))
        {
            var account = await CustomerAsync(scope, "Yaris");
            expenseId = await OpenAccountExpenseAsync(scope, account, net: 250m);   // 300 borç
        }

        // 4 paralel × 100 → tam 3'ü geçmeli (300), dördüncü reddedilmeli.
        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            using var s = host.ScopeFor(tenant);
            var svc = s.ServiceProvider.GetRequiredService<ExpenseService>();
            try { await svc.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 100m }); return true; }
            catch (ValidationException) { return false; }
        }).ToList();
        var results = await Task.WhenAll(tasks);

        using var read = host.ScopeFor(tenant);
        var expenses = read.ServiceProvider.GetRequiredService<ExpenseService>();
        var status = (await expenses.PaymentStatusesAsync(await expenses.ListAsync()))[expenseId];
        Assert.Equal(300m, status.Odenen);
        Assert.Equal(0m, status.Kalan);
        Assert.Equal(3, results.Count(x => x));
    }

    [Fact]
    public async Task Odeme_kaydi_DB_seviyesinde_degismez()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid expenseId;
        using (var scope = host.ScopeFor(tenant))
        {
            var account = await CustomerAsync(scope, "Degismez");
            expenseId = await OpenAccountExpenseAsync(scope, account);
            await scope.ServiceProvider.GetRequiredService<ExpenseService>()
                .AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 100m });
        }

        using var s2 = host.ScopeFor(tenant);
        var factory = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        // racar_app ile ham UPDATE denemesi: trigger reddetmeli (uygulama hatalı olsa bile DB tutar).
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE \"GiderOdemeleri\" SET \"Tutar\" = 999"));
    }

    [Fact]
    public async Task Gelecek_tarihli_odeme_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var account = await CustomerAsync(scope, "Gelecek");
        var expenseId = await OpenAccountExpenseAsync(scope, account);

        await Assert.ThrowsAsync<ValidationException>(() => expenses.AddPaymentAsync(
            new GiderOdemeInput { ExpenseId = expenseId, Tutar = 100m, Tarih = Day(5) }));
    }

    [Fact]
    public async Task Odeme_tenant_izolasyonu_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid expenseId;
        using (var s1 = host.ScopeFor(t1))
        {
            var account = await CustomerAsync(s1, "T1 Tedarikçi");
            expenseId = await OpenAccountExpenseAsync(s1, account);
        }

        // Başka tenant'ın gider kimliğiyle ödeme: bulunamaz.
        using (var s2 = host.ScopeFor(t2))
        {
            var svc = s2.ServiceProvider.GetRequiredService<ExpenseService>();
            await Assert.ThrowsAsync<ValidationException>(() =>
                svc.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 100m }));
            Assert.Empty(await svc.ListAsync());
        }

        // Operatör ödeme yapamaz (FinanceWrite yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(() => op.ServiceProvider
            .GetRequiredService<ExpenseService>()
            .AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 100m }));
    }

    [Fact]
    public async Task Odemeler_raporlara_SIZMAZ()
    {
        // Kırılgan regresyon: gelir-gider ve kasa/banka özeti ödeme kayıtlarından ETKİLENMEZ.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await CustomerAsync(scope, "Rapor");
        var expenseId = await OpenAccountExpenseAsync(scope, account);

        var ggOnce = await reports.GetRevenueExpenseAsync();
        var kbOnce = await reports.GetCashBankSummaryAsync();

        await expenses.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 700m });

        var plAfter = await reports.GetRevenueExpenseAsync();
        var cbAfter = await reports.GetCashBankSummaryAsync();
        Assert.Equal(ggOnce.GiderToplam, plAfter.GiderToplam);
        Assert.Equal(ggOnce.NetKar, plAfter.NetKar);
        Assert.Equal(kbOnce.KasaBakiye, cbAfter.KasaBakiye);
        Assert.Equal(kbOnce.BankaBakiye, cbAfter.BankaBakiye);
        // Elle: gider 1000 net (KDV indirilecek, gider toplamına girmez).
        Assert.Equal(1000m, plAfter.GiderToplam);
    }
}
