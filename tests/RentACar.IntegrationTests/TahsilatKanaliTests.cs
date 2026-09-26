using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-84 — tahsilat/ödeme kanalı (Masaüstü/Mobil/Tablet). KARARLAR.md: kanal SAF BİLGİ olarak
/// <see cref="CashTransaction"/> belgesine yazılır; <see cref="RentACar.Domain.Entities.AccountLedgerEntry"/>
/// şemasına/dengesine DOKUNULMAZ. Beklenen değerler elle kurulmuş senaryodan türetilir (bağımsız oracle).
///
/// Bu sınıf aynı zamanda faz'ın ZORUNLU kırılgan regresyon testini taşır: kanal doldurulduğunda
/// defter dengesi, cari bakiye ve kasa/banka özeti BİT-BİREBİR aynı kalır — bkz.
/// <see cref="Kanal_dolu_iken_defter_bakiye_ve_kasa_ozeti_kanalsiz_ile_BIT_BIREBIR_ayni"/>.
/// </summary>
[Collection("postgres")]
public sealed class TahsilatKanaliTests(PostgresFixture fx)
{
    private static async Task<Guid> SeedCustomerAsync(IServiceProvider sp, string name)
        => await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });

    private static IDbContextFactory<AppDbContext> Factory(IServiceProvider sp)
        => sp.GetRequiredService<IDbContextFactory<AppDbContext>>();

    // ---- 1. Açık kanal aynen yazılır ----
    [Fact]
    public async Task Kanal_verilince_CashTransaction_belgesine_aynen_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope.ServiceProvider, "KanalYazim");

        var id = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 250m, Kanal = "Mobil" });

        var tx = await cash.GetAsync(id);
        Assert.NotNull(tx);
        Assert.Equal("Mobil", tx!.Kanal);
    }

    // ---- 2. Boş/whitespace kanal → "Masaüstü" varsayılanı ----
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Bos_kanal_Masaustu_varsayilanina_duser(string? entered)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope.ServiceProvider, "KanalBos");

        var id = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 100m, Kanal = entered });

        var tx = await cash.GetAsync(id);
        Assert.Equal(CashKanal.Masaustu, tx!.Kanal);
    }

    // ---- 3. Case-insensitive eşleşme kanonik forma normalize edilir ----
    [Fact]
    public async Task Kucuk_harfli_kanal_kanonik_forma_normalize_edilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope.ServiceProvider, "KanalKucuk");

        var id = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 100m, Kanal = "tablet" });

        var tx = await cash.GetAsync(id);
        Assert.Equal("Tablet", tx!.Kanal);
    }

    // ---- 4. Bilinmeyen serbest metin GÜRÜLTÜLÜ reddedilir (sessiz normalize yanlış rapor üretirdi) ----
    [Fact]
    public async Task Gecersiz_kanal_gurultulu_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope.ServiceProvider, "KanalGecersiz");

        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CollectAsync(new CashInput { CariId = account, Tutar = 100m, Kanal = "Drone" }));

        // Reddedilen giriş HİÇ yazılmamalı (yarım/hatalı kayıt kalmaz).
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));
    }

    // ---- 5. ZORUNLU kırılgan regresyon: kanal doldurulunca defter/bakiye/özet BİT-BİREBİR aynı ----
    [Fact]
    public async Task Kanal_dolu_iken_defter_bakiye_ve_kasa_ozeti_kanalsiz_ile_BIT_BIREBIR_ayni()
    {
        using var host = new TestHost(fx.AppConnectionString);

        // İki AYRI tenant/senaryo: biri kanalsız (kontrol), biri "Mobil" kanallı. Aynı tutar/hesap/
        // döviz ile TIPATIP aynı işlemi yapıp defter+bakiye+özeti karşılaştırıyoruz — kanalın
        // deftere/bakiyeye/kasa özetine hiç sızmadığının ampirik kanıtı.
        decimal debitCheck, creditCheck, balanceCheck;
        CashboxSummaryDto summaryCheck;
        using (var check = host.ScopeFor(Guid.NewGuid()))
        {
            var cash = check.ServiceProvider.GetRequiredService<CashService>();
            var reports = check.ServiceProvider.GetRequiredService<ReportService>();
            var account = await SeedCustomerAsync(check.ServiceProvider, "KontrolKanalsiz");
            await cash.CollectAsync(new CashInput { CariId = account, Tutar = 1234.56m, Hesap = LedgerAccountType.Banka });

            await using var db = await Factory(check.ServiceProvider).CreateDbContextAsync();
            var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
            debitCheck = rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase);
            creditCheck = rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase);
            balanceCheck = await cash.GetAccountBalanceAsync(account);
            summaryCheck = await reports.GetCashBankSummaryAsync();
        }

        decimal debitMobile, creditMobile, balanceMobile;
        CashboxSummaryDto summaryMobile;
        using (var mobile = host.ScopeFor(Guid.NewGuid()))
        {
            var cash = mobile.ServiceProvider.GetRequiredService<CashService>();
            var reports = mobile.ServiceProvider.GetRequiredService<ReportService>();
            var account = await SeedCustomerAsync(mobile.ServiceProvider, "KanalMobil");
            await cash.CollectAsync(new CashInput
            { CariId = account, Tutar = 1234.56m, Hesap = LedgerAccountType.Banka, Kanal = "Mobil" });

            await using var db = await Factory(mobile.ServiceProvider).CreateDbContextAsync();
            var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
            debitMobile = rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase);
            creditMobile = rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase);
            balanceMobile = await cash.GetAccountBalanceAsync(account);
            summaryMobile = await reports.GetCashBankSummaryAsync();
        }

        // Ayrı tenant'lar (izole) — mutlak değerler değil, ŞEKİL bit-birebir aynı olmalı.
        Assert.Equal(debitCheck, creditCheck); // kontrol dengeli
        Assert.Equal(debitMobile, creditMobile);     // Mobil de dengeli
        Assert.Equal(debitCheck, debitMobile);    // aynı tutar → aynı defter büyüklüğü
        Assert.Equal(balanceCheck, balanceMobile);
        Assert.Equal(summaryCheck.BankaGiris, summaryMobile.BankaGiris);
        Assert.Equal(summaryCheck.BankaBakiye, summaryMobile.BankaBakiye);
        Assert.Equal(summaryCheck.KasaBakiye, summaryMobile.KasaBakiye);

        // AccountLedgerEntry satırlarında "Kanal" diye bir kolon YOK — şemaya hiç dokunulmadığının
        // yapısal kanıtı (derleme-zamanı: tip üzerinde böyle bir üye yok; burada isim listesiyle
        // ek doğrulama — ileride biri yanlışlıkla eklerse bu test kırılır ve niyeti hatırlatır).
        var ledgerProps = typeof(RentACar.Domain.Entities.AccountLedgerEntry)
            .GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Kanal", ledgerProps);
    }

    // ---- 6. Ters kayıt orijinalin kanalını KORUR (kaybolmaz) ----
    [Fact]
    public async Task Ters_kayit_orijinalin_kanalini_korur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope.ServiceProvider, "TersKanal");

        var id = await cash.PayAsync(new CashInput { CariId = account, Tutar = 500m, Kanal = "Tablet" });
        var revId = await cash.ReverseAsync(id);

        var rev = await cash.GetAsync(revId);
        Assert.Equal("Tablet", rev!.Kanal);
    }

    // ---- 7. İdempotency BOZULMAZ: aynı IslemAnahtari + FARKLI kanal ile 2. POST reddedilir,
    //         kayıt TEK kalır ve ORİJİNAL kanal değişmez (kanal dedup anahtarına GİRMEZ) ----
    [Fact]
    public async Task Ayni_islemanahtari_farkli_kanalla_ikinci_yazim_reddedilir_orijinal_kanal_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account;
        using (var seed = host.ScopeFor(tenant)) account = await SeedCustomerAsync(seed.ServiceProvider, "IdempKanal");

        var token = Guid.NewGuid();
        using var s1 = host.ScopeFor(tenant);
        var id = await s1.ServiceProvider.GetRequiredService<CashService>().CollectAsync(
            new CashInput { CariId = account, Tutar = 100m, IslemAnahtari = token, Kanal = "Masaüstü" });

        using var s2 = host.ScopeFor(tenant);
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            s2.ServiceProvider.GetRequiredService<CashService>().CollectAsync(
                new CashInput { CariId = account, Tutar = 100m, IslemAnahtari = token, Kanal = "Mobil" }));

        using var check = host.ScopeFor(tenant);
        var sp = check.ServiceProvider;
        var tx = await sp.GetRequiredService<CashService>().GetAsync(id);
        Assert.Equal("Masaüstü", tx!.Kanal); // ikinci (Mobil) denemesi hiç yazılmadı
        Assert.Equal(-100m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account)); // tek kayıt
    }

    // ---- 8. Toplu tahsilat: satır-bazlı kanal doğru satıra yazılır ----
    [Fact]
    public async Task Toplu_tahsilatta_kanal_satir_bazli_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var c1 = await SeedCustomerAsync(scope.ServiceProvider, "Toplu1");
        var c2 = await SeedCustomerAsync(scope.ServiceProvider, "Toplu2");

        await cash.BatchCollectAsync(
        [
            new CashInput { CariId = c1, Tutar = 100m, Kanal = "Mobil" },
            new CashInput { CariId = c2, Tutar = 200m, Kanal = "Tablet" }
        ]);

        var all = await cash.ListAsync();
        Assert.Equal("Mobil", all.Single(t => t.CariId == c1).Kanal);
        Assert.Equal("Tablet", all.Single(t => t.CariId == c2).Kanal);
    }

    // ---- 9. Tek-cari toplu kapatma: kanal opsiyonel parametreden geçer ----
    [Fact]
    public async Task TekCariTopluKapat_kanal_verilirse_belgeye_yazilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var invoices = scope.ServiceProvider.GetRequiredService<InvoiceService>();
        var account = await SeedCustomerAsync(scope.ServiceProvider, "TekCariKanal");

        // Basit borç kalemi: manuel fatura (Borç Cari) — TekCariTopluKapat bunu ekstreden okur.
        await invoices.CreateManualAsync(new ManualInvoiceInput
        { CariId = account, NetTutar = 1000m, KdvOrani = 0m, Aciklama = "Kanal test borcu" });

        var statement = await cash.GetStatementAsync(account);
        var debit = statement.Satirlar.Single(s => s.Direction == LedgerDirection.Debit);

        var amount = await cash.CloseSingleAccountBulkAsync(
            account, [debit.Id], LedgerAccountType.Kasa, channel: "Tablet");
        Assert.Equal(1000m, amount);

        var all = await cash.ListAsync();
        var tx = all.Single(t => t.CariId == account && t.Tip == CashTransactionType.Tahsilat);
        Assert.Equal("Tablet", tx.Kanal);
    }

    // ---- 10. Tenant izolasyonu: A'nın "Mobil" kanallı tahsilatı B'nin listesinde hiç görünmez ----
    [Fact]
    public async Task Kanal_tenantlar_arasi_sizmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var a = host.ScopeFor(tenantA))
        {
            var cash = a.ServiceProvider.GetRequiredService<CashService>();
            var account = await SeedCustomerAsync(a.ServiceProvider, "IzoleA");
            await cash.CollectAsync(new CashInput { CariId = account, Tutar = 500m, Kanal = "Mobil" });
        }

        using var b = host.ScopeFor(tenantB);
        var listB = await b.ServiceProvider.GetRequiredService<CashService>().ListAsync();
        Assert.Empty(listB);
    }
}
