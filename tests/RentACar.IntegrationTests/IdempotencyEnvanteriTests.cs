using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.DisHizmetler;
using RentACar.Application.Expenses;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.GelenEFaturalar;
using RentACar.Application.Hgs;
using RentACar.Application.Integrations;
using RentACar.Application.Penalties;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.4 — <c>docs/api/idempotency-envanteri.md</c> tablosunun KİLİDİ. Her satır: aynı işlem İKİ kez
/// gönderilir ve (1) bugünkü sonuç (istisna tipi ya da sessiz başarı) ile (2) para oracle'ı (tek defter
/// kümesi; bakiye çift gönderimden etkilenmez) doğrulanır.
///
/// <para><b>Bağımsız oracle (CLAUDE.md §3):</b> beklenen tutarlar senaryodan ELLE yazılır (ör. "250
/// tahsilat → cari −250"); rapor/servis kodundan türetilmez. Bakiye DB'den doğrudan okunur
/// (<see cref="Balance"/>: Borç +, Alacak −, baz para).</para>
///
/// <para><b>Test adı = envanter satır no.</b> Tabloyu değiştiren PR bu dosyayı da değiştirmek ZORUNDA.</para>
/// </summary>
[Collection("postgres")]
public sealed class IdempotencyEnvanteriTests(PostgresFixture fx)
{
    // =====================================================================================
    // yardımcılar
    // =====================================================================================

    private static async Task<decimal> Balance(IServiceProvider sp, LedgerAccountType tip, Guid? reference = null, bool referenceFilter = false)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == tip);
        if (referenceFilter || reference is not null) q = q.Where(e => e.AccountRef == reference);
        var rows = await q.Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync();
        return rows.Sum(r => (r.Direction == LedgerDirection.Debit ? 1m : -1m) * r.A * r.R);
    }

    private static Task<decimal> Account(IServiceProvider sp, Guid account) => Balance(sp, LedgerAccountType.Cari, account);

    private static async Task<int> LedgerLine(IServiceProvider sp, string sourceType)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await db.AccountLedgerEntries.AsNoTracking().CountAsync(e => e.SourceType == sourceType);
    }

    /// <summary>Tüm defter DENGELİ mi (Σ borç == Σ alacak) — her testin sonunda.</summary>
    private static async Task BalanceCheckAsync(IServiceProvider sp)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync();
        Assert.Equal(rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.A * r.R),
                     rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.A * r.R));
    }

    private static async Task<int> Say<T>(IServiceProvider sp, Func<AppDbContext, IQueryable<T>> q)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await q(db).CountAsync();
    }

    private static Task<Guid> CreateCustomer(IServiceProvider sp, string name = "Idem") =>
        sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Cari" });

    private static readonly DateTimeOffset RentalStart = new(2027, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid Kira, Guid Musteri, Guid Arac)> CreateRental(
        IServiceProvider sp, string plate, DateTimeOffset start, int day)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate });
        var m = await CreateCustomer(sp, "Kiraci");
        var k = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = start, BitTar = start.AddDays(day), GunlukUcret = 100m });
        return (k, m, v);
    }

    /// <summary>Eşzamanlı iki gönderim: ayrı scope (ayrı DbContext) + aynı kiracı.</summary>
    private static async Task<(T? Deger, Exception? Hata)[]> TwoConcurrent<T>(
        TestHost host, Guid tenant, Func<IServiceProvider, Task<T>> operation)
    {
        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        async Task<(T?, Exception?)> Sar(IServiceProvider sp)
        {
            try { return (await operation(sp), null); }
            catch (Exception ex) { return (default, ex); }
        }
        var t1 = Task.Run(() => Sar(s1.ServiceProvider));
        var t2 = Task.Run(() => Sar(s2.ServiceProvider));
        return await Task.WhenAll(t1, t2);
    }

    // =====================================================================================
    // Kasa / Banka (CashService)
    // =====================================================================================

    [Fact]
    public async Task E01_Tahsilat_anahtarli_ikinci_gonderim_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();

        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 250m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            cash.CollectAsync(new CashInput { CariId = account, Tutar = 250m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k }));

        Assert.Equal(-250m, await Account(sp, account));                      // ELLE: tahsilat 250 → cari −250
        Assert.Equal(250m, await Balance(sp, LedgerAccountType.Kasa));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E01_Tahsilat_anahtarli_ESZAMANLI_yalniz_biri_yazilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();

        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = account, Tutar = 250m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k }));

        Assert.Single(result, r => r.Hata is null);
        Assert.IsType<DuplicateOperationException>(Assert.Single(result, r => r.Hata is not null).Hata);
        Assert.Equal(-250m, await Account(sp, account));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E01_Tahsilat_anahtarsiz_iki_gonderim_iki_ayri_islem()
    {
        // Mekanizma yalnız anahtarla çalışır: anahtarsız iki çağrı iki MEŞRU tahsilattır (bugünkü sözleşme).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);

        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        Assert.Equal(-200m, await Account(sp, account));
    }

    [Fact]
    public async Task E02_Odeme_anahtarli_ikinci_gonderim_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();

        await cash.PayAsync(new CashInput { CariId = account, Tutar = 400m, Hesap = LedgerAccountType.Banka, IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            cash.PayAsync(new CashInput { CariId = account, Tutar = 400m, Hesap = LedgerAccountType.Banka, IslemAnahtari = k }));

        Assert.Equal(400m, await Account(sp, account));                       // ELLE: ödeme 400 → cari +400
        Assert.Equal(-400m, await Balance(sp, LedgerAccountType.Banka));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E03_E04_Toplu_tahsilat_ve_odeme_RowKey_ikinci_gonderim_409_hic_satir_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var a = await CreateCustomer(sp, "A");
        var b = await CreateCustomer(sp, "B");
        var collectionBatch = Guid.NewGuid();
        var paymentBatch = Guid.NewGuid();
        CashInput[] Rows() =>
        [
            new() { CariId = a, Tutar = 100m, Hesap = LedgerAccountType.Kasa },
            new() { CariId = b, Tutar = 200m, Hesap = LedgerAccountType.Kasa }
        ];

        await cash.BatchCollectAsync(Rows(), collectionBatch);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.BatchCollectAsync(Rows(), collectionBatch));
        await cash.BatchPayAsync(Rows(), paymentBatch);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.BatchPayAsync(Rows(), paymentBatch));

        // ELLE: tahsilat −100/−200 + ödeme +100/+200 → iki cari de 0; 4 belge (2+2), mükerrerler 0 satır.
        Assert.Equal(0m, await Account(sp, a));
        Assert.Equal(0m, await Account(sp, b));
        Assert.Equal(4, await Say(sp, db => db.CashTransactions));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E05_Tek_cari_kapatma_TAM_kapatmada_da_ikinci_gonderim_409()
    {
        // ÖNCE: ilk gönderim kalemi tamamen kapattıysa ikinci "zaten kapatılmış" (400) alıyordu;
        // kısmi kapattıysa kısıt (409). F1.4: anahtar önce → her iki halde 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        await cash.PayAsync(new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        var item = (await cash.GetStatementAsync(account)).Satirlar.Single(x => x.Direction == LedgerDirection.Debit).Id;
        var k = Guid.NewGuid();

        Assert.Equal(100m, await cash.CloseSingleAccountBulkAsync(account, [item], LedgerAccountType.Kasa, operationKey: k));
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            cash.CloseSingleAccountBulkAsync(account, [item], LedgerAccountType.Kasa, operationKey: k));
        Assert.Equal("Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).", ex.Message);

        Assert.Equal(0m, await Account(sp, account));                          // ELLE: borç 100 − tahsilat 100
        Assert.Equal(1, await Say(sp, db => db.CashTransactions.Where(t => t.Tip == CashTransactionType.Tahsilat)));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E05_Tek_cari_kapatma_KISMI_kapatmada_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        await cash.PayAsync(new CashInput { CariId = account, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });
        var item = (await cash.GetStatementAsync(account)).Satirlar.Single(x => x.Direction == LedgerDirection.Debit).Id;
        var k = Guid.NewGuid();
        var selection = new Dictionary<Guid, decimal?> { [item] = 400m };

        await cash.CloseSingleAccountBulkAsync(account, selection, LedgerAccountType.Kasa, operationKey: k);
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            cash.CloseSingleAccountBulkAsync(account, selection, LedgerAccountType.Kasa, operationKey: k));

        Assert.Equal(600m, await Account(sp, account));                        // ELLE: 1000 − 400
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E06_Kasa_banka_virman_anahtarli_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var k = Guid.NewGuid();

        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m, operationKey: k);
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m, operationKey: k); // istisna YOK

        Assert.Equal(-500m, await Balance(sp, LedgerAccountType.Kasa));   // ELLE: tek virman 500
        Assert.Equal(500m, await Balance(sp, LedgerAccountType.Banka));
        Assert.Equal(2, await LedgerLine(sp, "Virman"));
        Assert.Equal(1, await Say(sp, db => db.Set<KasaVirmanBilgi>()));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E07_Cari_virman_anahtarli_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var source = await CreateCustomer(sp, "Kaynak");
        var target = await CreateCustomer(sp, "Hedef");
        var k = Guid.NewGuid();

        await cash.TransferBetweenAccountsAsync(source, target, 300m, operationKey: k);
        await cash.TransferBetweenAccountsAsync(source, target, 300m, operationKey: k);

        Assert.Equal(300m, await Account(sp, target));                        // ELLE: hedef Borç 300
        Assert.Equal(-300m, await Account(sp, source));                      //       kaynak Alacak 300
        Assert.Equal(1, await Say(sp, db => db.Set<CariVirmanBilgi>()));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E08_Ters_kayit_sirali_ikinci_istek_ARTIK_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        var id = await cash.CollectAsync(new CashInput { CariId = account, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });

        await cash.ReverseAsync(id);
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.ReverseAsync(id));
        Assert.Equal("Bu işlem zaten ters kaydedilmiş.", ex.Message);

        Assert.Equal(0m, await Account(sp, account));                           // ELLE: −1000 + 1000
        Assert.Equal(1, await Say(sp, db => db.CashTransactions.Where(t => t.TersKayitMi)));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E08_Ters_kayit_ESZAMANLI_kaybeden_ayni_tip_ve_ayni_mesaj()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var account = await CreateCustomer(sp);
        var id = await sp.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = account, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });

        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<CashService>().ReverseAsync(id));

        Assert.Single(result, r => r.Hata is null);
        var error = Assert.IsType<DuplicateOperationException>(Assert.Single(result, r => r.Hata is not null).Hata);
        // Yarışı ön-kontrolde de kaybetse kısıtta da kaybetse AYNI metin (zamanlamadan bağımsız).
        Assert.Equal("Bu işlem zaten ters kaydedilmiş.", error.Message);
        Assert.Equal(0m, await Account(sp, account));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions.Where(t => t.TersKayitMi)));
    }

    // =====================================================================================
    // Depozito (sessiz idempotent — I3 sözleşmesi)
    // =====================================================================================

    [Fact]
    public async Task E09_Depozito_al_anahtarli_ikinci_gonderim_sessiz_ayni_id()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();

        var id1 = await dep.GetAsync(account, 500m, LedgerAccountType.Kasa, operationKey: k);
        var id2 = await dep.GetAsync(account, 500m, LedgerAccountType.Kasa, operationKey: k);

        Assert.Equal(k, id1);
        Assert.Equal(k, id2);
        Assert.Equal(500m, await dep.GetBalanceAsync(account));               // ELLE: tek 500
        Assert.Equal(500m, await Balance(sp, LedgerAccountType.Kasa));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E10_Depozito_iade_TUTULANIN_TAMAMI_icin_de_ikinci_gonderim_sessiz()
    {
        // ÖNCE: tamamını iade eden gönderimin tekrarı bakiye çitine takılıp 400 alıyordu (kısmi iade
        // sessiz geçiyordu) — sonuç tutara bağlıydı. F1.4: anahtar önce → sessiz.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        await dep.GetAsync(account, 500m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.RefundAsync(account, 500m, LedgerAccountType.Kasa, operationKey: k);
        await dep.RefundAsync(account, 500m, LedgerAccountType.Kasa, operationKey: k);

        Assert.Equal(0m, await dep.GetBalanceAsync(account));                 // ELLE: 500 − 500
        Assert.Equal(0m, await Balance(sp, LedgerAccountType.Kasa));
        Assert.Equal(2, await LedgerLine(sp, "DepozitoIade"));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E10_Depozito_iade_kismi_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        await dep.GetAsync(account, 500m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.RefundAsync(account, 200m, LedgerAccountType.Kasa, operationKey: k);
        await dep.RefundAsync(account, 200m, LedgerAccountType.Kasa, operationKey: k);

        Assert.Equal(300m, await dep.GetBalanceAsync(account));               // ELLE: 500 − 200
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E11_Depozito_mahsup_tamami_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        await dep.GetAsync(account, 300m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.OffsetAsync(account, 300m, operationKey: k);
        await dep.OffsetAsync(account, 300m, operationKey: k);

        Assert.Equal(0m, await dep.GetBalanceAsync(account));
        Assert.Equal(-300m, await Account(sp, account));                        // ELLE: Alacak Cari 300 (tek)
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E09_E11_Depozito_anahtari_TURLER_ARASI_tekil_baska_turde_mukerrer_ve_yazim_yok()
    {
        // #299 L2 (servis düzeyi — Blazor yolu ham anahtarı doğrudan geçirir): al'ın anahtarıyla iade/mahsup/irat
        // MukerrerIslemException (mevcut = al, ayniIcerik=false) ve HİÇBİR satır yazılmaz; ters yön de aynı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();
        Assert.Equal(k, await dep.GetAsync(account, 500m, LedgerAccountType.Kasa, operationKey: k));

        var refund = await Assert.ThrowsAsync<DuplicateOperationException>(
            () => dep.RefundAsync(account, 100m, LedgerAccountType.Kasa, operationKey: k));
        Assert.NotNull(refund.Existing);
        Assert.Equal(k, refund.Existing!.Id);
        Assert.Equal(500m, refund.Existing.Tutar);
        Assert.Equal("TRY", refund.Existing.Doviz);
        Assert.False(refund.Existing.AyniIcerik);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.OffsetAsync(account, 50m, operationKey: k));
        await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.ForfeitAsync(account, 20m, operationKey: k));

        Assert.Equal(500m, await dep.GetBalanceAsync(account));               // ELLE: yalnız al 500
        Assert.Equal(2, await LedgerLine(sp, "DepozitoAl"));
        Assert.Equal(0, await LedgerLine(sp, "DepozitoIade"));
        Assert.Equal(0, await LedgerLine(sp, "DepozitoMahsup"));
        Assert.Equal(0, await LedgerLine(sp, "DepozitoIrat"));
        Assert.Equal(0, await Say(sp, db => db.DepozitoIratlar));

        // Ters yön: mahsup anahtarıyla al → 409; mahsubun birebir tekrarı sessiz.
        var m = Guid.NewGuid();
        await dep.OffsetAsync(account, 100m, operationKey: m);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.GetAsync(account, 100m, LedgerAccountType.Kasa, operationKey: m));
        await dep.OffsetAsync(account, 100m, operationKey: m);
        Assert.Equal(400m, await dep.GetBalanceAsync(account));               // ELLE: 500 − 100
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E12_Depozito_irat_BASKA_KIRACININ_anahtariyla_400_mukerrer_degil_ve_yazim_yok()
    {
        // r314 P5: DepozitoIrat.Id = anahtar, PK kiracılar arası tekil. Başka kiracının kullandığı ham anahtar bu
        // kiracıda "görünmez" → mükerrer (409) DEĞİL, net 400 ve hiçbir satır yazılmaz.
        using var host = new TestHost(fx.AppConnectionString);
        var k = Guid.NewGuid();
        using (var other = host.ScopeFor(Guid.NewGuid()))
        {
            var sp0 = other.ServiceProvider;
            var dep0 = sp0.GetRequiredService<DepositService>();
            var account0 = await CreateCustomer(sp0);
            await dep0.GetAsync(account0, 50m, LedgerAccountType.Kasa);
            Assert.Equal(k, await dep0.ForfeitAsync(account0, 5m, operationKey: k));
        }
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        await dep.GetAsync(account, 50m, LedgerAccountType.Kasa);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => dep.ForfeitAsync(account, 5m, operationKey: k));
        Assert.IsNotType<DuplicateOperationException>(ex);
        Assert.Equal(50m, await dep.GetBalanceAsync(account));                // ELLE: irat yazılmadı
        Assert.Equal(0, await LedgerLine(sp, "DepozitoIrat"));
        Assert.Equal(0, await Say(sp, db => db.DepozitoIratlar));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E12_Depozito_irat_tamami_ikinci_gonderim_sessiz_ayni_id()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var account = await CreateCustomer(sp);
        await dep.GetAsync(account, 400m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        Assert.Equal(k, await dep.ForfeitAsync(account, 400m, operationKey: k));
        Assert.Equal(k, await dep.ForfeitAsync(account, 400m, operationKey: k));

        Assert.Equal(0m, await dep.GetBalanceAsync(account));
        Assert.Equal(-400m, await Balance(sp, LedgerAccountType.Gelir));   // ELLE: gelir 400 (Alacak)
        Assert.Equal(1, await Say(sp, db => db.DepozitoIratlar));
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Bakiye düzeltme (sessiz)
    // =====================================================================================

    [Fact]
    public async Task E13_Bakiye_duzeltme_anahtarli_ikinci_gonderim_sessiz_ayni_id()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<BalanceAdjustmentService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();
        BakiyeDuzeltmeInput Input() => new() { CariId = account, Tutar = 150m, Yon = BalanceAdjustmentDirection.Borclandir, IslemAnahtari = k };

        Assert.Equal(k, await svc.AdjustAsync(Input()));
        Assert.Equal(k, await svc.AdjustAsync(Input()));

        Assert.Equal(150m, await Account(sp, account));                          // ELLE: borçlandırma 150 (tek)
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Fatura
    // =====================================================================================

    [Fact]
    public async Task E14_Manuel_fatura_anahtarli_ikinci_gonderim_sessiz_mevcut_id()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();
        ManualInvoiceInput Input() => new() { CariId = account, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = k };

        Assert.Equal(k, await fat.CreateManualAsync(Input()));
        Assert.Equal(k, await fat.CreateManualAsync(Input()));

        Assert.Equal(1200m, await Account(sp, account));                         // ELLE: 1000 + %20 = 1200
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E14_Manuel_fatura_ESZAMANLI_ikisi_de_ayni_id_ile_sessiz_basari()
    {
        // ÖNCE: yarışı kaybeden PK'ye çarpıp "Kira zaten faturalanmış." (400) alıyordu.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();

        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = account, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = k }));

        Assert.All(result, r => Assert.Null(r.Hata));
        Assert.All(result, r => Assert.Equal(k, r.Deger));
        Assert.Equal(1200m, await Account(sp, account));
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E15_Kira_faturasi_ikinci_kesim_400_tam_faturalanmis()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (rental, customer, _) = await CreateRental(sp, "34 ID 15", RentalStart, 3);

        await fat.CreateFromRentalAsync(rental);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => fat.CreateFromRentalAsync(rental));
        Assert.Equal("Kira zaten tam faturalanmış (yeni ek bedel yok).", ex.Message);

        Assert.Equal(300m, await Account(sp, customer));                       // ELLE: 3 gün × 100 = 300 brüt
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E15_Kira_faturasi_ESZAMANLI_kaybeden_400_dogrulama()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (rental, customer, _) = await CreateRental(sp, "34 ID 16", RentalStart, 3);

        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental));

        Assert.Single(result, r => r.Hata is null);
        // Tip TAM ValidationException (mükerrer değil): kira-fatura kısıtı iş kuralıdır, idempotency kısıtı değil.
        Assert.Equal(typeof(ValidationException), Assert.Single(result, r => r.Hata is not null).Hata!.GetType());
        Assert.Equal(300m, await Account(sp, customer));
        Assert.Equal(1, await Say(sp, db => db.Invoices));
    }

    [Fact]
    public async Task E16_Toplu_fatura_ikinci_gonderim_yeni_belge_uretmez_atlananlara_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (k1, m1, _) = await CreateRental(sp, "34 ID 17", RentalStart, 3);
        var (k2, m2, _) = await CreateRental(sp, "34 ID 18", RentalStart, 2);

        var first = await fat.BatchCreateFromRentalsAsync([k1, k2]);
        var second = await fat.BatchCreateFromRentalsAsync([k1, k2]);

        Assert.Equal(2, first.Kesilen.Count);
        Assert.Empty(second.Kesilen);
        Assert.Equal(2, second.Atlananlar.Count);
        Assert.All(second.Atlananlar, a => Assert.Contains("tam faturalanmış", a));
        Assert.Equal(300m, await Account(sp, m1));                            // ELLE: 3 × 100
        Assert.Equal(200m, await Account(sp, m2));                            // ELLE: 2 × 100
        Assert.Equal(2, await Say(sp, db => db.Invoices));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E17_Iade_faturasi_ikinci_iade_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var account = await CreateCustomer(sp);
        var source = await fat.CreateManualAsync(new ManualInvoiceInput { CariId = account, NetTutar = 1000m, KdvOrani = 0.20m });

        await fat.CreateRefundAsync(source);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => fat.CreateRefundAsync(source));
        Assert.Equal("Bu fatura zaten iade edilmiş.", ex.Message);

        Assert.Equal(0m, await Account(sp, account));                            // ELLE: +1200 − 1200
        Assert.Equal(1, await Say(sp, db => db.Invoices.Where(i => i.IadeMi)));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E18_Donem_faturasi_ikinci_kesim_sessiz_ayni_fatura()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (rental, customer, _) = await CreateRental(sp, "34 ID 19", RentalStart, 90);

        var f1 = await fat.CreatePeriodInvoiceAsync(rental, 1);
        Assert.Equal(f1, await fat.CreatePeriodInvoiceAsync(rental, 1));

        Assert.Equal(3100m, await Account(sp, customer));                      // ELLE: D1 = 31 gün × 100
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E18_Donem_faturasi_ESZAMANLI_ikisi_de_ayni_fatura_id_ile_sessiz()
    {
        // ÖNCE: yarışı kaybeden "Kira faturaları bu sırada değişti" ya da "kesilecek tahakkuk kalmadı"
        // (400) alıyordu; sıralı ikinci istek sessiz başarı. F1.4: ikisi de mevcut fatura id'si.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (rental, customer, _) = await CreateRental(sp, "34 ID 20", RentalStart, 90);

        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<InvoiceService>().CreatePeriodInvoiceAsync(rental, 1));

        Assert.All(result, r => Assert.Null(r.Hata));
        Assert.Equal(result[0].Deger, result[1].Deger);
        Assert.Equal(3100m, await Account(sp, customer));
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        var period = (await sp.GetRequiredService<IInvoicePeriodRepository>().ListForRentalAsync(rental)).Single(d => d.DonemSira == 1);
        Assert.Equal(InvoicePeriodStatus.Kesildi, period.Durum);                // Atlandi'ye DÜŞMEDİ
    }

    [Fact]
    public async Task E19_Donem_kes_ve_tahsil_et_ikinci_gonderim_sessiz_tahsilat_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PeriodCollectionService>();
        var (rental, customer, _) = await CreateRental(sp, "34 ID 21", RentalStart, 90);

        var first = await svc.IssueAndCollectDetailAsync(rental, 1, true, LedgerAccountType.Kasa);
        var second = await svc.IssueAndCollectDetailAsync(rental, 1, true, LedgerAccountType.Kasa);

        Assert.True(first.TahsilatYazildi);
        Assert.False(second.TahsilatYazildi);
        Assert.Equal(first.InvoiceId, second.InvoiceId);
        Assert.Equal(0m, await Account(sp, customer));                          // ELLE: fatura 3100 − tahsilat 3100
        Assert.Equal(3100m, await Balance(sp, LedgerAccountType.Kasa));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E20_Otomatik_tahsilat_ikinci_calistirma_hic_bir_sey_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AutoCollectionService>();
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 ID 22" });
        var m = await CreateCustomer(sp, "Oto");
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = start, BitTar = start.AddDays(90), GunlukUcret = 100m, DonemselFaturalama = true });

        var selection = (await svc.CandidatesAsync()).Select(a => (a.RentalId, a.DonemSira)).ToList();
        Assert.Equal(2, selection.Count);                                       // ELLE: 65 gün → 2 dönem vadeli
        var first = await svc.RunAsync(selection, doCollection: true, LedgerAccountType.Kasa);
        var accountAfter = await Account(sp, m);
        var cashAfter = await Balance(sp, LedgerAccountType.Kasa);

        var second = await svc.RunAsync(selection, doCollection: true, LedgerAccountType.Kasa);

        Assert.Equal(2, first.Kesilen);
        Assert.Equal(2, first.Tahsilat);
        Assert.Equal(0, second.Kesilen);
        Assert.Equal(0, second.Tahsilat);
        Assert.Equal(2, second.Atlananlar.Count);
        Assert.Equal(0m, accountAfter);                                        // ELLE: kesilen = tahsil edilen
        Assert.Equal(accountAfter, await Account(sp, m));                          // çift çalıştırma bakiyeyi DEĞİŞTİRMEDİ
        Assert.Equal(cashAfter, await Balance(sp, LedgerAccountType.Kasa));
        Assert.Equal(2, await Say(sp, db => db.Invoices));
        Assert.Equal(2, await Say(sp, db => db.CashTransactions));
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Gider
    // =====================================================================================

    [Fact]
    public async Task E21_Tekil_gider_ARTIK_anahtar_tasir_ikinci_gonderim_409()
    {
        // ÖNCE: tekil giderde HİÇ mekanizma yoktu (her çağrı yeni gider). F1.4: IslemAnahtari.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var k = Guid.NewGuid();
        ExpenseInput Input() => new()
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, IslemAnahtari = k };

        await gid.CreateAsync(Input());
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() => gid.CreateAsync(Input()));
        Assert.Equal("Bu gider zaten kaydedilmiş (çift gönderim).", ex.Message);

        Assert.Equal(1000m, await Balance(sp, LedgerAccountType.Gider));      // ELLE: tek gider 1000
        Assert.Equal(-1000m, await Balance(sp, LedgerAccountType.Kasa));
        Assert.Equal(1, await Say(sp, db => db.Expenses));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E21_Tekil_gider_anahtarsiz_iki_gonderim_iki_gider()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        ExpenseInput Input() => new() { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit };

        await gid.CreateAsync(Input());
        await gid.CreateAsync(Input());
        Assert.Equal(200m, await Balance(sp, LedgerAccountType.Gider));
    }

    [Fact]
    public async Task E22_Toplu_gider_RowKey_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var batch = Guid.NewGuid();
        ExpenseInput[] Items() =>
        [
            new() { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m },
            new() { Tip = ExpenseType.Genel, NetTutar = 200m, KdvOrani = 0m }
        ];

        await gid.BatchCreateAsync(Items(), batch);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => gid.BatchCreateAsync(Items(), batch));

        Assert.Equal(500m, await Balance(sp, LedgerAccountType.Gider));       // ELLE: 300 + 200
        Assert.Equal(2, await Say(sp, db => db.Expenses));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E23_Gider_odemesi_KALANIN_TAMAMI_icin_de_ikinci_gonderim_sessiz_null()
    {
        // ÖNCE: kalanın tamamını kapatan ödemenin tekrarı "kalanı yok" (400) alıyordu. F1.4: sessiz null.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var supplier = await CreateCustomer(sp, "Tedarikci");
        var expenseId = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = PaymentMethod.AcikHesap, CariId = supplier });
        var k = Guid.NewGuid();

        Assert.NotNull(await gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, IslemAnahtari = k }));
        Assert.Null(await gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, IslemAnahtari = k }));

        var status = (await gid.PaymentStatusesAsync(await gid.ListAsync()))[expenseId];
        Assert.Equal(1200m, status.Odenen);                                  // ELLE: 1000 + %20, tek ödeme
        Assert.Equal(0m, status.Kalan);
        Assert.Equal(1, await Say(sp, db => db.Set<GiderOdeme>()));
    }

    [Fact]
    public async Task E23_Gider_odemesi_kismi_ikinci_gonderim_sessiz_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var supplier = await CreateCustomer(sp, "Tedarikci");
        var expenseId = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = PaymentMethod.AcikHesap, CariId = supplier });
        var k = Guid.NewGuid();

        Assert.NotNull(await gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 400m, IslemAnahtari = k }));
        Assert.Null(await gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = expenseId, Tutar = 400m, IslemAnahtari = k }));

        Assert.Equal(800m, (await gid.PaymentStatusesAsync(await gid.ListAsync()))[expenseId].Kalan); // ELLE: 1200 − 400
    }

    [Fact]
    public async Task E24_Gelen_efatura_ikinci_giderlestirme_ARTIK_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<IncomingEInvoiceService>();
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc), TimeSpan.Zero);
        var id = await svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = "IDEM-F14", GonderenVkn = "1234567890", GonderenUnvan = "Tedarikçi A.Ş.", Tarih = t,
            NetTutar = 1000m, KdvTutar = 200m, GenelToplam = 1200m, Currency = "TRY"
        });
        await svc.ApproveAsync(id);

        await svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            svc.ConvertToExpenseAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = PaymentMethod.Nakit }));

        Assert.Equal(-1200m, await Balance(sp, LedgerAccountType.Kasa));     // ELLE: tek çıkış 1200
        Assert.Equal(1, await Say(sp, db => db.Expenses));
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Ceza
    // =====================================================================================

    [Fact]
    public async Task E25_Ceza_yansitma_ikinci_istek_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var account = Guid.NewGuid();
        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = account, Tutar = 300m });

        Assert.True(await svc.ReflectAsync(id));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.ReflectAsync(id));
        Assert.Equal("Yalnız 'Yeni' durumundaki ceza yansıtılabilir.", ex.Message);
        Assert.Equal(300m, await Account(sp, account));
        Assert.Equal(2, await LedgerLine(sp, "Ceza"));
    }

    [Fact]
    public async Task E25_Ceza_yansitma_ESZAMANLI_kaybeden_ARTIK_400_sessiz_false_degil()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var account = Guid.NewGuid();
        var id = await sp.GetRequiredService<PenaltyService>().CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = account, Tutar = 300m });

        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<PenaltyService>().ReflectAsync(id));

        Assert.Single(result, r => r.Hata is null && r.Deger);
        Assert.Equal(typeof(ValidationException), Assert.Single(result, r => r.Hata is not null).Hata!.GetType());
        Assert.Equal(300m, await Account(sp, account));
        Assert.Equal(2, await LedgerLine(sp, "Ceza"));
    }

    private static async Task<(Guid Ceza, Guid Satir)> PenaltyItem(IServiceProvider sp, decimal amount)
    {
        var svc = sp.GetRequiredService<PenaltyService>();
        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Satirlar = [new PenaltySatirInput { Tutar = amount, Sebep = "Hız" }] });
        return (id, (await svc.ListLinesAsync(id)).Single().Id);
    }

    [Fact]
    public async Task E26_Ceza_odemesi_TAM_odemede_de_ikinci_gonderim_409()
    {
        // ÖNCE: kalemi kapatan ödemenin tekrarı "ödenecek bakiye yok" (400) alıyordu. F1.4: 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var (penalty, row) = await PenaltyItem(sp, 500m);
        var k = Guid.NewGuid();

        await svc.PayPartialAsync(penalty, new CezaOdemeInput { SatirId = row, IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() => svc.PayPartialAsync(penalty, new CezaOdemeInput { SatirId = row, IslemAnahtari = k }));

        Assert.Equal(-500m, await Balance(sp, LedgerAccountType.Kasa));      // ELLE: tek ödeme 500
        Assert.Equal(1, (await svc.ListPaymentsAsync(penalty)).Count);
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E26_Ceza_odemesi_kismi_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var (penalty, row) = await PenaltyItem(sp, 500m);
        var k = Guid.NewGuid();

        await svc.PayPartialAsync(penalty, new CezaOdemeInput { SatirId = row, Tutar = 200m, IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            svc.PayPartialAsync(penalty, new CezaOdemeInput { SatirId = row, Tutar = 200m, IslemAnahtari = k }));

        Assert.Equal(300m, (await svc.GetAsync(penalty))!.Kalan);             // ELLE: 500 − 200
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // MTV / Muayene / Sigorta
    // =====================================================================================

    private static async Task<(RegulationService Reg, Guid Arac)> Regulation(IServiceProvider sp, string plate)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        return (sp.GetRequiredService<RegulationService>(), v);
    }

    [Fact]
    public async Task E27_Mtv_anahtarli_TAM_odemede_de_ikinci_gonderim_409()
    {
        // ÖNCE: kaydı kapatan anahtarlı ödemenin tekrarı "MTV zaten ödendi." (400) alıyordu. F1.4: 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulation(sp, "34 ID 27");
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        var k = Guid.NewGuid();

        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { IslemAnahtari = k }));

        Assert.Equal(1000m, await Balance(sp, LedgerAccountType.Gider, v));  // ELLE: tek ödeme 1000
        Assert.Equal(2, await LedgerLine(sp, "MtvOdeme"));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E27_Mtv_anahtarsiz_tam_odeme_ikinci_istek_400_zaten_odendi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulation(sp, "34 ID 28");
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => reg.PayMtvAsync(mtv, LedgerAccountType.Kasa));
        Assert.Equal("MTV zaten ödendi.", ex.Message);
        Assert.Equal(1000m, await Balance(sp, LedgerAccountType.Gider, v));
    }

    [Fact]
    public async Task E27_Mtv_kismi_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulation(sp, "34 ID 29");
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        var k = Guid.NewGuid();

        await reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            reg.PayMtvAsync(mtv, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = k }));

        Assert.Equal(400m, await Balance(sp, LedgerAccountType.Gider, v));   // ELLE: tek 400
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E28_Muayene_anahtarli_TAM_odemede_de_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulation(sp, "34 ID 30");
        var insp = await reg.AddInspectionAsync(v, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 800m);
        var k = Guid.NewGuid();

        await reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            reg.PayInspectionAsync(insp, LedgerAccountType.Kasa, payment: new RegulasyonOdemeInput { IslemAnahtari = k }));

        Assert.Equal(800m, await Balance(sp, LedgerAccountType.Gider, v));   // ELLE: tek 800
        Assert.Equal(2, await LedgerLine(sp, "MuayeneOdeme"));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E29_Sigorta_ikinci_odeme_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulation(sp, "34 ID 31");
        var pol = await reg.AddInsuranceAsync(v, InsuranceType.Kasko,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1200m, "POL-IDEM", "Firma", null);

        await reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => reg.PayInsuranceAsync(pol, LedgerAccountType.Kasa));
        Assert.Equal("Sigorta zaten ödendi.", ex.Message);

        Assert.Equal(1200m, await Balance(sp, LedgerAccountType.Gider, v));  // ELLE: prim 1200 (tek)
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Servis yansıtma / HGS / Araç kredisi / Dış hizmet / Araç satış / Dönem kapanış
    // =====================================================================================

    [Fact]
    public async Task E30_Servis_yansitma_ikinci_istek_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 ID 32", Durum = VehicleStatus.Musait });
        var account = await CreateCustomer(sp, "Rucu");
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = v, Tip = ServiceType.Ariza, GirisKm = 0, HasarSorumlu = DamageResponsible.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Tampon", Tutar = 1000m }]
        });
        await svc.StartAsync(id);
        await svc.CompleteAsync(id, pickupKm: 100);

        await svc.ReflectAsync(id, account);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.ReflectAsync(id, account));
        Assert.Equal("Servis maliyeti zaten yansıtıldı.", ex.Message);
        Assert.Equal(500m, await Account(sp, account));                           // ELLE: 1000 × 0,5
    }

    private sealed class HgsFake(IReadOnlyList<TollCrossing> passages) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(string plate, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(passages);
    }

    [Fact]
    public async Task E31_Hgs_yansitma_ikinci_istek_sessiz_deterministik_anahtar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var hgs = new HgsReflectionService(new HgsFake([new TollCrossing(t, "Köprü", 100m)]),
            sp.GetRequiredService<ILedgerPoster>(), sp.GetRequiredService<IPeriodLockGuard>(), sp.GetRequiredService<ICurrentUser>());

        var r1 = await hgs.ReflectAsync(account, "34ID33", t, t.AddDays(1));
        var r2 = await hgs.ReflectAsync(account, "34ID33", t, t.AddDays(1));

        Assert.Equal(103m, r1.YansitilanTutar);
        Assert.Equal(103m, r2.YansitilanTutar);                              // sessiz: aynı sonuç, ikinci yazım yok
        Assert.Equal(103m, await Account(sp, account));                            // ELLE: 100 × 1,03 (tek)
        Assert.Equal(2, await LedgerLine(sp, "Hgs"));
    }

    [Fact]
    public async Task E32_Arac_kredi_taksiti_anahtarli_ikinci_gonderim_ARTIK_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<VehicleLoanService>();
        var loan = await svc.CreateAsync(new AracKrediInput { BankaAdi = "Banka", KrediTutari = 12000m, FaizOran = 0m, TaksitSayisi = 12 });
        var k = Guid.NewGuid();

        Assert.True(await svc.PayInstallmentAsync(loan, operationKey: k));
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() => svc.PayInstallmentAsync(loan, operationKey: k));
        Assert.Equal("Bu taksit ödemesi zaten kaydedilmiş (çift gönderim).", ex.Message);

        Assert.Equal(1, (await svc.GetAsync(loan))!.OdenenTaksit);
        Assert.Equal(1000m, await Balance(sp, LedgerAccountType.Gider));      // ELLE: 12000 / 12, faizsiz
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E32_Arac_kredi_SON_taksit_tekrari_da_409_sessiz_false_degil()
    {
        // ÖNCE: son taksidin anahtarlı tekrarı "tüm taksitler ödendi" çitine takılıp sessiz false
        // dönüyordu (ara taksitte kısıt reddi). F1.4: anahtar önce → 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<VehicleLoanService>();
        var loan = await svc.CreateAsync(new AracKrediInput { BankaAdi = "Banka", KrediTutari = 1000m, FaizOran = 0m, TaksitSayisi = 1 });
        var k = Guid.NewGuid();

        Assert.True(await svc.PayInstallmentAsync(loan, operationKey: k));
        await Assert.ThrowsAsync<DuplicateOperationException>(() => svc.PayInstallmentAsync(loan, operationKey: k));
        // Anahtarsız tekrar: bugünkü sözleşme (tüm taksitler ödendi → false, yazım yok).
        Assert.False(await svc.PayInstallmentAsync(loan));

        Assert.Equal(1000m, await Balance(sp, LedgerAccountType.Gider));      // ELLE: tek taksit 1000
        await BalanceCheckAsync(sp);
    }

    private static async Task<(Guid Kira, Guid Tedarikci)> SetupOutsourcedService(IServiceProvider sp, string plate)
    {
        var (rental, _, _) = await CreateRental(sp, plate, RentalStart, 3);
        var t = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Kurumsal, Unvan = "Tedarikçi AŞ" });
        return (rental, t);
    }

    [Fact]
    public async Task E33_Dis_hizmet_anahtarli_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<OutsourcedServiceService>();
        var (rental, supplier) = await SetupOutsourcedService(sp, "34 ID 34");
        var k = Guid.NewGuid();
        DisHizmetInput Input() => new()
        {
            RentalId = rental, FaturaKesilecekCariId = supplier, AlinanHizmet = "Transfer",
            HizmetBedeli = 1000m, TedarikciKomisyonOran = 10m, IslemAnahtari = k
        };

        await svc.CreateAsync(Input());
        await Assert.ThrowsAsync<DuplicateOperationException>(() => svc.CreateAsync(Input()));

        Assert.Equal(-900m, await Account(sp, supplier));                     // ELLE: alacak 1000 − komisyon 100
        Assert.Single(await svc.ListForRentalAsync(rental));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E34_Dis_hizmet_iptal_ikinci_istek_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<OutsourcedServiceService>();
        var (rental, supplier) = await SetupOutsourcedService(sp, "34 ID 35");
        var id = await svc.CreateAsync(new DisHizmetInput
        { RentalId = rental, FaturaKesilecekCariId = supplier, AlinanHizmet = "Transfer", HizmetBedeli = 1000m, TedarikciKomisyonOran = 10m });

        await svc.CancelAsync(id);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CancelAsync(id));
        Assert.Equal("Kayıt zaten iptal edilmiş.", ex.Message);

        Assert.Equal(0m, await Account(sp, supplier));                        // ELLE: −900 + 900
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E35_Arac_satis_ikinci_satis_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var sale = sp.GetRequiredService<VehicleSaleService>();
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 ID 36" });
        var recipient = await CreateCustomer(sp, "Alici");
        VehicleSaleInput Input() => new() { VehicleId = v, AliciCariId = recipient, SatisNet = 1000m, KdvOrani = 0.20m };

        await sale.CreateAsync(Input());
        var ex = await Assert.ThrowsAsync<ValidationException>(() => sale.CreateAsync(Input()));
        Assert.Equal("Araç zaten satılmış.", ex.Message);

        Assert.Equal(1200m, await Account(sp, recipient));                         // ELLE: 1000 + %20
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task E36_Donem_kapanis_ikinci_istek_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var businessDate = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var closing = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var account = await CreateCustomer(sp);
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = account, NetTutar = 1000m, KdvOrani = 0m, Tarih = businessDate });
        var svc = sp.GetRequiredService<PeriodClosingVoucherService>();

        await svc.CloseAsync(closing);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CloseAsync(closing));
        Assert.StartsWith("Dönem zaten 2026-06-30 tarihine kapalı.", ex.Message);

        Assert.Equal(0m, await Balance(sp, LedgerAccountType.Gelir));        // ELLE: gelir 1000 kapatıldı (tek fiş)
        Assert.Equal(-1000m, await Balance(sp, LedgerAccountType.DonemSonucu));
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Adversarial (F1.4 öz-denetim) — başlıktan türetilen anahtarın uçtan uca davranışı
    // =====================================================================================

    [Fact]
    public async Task A1_Iki_kullanici_ayni_baslik_degeri_CAKISMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        const string title = "ortak-istemci-anahtari-0001";
        var ali = Guid.NewGuid();
        var ayse = Guid.NewGuid();
        using var s1 = host.ScopeFor(tenant, ali);
        using var s2 = host.ScopeFor(tenant, ayse);
        var account = await CreateCustomer(s1.ServiceProvider);

        await s1.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = OperationKeyDeriver.Derive(tenant, ali, title) });
        await s2.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = OperationKeyDeriver.Derive(tenant, ayse, title) });

        Assert.Equal(-200m, await Account(s1.ServiceProvider, account));          // ELLE: iki MEŞRU tahsilat
    }

    [Fact]
    public async Task A2_Iki_kiraci_ayni_kullanici_id_ve_baslik_CAKISMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        const string title = "ortak-istemci-anahtari-0002";
        var user = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        using var s1 = host.ScopeFor(t1, user);
        using var s2 = host.ScopeFor(t2, user);
        var c1 = await CreateCustomer(s1.ServiceProvider);
        var c2 = await CreateCustomer(s2.ServiceProvider);

        // Manuel fatura: anahtar = faturanın PK'si (kiracı-GLOBAL). Ham başlık PK olsaydı ikinci kiracı
        // birincinin faturasına çarpardı; türetilmiş anahtar kiracıyı içerdiği için çarpmaz.
        var k1 = OperationKeyDeriver.Derive(t1, user, title);
        var k2 = OperationKeyDeriver.Derive(t2, user, title);
        Assert.NotEqual(k1, k2);
        Assert.Equal(k1, await s1.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c1, NetTutar = 100m, KdvOrani = 0m, IslemAnahtari = k1 }));
        Assert.Equal(k2, await s2.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c2, NetTutar = 100m, KdvOrani = 0m, IslemAnahtari = k2 }));

        Assert.Equal(100m, await Account(s1.ServiceProvider, c1));
        Assert.Equal(100m, await Account(s2.ServiceProvider, c2));
    }

    [Fact]
    public async Task A2b_Baska_kiracinin_fatura_idsi_anahtar_olarak_verilirse_sessiz_yutulmaz()
    {
        // Ham istemci değeri PK olsaydı (türetme atlanırsa) başka kiracının faturası "mevcut" sayılıp
        // gelir sessizce kaybolurdu. Yarış yolundaki yeni sessiz-başarı dalı bunu açmamalı: görünmeyen
        // Id → net red.
        using var host = new TestHost(fx.AppConnectionString);
        using var s1 = host.ScopeFor(Guid.NewGuid());
        using var s2 = host.ScopeFor(Guid.NewGuid());
        var c1 = await CreateCustomer(s1.ServiceProvider);
        var c2 = await CreateCustomer(s2.ServiceProvider);
        var foreign = await s1.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c1, NetTutar = 100m, KdvOrani = 0m });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s2.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c2, NetTutar = 500m, KdvOrani = 0m, IslemAnahtari = foreign }));
        Assert.Contains("başka bir kayıtla çakıştı", ex.Message);
        Assert.Equal(0m, await Account(s2.ServiceProvider, c2));
        Assert.Equal(100m, await Account(s1.ServiceProvider, c1));             // diğer kiracıya DOKUNULMADI
    }

    [Fact]
    public async Task A3_Ayni_baslik_farkli_islem_para_kaybi_yok()
    {
        // Sözleşme: istemci anahtarı her 2xx'ten sonra yeniler. Buna uymayan istemcide sonuç:
        //   * aynı tabloda/aynı kaynak türünde (tahsilat→ödeme aynı CashTransactions index'i) → 409;
        //   * farklı tablo/kaynak türünde (tahsilat → depozito al) → iki BAĞIMSIZ işlem, ikisi de yazılır;
        //   * AYNI işlem türünde FARKLI içerik (başka cari/tutar) → 409 "farklı içerikle" — sessiz başarı
        //     yalnız içerik birebir aynıysa verilir (M1-M7 testleri; adversarial MEDIUM-1 düzeltmesi).
        // Hiçbir durumda istek sessizce yutulup kullanıcıya "başarılı" denmez.
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant, user);
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        var k = OperationKeyDeriver.Derive(tenant, user, "yeniden-kullanilan-anahtar");

        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            cash.PayAsync(new CashInput { CariId = account, Tutar = 70m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k }));
        await sp.GetRequiredService<DepositService>().GetAsync(account, 50m, LedgerAccountType.Kasa, operationKey: k);

        Assert.Equal(-100m, await Account(sp, account));                          // ELLE: yalnız tahsilat
        Assert.Equal(50m, await sp.GetRequiredService<DepositService>().GetBalanceAsync(account));
        Assert.Equal(150m, await Balance(sp, LedgerAccountType.Kasa));        // ELLE: 100 + 50
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task A4_Deterministik_tahsilat_anahtari_baslik_anahtarini_ezer_iki_sekme_tek_tahsilat()
    {
        // İki sekme AYNI panel anlık görüntüsünü (kira, bakiye, işlem sayısı) gösteriyor ama her biri
        // kendi rastgele Idempotency-Key'ini yolluyor. Başlık kazansaydı iki tahsilat yazılırdı.
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant, user);
        var sp = scope.ServiceProvider;
        var (rental, customer, _) = await CreateRental(sp, "34 ID 40", RentalStart, 3);
        var cash = sp.GetRequiredService<CashService>();
        var panelKey = RentACar.Web.Finance.CollectionKey.Generate(rental, 300m, 0);

        var tab1 = OperationKeyDeriver.Select(panelKey, OperationKeyDeriver.Derive(tenant, user, "sekme-1-rastgele-anahtar"));
        var tab2 = OperationKeyDeriver.Select(panelKey, OperationKeyDeriver.Derive(tenant, user, "sekme-2-rastgele-anahtar"));
        Assert.Equal(panelKey, tab1);
        Assert.Equal(panelKey, tab2);

        await cash.CollectAsync(new CashInput { CariId = customer, RentalId = rental, Tutar = 300m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = tab1 });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            cash.CollectAsync(new CashInput { CariId = customer, RentalId = rental, Tutar = 300m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = tab2 }));

        Assert.Equal(-300m, await Account(sp, customer));                       // ELLE: tek tahsilat 300
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
    }

    [Fact]
    public async Task A5_Geri_alinan_islem_anahtari_REZERVE_ETMEZ()
    {
        // Başarısız (rollback) bir gönderim anahtarı "kullanılmış" bırakırsa düzeltilmiş tekrar
        // haksız yere mükerrer sayılırdı.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var cash = sp.GetRequiredService<CashService>();
        var account = await CreateCustomer(sp);
        var k = Guid.NewGuid();

        // (a) Depozito: tutulan yokken iade → bakiye çiti (kilidin arkasında) reddeder, hiçbir şey yazılmaz.
        await Assert.ThrowsAsync<ValidationException>(() => dep.RefundAsync(account, 200m, LedgerAccountType.Kasa, operationKey: k));
        await dep.GetAsync(account, 500m, LedgerAccountType.Kasa);
        await dep.RefundAsync(account, 200m, LedgerAccountType.Kasa, operationKey: k);   // AYNI anahtar → yazılır
        Assert.Equal(300m, await dep.GetBalanceAsync(account));                         // ELLE: 500 − 200

        // (b) Tek-cari kapatma: borç yokken → red; borç oluşunca AYNI anahtarla → yazılır.
        var k2 = Guid.NewGuid();
        var other = await CreateCustomer(sp, "Borclu");
        await cash.PayAsync(new CashInput { CariId = other, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        var item = (await cash.GetStatementAsync(other)).Satirlar.Single(x => x.Direction == LedgerDirection.Debit).Id;
        await Assert.ThrowsAsync<ValidationException>(() =>
            cash.CloseSingleAccountBulkAsync(other, new Dictionary<Guid, decimal?> { [item] = 150m }, LedgerAccountType.Kasa, operationKey: k2));
        Assert.Equal(100m, await cash.CloseSingleAccountBulkAsync(other, [item], LedgerAccountType.Kasa, operationKey: k2));
        Assert.Equal(0m, await Account(sp, other));
        await BalanceCheckAsync(sp);
    }

    // =====================================================================================
    // Adversarial MEDIUM-1 — AYNI anahtar, FARKLI içerik → 409 (asla sessiz başarı değil)
    // =====================================================================================

    [Fact]
    public async Task M1_Manuel_fatura_ayni_anahtar_baska_cari_ya_da_tutar_409_hic_bir_sey_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var a = await CreateCustomer(sp, "A");
        var b = await CreateCustomer(sp, "B");
        var k = Guid.NewGuid();

        Assert.Equal(k, await fat.CreateManualAsync(new ManualInvoiceInput { CariId = a, NetTutar = 1000m, KdvOrani = 0m, IslemAnahtari = k }));
        // P1: aynı anahtar, başka cari + başka tutar → önce A'nın id'si SESSİZCE dönüyordu, B 0 kalıyordu.
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            fat.CreateManualAsync(new ManualInvoiceInput { CariId = b, NetTutar = 5000m, KdvOrani = 0m, IslemAnahtari = k }));
        Assert.Equal(DuplicateOperationException.DifferentContentMessage, ex.Message);
        // Aynı cari, başka tutar → 409.
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            fat.CreateManualAsync(new ManualInvoiceInput { CariId = a, NetTutar = 2000m, KdvOrani = 0m, IslemAnahtari = k }));
        // Birebir aynı tekrar → sessiz, aynı id.
        Assert.Equal(k, await fat.CreateManualAsync(new ManualInvoiceInput { CariId = a, NetTutar = 1000m, KdvOrani = 0m, IslemAnahtari = k }));

        Assert.Equal(1000m, await Account(sp, a));                             // ELLE: tek fatura 1000 (KDV 0)
        Assert.Equal(0m, await Account(sp, b));                                // B'ye hiçbir şey yazılmadı
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task M1_Manuel_fatura_ayni_anahtar_farkli_cari_ESZAMANLI_biri_yazilir_digeri_409()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var a = await CreateCustomer(sp, "A");
        var b = await CreateCustomer(sp, "B");
        var k = Guid.NewGuid();
        var order = 0;

        // Yarış yolu (PK_Invoices dalı) da içerik karşılaştırır.
        var result = await TwoConcurrent(host, tenant, s => s.GetRequiredService<InvoiceService>().CreateManualAsync(
            Interlocked.Increment(ref order) == 1
                ? new ManualInvoiceInput { CariId = a, NetTutar = 1000m, KdvOrani = 0m, IslemAnahtari = k }
                : new ManualInvoiceInput { CariId = b, NetTutar = 5000m, KdvOrani = 0m, IslemAnahtari = k }));

        Assert.Single(result, r => r.Hata is null);
        var error = Assert.IsType<DuplicateOperationException>(Assert.Single(result, r => r.Hata is not null).Hata);
        Assert.Equal(DuplicateOperationException.DifferentContentMessage, error.Message);
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        // ELLE: kazanan hangisiyse yalnız onun tutarı yazıldı (A 1000 ya da B 5000), diğeri 0.
        var (ba, bb) = (await Account(sp, a), await Account(sp, b));
        Assert.True((ba == 1000m && bb == 0m) || (ba == 0m && bb == 5000m), $"A={ba} B={bb}");
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task M2_Depozito_iade_ayni_anahtar_baska_cari_ve_tutar_409_hic_bir_sey_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var a = await CreateCustomer(sp, "A");
        var b = await CreateCustomer(sp, "B");
        await dep.GetAsync(a, 500m, LedgerAccountType.Kasa);
        await dep.GetAsync(b, 100m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.RefundAsync(a, 200m, LedgerAccountType.Kasa, operationKey: k);
        // P3: önce k döndürüp HİÇBİR ŞEY yazmıyordu (F1.4 öncesi bakiye çitinden 400'dü).
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.RefundAsync(b, 900m, LedgerAccountType.Kasa, operationKey: k));
        Assert.Equal(DuplicateOperationException.DifferentContentMessage, ex.Message);
        // Aynı cari, başka tutar / başka hesap → 409.
        await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.RefundAsync(a, 250m, LedgerAccountType.Kasa, operationKey: k));
        await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.RefundAsync(a, 200m, LedgerAccountType.Banka, operationKey: k));
        // Birebir aynı tekrar → sessiz.
        Assert.Equal(k, await dep.RefundAsync(a, 200m, LedgerAccountType.Kasa, operationKey: k));

        Assert.Equal(300m, await dep.GetBalanceAsync(a));                    // ELLE: 500 − 200 (tek)
        Assert.Equal(100m, await dep.GetBalanceAsync(b));                    // ELLE: dokunulmadı
        Assert.Equal(400m, await Balance(sp, LedgerAccountType.Kasa));       // ELLE: 500 + 100 − 200
        Assert.Equal(0m, await Balance(sp, LedgerAccountType.Banka));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task M2_Depozito_ayni_anahtar_farkli_cari_ESZAMANLI_biri_yazilir_digeri_409()
    {
        // Kilit cari başına: farklı carilerin istekleri serileşmez → catch yolu da içerik karşılaştırmalı.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var a = await CreateCustomer(sp, "A");
        var b = await CreateCustomer(sp, "B");
        await dep.GetAsync(a, 500m, LedgerAccountType.Kasa);
        await dep.GetAsync(b, 100m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();
        var order = 0;

        var result = await TwoConcurrent(host, tenant, s => Interlocked.Increment(ref order) == 1
            ? s.GetRequiredService<DepositService>().RefundAsync(a, 200m, LedgerAccountType.Kasa, operationKey: k)
            : s.GetRequiredService<DepositService>().RefundAsync(b, 50m, LedgerAccountType.Kasa, operationKey: k));

        Assert.Single(result, r => r.Hata is null);
        Assert.IsType<DuplicateOperationException>(Assert.Single(result, r => r.Hata is not null).Hata);
        // ELLE: ya A 500→300 (B 100) ya B 100→50 (A 500).
        var (da, db2) = (await dep.GetBalanceAsync(a), await dep.GetBalanceAsync(b));
        Assert.True((da == 300m && db2 == 100m) || (da == 500m && db2 == 50m), $"A={da} B={db2}");
        Assert.Equal(2, await LedgerLine(sp, "DepozitoIade"));
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task M3_Gider_odemesi_ayni_anahtar_baska_gider_ya_da_tutar_409_hic_bir_sey_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var ted = await CreateCustomer(sp, "Tedarikci");
        var x = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = PaymentMethod.AcikHesap, CariId = ted });
        var y = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.AcikHesap, CariId = ted });
        var k = Guid.NewGuid();

        Assert.NotNull(await gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = x, Tutar = 400m, IslemAnahtari = k }));
        // P4: başka gider + başka tutar → önce sessiz null (hiçbir şey yazılmıyordu).
        var ex = await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = y, Tutar = 999m, IslemAnahtari = k }));
        Assert.Equal(DuplicateOperationException.DifferentContentMessage, ex.Message);
        // Aynı gider, başka tutar → 409; "kalanın tamamı" (null) ama ilk ödeme kapatmamıştı → 409.
        await Assert.ThrowsAsync<DuplicateOperationException>(() => gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = x, Tutar = 500m, IslemAnahtari = k }));
        await Assert.ThrowsAsync<DuplicateOperationException>(() => gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = x, IslemAnahtari = k }));
        // Birebir aynı tekrar → sessiz null.
        Assert.Null(await gid.AddPaymentAsync(new GiderOdemeInput { ExpenseId = x, Tutar = 400m, IslemAnahtari = k }));

        var status = await gid.PaymentStatusesAsync(await gid.ListAsync());
        Assert.Equal(800m, status[x].Kalan);                                 // ELLE: 1200 − 400 (tek)
        Assert.Equal(300m, status[y].Kalan);                                 // ELLE: dokunulmadı
        Assert.Equal(1, await Say(sp, db => db.Set<GiderOdeme>()));
    }

    [Fact]
    public async Task M3_Gider_odemesi_ayni_anahtar_farkli_gider_ESZAMANLI_biri_yazilir_digeri_409()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var ted = await CreateCustomer(sp, "Tedarikci");
        var x = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.AcikHesap, CariId = ted });
        var y = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.AcikHesap, CariId = ted });
        var k = Guid.NewGuid();
        var order = 0;

        var result = await TwoConcurrent(host, tenant, s => Interlocked.Increment(ref order) == 1
            ? s.GetRequiredService<ExpenseService>().AddPaymentAsync(new GiderOdemeInput { ExpenseId = x, Tutar = 400m, IslemAnahtari = k })
            : s.GetRequiredService<ExpenseService>().AddPaymentAsync(new GiderOdemeInput { ExpenseId = y, Tutar = 100m, IslemAnahtari = k }));

        Assert.Single(result, r => r.Hata is null && r.Deger is not null);
        Assert.IsType<DuplicateOperationException>(Assert.Single(result, r => r.Hata is not null).Hata);
        Assert.Equal(1, await Say(sp, db => db.Set<GiderOdeme>()));
    }

    [Fact]
    public async Task M4_Virman_cari_virman_bakiye_duzeltme_ayni_anahtar_farkli_icerik_409()
    {
        // LedgerPoster eskiden HER unique ihlalini sessizce yutuyordu.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var duz = sp.GetRequiredService<BalanceAdjustmentService>();
        var a = await CreateCustomer(sp, "A");
        var b = await CreateCustomer(sp, "B");
        var c = await CreateCustomer(sp, "C");

        var kv = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m, operationKey: kv);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 700m, operationKey: kv));
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Kasa, 500m, operationKey: kv));
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m, operationKey: kv); // aynı → sessiz

        var kc = Guid.NewGuid();
        await cash.TransferBetweenAccountsAsync(a, b, 300m, operationKey: kc);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.TransferBetweenAccountsAsync(a, c, 300m, operationKey: kc));
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.TransferBetweenAccountsAsync(a, b, 301m, operationKey: kc));

        var kd = Guid.NewGuid();
        await duz.AdjustAsync(new BakiyeDuzeltmeInput { CariId = c, Tutar = 150m, Yon = BalanceAdjustmentDirection.Borclandir, IslemAnahtari = kd });
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            duz.AdjustAsync(new BakiyeDuzeltmeInput { CariId = a, Tutar = 150m, Yon = BalanceAdjustmentDirection.Borclandir, IslemAnahtari = kd }));
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            duz.AdjustAsync(new BakiyeDuzeltmeInput { CariId = c, Tutar = 150m, Yon = BalanceAdjustmentDirection.Alacaklandir, IslemAnahtari = kd }));

        Assert.Equal(-500m, await Balance(sp, LedgerAccountType.Kasa));      // ELLE: tek virman 500
        Assert.Equal(500m, await Balance(sp, LedgerAccountType.Banka));
        Assert.Equal(-300m, await Account(sp, a));                              // ELLE: yalnız a→b 300
        Assert.Equal(300m, await Account(sp, b));
        Assert.Equal(150m, await Account(sp, c));                               // ELLE: yalnız düzeltme 150
        await BalanceCheckAsync(sp);
    }

    [Fact]
    public async Task M5_Hgs_ayni_donem_farkli_tutar_409_sessiz_degil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var account = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        HgsReflectionService Hgs(decimal amount) => new(new HgsFake([new TollCrossing(t, "Köprü", amount)]),
            sp.GetRequiredService<ILedgerPoster>(), sp.GetRequiredService<IPeriodLockGuard>(), sp.GetRequiredService<ICurrentUser>());

        await Hgs(100m).ReflectAsync(account, "34ID50", t, t.AddDays(1));
        // Aynı (cari, plaka, dönem) → aynı deterministik anahtar; geçiş tutarı değişmişse eskiden sessizce
        // yutuluyordu (fark hiç borçlandırılmıyordu, sonuç "yansıtıldı 206" diyordu).
        await Assert.ThrowsAsync<DuplicateOperationException>(() => Hgs(200m).ReflectAsync(account, "34ID50", t, t.AddDays(1)));
        await Hgs(100m).ReflectAsync(account, "34ID50", t, t.AddDays(1));   // aynı → sessiz

        Assert.Equal(103m, await Account(sp, account));                          // ELLE: 100 × 1,03 (tek)
    }

    [Fact]
    public async Task M6_Depozito_irat_ayni_anahtar_baska_kira_atfi_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepositService>();
        var (rental, customer, _) = await CreateRental(sp, "34 ID 51", RentalStart, 3);
        await dep.GetAsync(customer, 400m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.ForfeitAsync(customer, 100m, operationKey: k);
        // Aynı cari/tutar ama gelir başka araca (kiraya) atfediliyor → farklı işlem → 409.
        await Assert.ThrowsAsync<DuplicateOperationException>(() => dep.ForfeitAsync(customer, 100m, rentalId: rental, operationKey: k));
        Assert.Equal(k, await dep.ForfeitAsync(customer, 100m, operationKey: k));   // aynı → sessiz

        Assert.Equal(300m, await dep.GetBalanceAsync(customer));               // ELLE: 400 − 100
        Assert.Equal(1, await Say(sp, db => db.DepozitoIratlar));
    }

    [Fact]
    public async Task M7_Donem_faturasi_acik_farkli_KDV_orani_ile_tekrar_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (rental, customer, _) = await CreateRental(sp, "34 ID 52", RentalStart, 90);

        var f1 = await fat.CreatePeriodInvoiceAsync(rental, 1, vatRate: 0.20m);
        await Assert.ThrowsAsync<DuplicateOperationException>(() => fat.CreatePeriodInvoiceAsync(rental, 1, vatRate: 0.10m));
        Assert.Equal(f1, await fat.CreatePeriodInvoiceAsync(rental, 1, vatRate: 0.20m));   // aynı oran → sessiz
        Assert.Equal(f1, await fat.CreatePeriodInvoiceAsync(rental, 1));                    // oran verilmedi → sessiz

        Assert.Equal(3100m, await Account(sp, customer));                        // ELLE: D1 31 × 100 (tek)
        Assert.Equal(1, await Say(sp, db => db.Invoices));
    }
}
