using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-29 ADVERSARIAL KALICI KİLİTLER — bu testlerin her biri bir kez GERÇEKTEN kırıktı.
/// Adversarial inceleme sırasında bulguyu kanıtlayan probe olarak yazıldılar; düzeltmelerden
/// sonra yeşile döndüler ve regresyon kilidi olarak BIRAKILDILAR (silinmediler).
///
/// <para>H1: aynı borç kalemi, cari'nin BAŞKA açık borcu varken defalarca kapatılabiliyordu —
/// bakiye çiti tek başına yetmiyordu, kalem-bazlı tahsis kaydı (KapatmaTahsis) eklendi.
/// H2: bakiye kontrolü ile kayıt arasında kilit yoktu; 8 eşzamanlı kapatma bakiyeyi −7000 yaptı —
/// (tenant, cari) danışma kilidi + aynı transaction. M1/M2: FK ihlalleri 500 veriyordu → temiz red.
/// M3: dövizli kalemde iki taraf ayrı ayrı yukarı yuvarlanınca bakiye eksiye düşüyordu → aşağı
/// yuvarlama. Son iki test çürütülemeyen iddiaların kilidi (dönem kilidi, ters kayıt).</para>
/// </summary>
[Collection("postgres")]
public sealed class Faz29AdversarialKilitTests(PostgresFixture fx)
{
    private static async Task<Guid> CustomerAsync(IServiceProvider sp, string name = "Probe") =>
        await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Cari" });

    /// <summary>Cariye borç yazar (Borç Cari / Alacak Kasa).</summary>
    private static Task DebitAsync(IServiceProvider sp, Guid customerId, decimal amount,
        string description, string currency = "TRY", decimal? exchangeRate = 1m)
        => sp.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = customerId, Tutar = amount, Doviz = currency, Kur = exchangeRate, Hesap = LedgerAccountType.Kasa, Aciklama = description });

    private static async Task<List<Guid>> DebitLinesAsync(IServiceProvider sp, Guid customerId, decimal? amount = null)
        => (await sp.GetRequiredService<CashService>().GetStatementAsync(customerId)).Satirlar
            .Where(x => x.Direction == LedgerDirection.Debit
                        && (amount is null || x.Amount.Amount == amount))
            .Select(x => x.Id).ToList();

    private static async Task<(bool ok, Exception? ex)> Wrap(Task t)
    {
        try { await t; return (true, null); }
        catch (Exception ex) { return (false, ex); }
    }

    private static async Task Exec(NpgsqlConnection c, NpgsqlTransaction? tx, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Bu DB'de kilit bekleyen oturum sayısı (racar_app kendi oturumlarını görür).</summary>
    private static async Task<int> PendingCountAsync(string conn)
    {
        await using var c = new NpgsqlConnection(conn);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pg_stat_activity " +
                          "WHERE datname = current_database() AND wait_event_type = 'Lock'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ------------------------------------------------------------------------------------
    // BULGU-1 (iddia 5 "ÇİFT KAPATMA ÇİTİ"): çit YALNIZCA seçim == TÜM bakiye olduğunda tutar.
    // Cari'nin BAŞKA açık borcu varsa AYNI kalem defalarca "kapatılabilir" → alacak sessizce erir.
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task H1_ayni_kalem_baska_borc_varken_IKINCI_kez_kapatilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp);

        // ELLE: iki borç kalemi — 100 ve 900 → bakiye 1000.
        await DebitAsync(sp, account, 100m, "K1");
        await DebitAsync(sp, account, 900m, "K2");
        Assert.Equal(1000m, await cash.GetAccountBalanceAsync(account));

        var k1 = await DebitLinesAsync(sp, account, 100m);
        Assert.Single(k1);

        // 1. kapatma: 100 tahsil → bakiye 900 (ELLE).
        Assert.Equal(100m, await cash.CloseSingleAccountBulkAsync(account, k1, LedgerAccountType.Kasa));
        Assert.Equal(900m, await cash.GetAccountBalanceAsync(account));

        // 2. kapatma AYNI KALEM (yeni idempotency token'ı = ayrı form render'ı, gerçek senaryo:
        // kalem listede hâlâ "borç" göründüğü için ikinci operatör yeniden işaretler).
        var second = await Wrap(cash.CloseSingleAccountBulkAsync(account, k1, LedgerAccountType.Kasa));
        var last = await cash.GetAccountBalanceAsync(account);

        // İDDİA: gürültülü red → bakiye 900'de kalmalı. GERÇEK: kabul → 100'lük kalem İKİ kez
        // tahsil edildi, bakiye 800'e düştü (100 TL alacak sessizce silindi).
        Assert.True(!second.ok && last == 900m,
            $"BULGU-1: aynı kalem ikinci kez kapatıldı (ikinci çağrı ok={second.ok}); bakiye {last} (beklenen 900).");
    }

    // ------------------------------------------------------------------------------------
    // BULGU-2 (iddia 5 + c TOCTOU): bakiye okuma ile post arasında kilit YOK
    // (karş. PostDepozitoIslemAsync → DepozitoKilitAsync danışma kilidi).
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task H2_eszamanli_kapatma_bakiye_citini_asamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid account;
        List<Guid> selected;
        using (var s0 = host.ScopeFor(tenant))
        {
            account = await CustomerAsync(s0.ServiceProvider, "Yaris");
            await DebitAsync(s0.ServiceProvider, account, 1000m, "Tek borç");
            selected = await DebitLinesAsync(s0.ServiceProvider, account);
        }

        // DETERMİNİSTİK YARIŞ: "aynı anda çalıştır ve umut et" flaky'dir. Bunun yerine No tahsis
        // satırını (TenantSequences/CashNo) DIŞARIDAN kilitleriz → her oturum bakiye çitini GEÇİP
        // insert'te bloke olur. Hepsi bloke olunca kilidi bırakırız: çitin post ile arasında
        // hiçbir koruma olmadığı ampirik olarak sabitlenir.
        const int N = 8;
        await using var blocker = new NpgsqlConnection(fx.AppConnectionString);
        await blocker.OpenAsync();
        await Exec(blocker, null, $"SELECT set_config('app.tenant_id','{tenant}',false)");
        await using var btx = await blocker.BeginTransactionAsync();
        await Exec(blocker, btx,
            "INSERT INTO \"TenantSequences\" (\"TenantId\",\"Name\",\"NextValue\") " +
            $"VALUES ('{tenant}','CashNo',1) ON CONFLICT (\"TenantId\",\"Name\") " +
            "DO UPDATE SET \"NextValue\" = \"TenantSequences\".\"NextValue\" + 1");

        var scopes = Enumerable.Range(0, N).Select(_ => host.ScopeFor(tenant)).ToList();
        var jobs = scopes.Select(s => Wrap(Task.Run(() => s.ServiceProvider
            .GetRequiredService<CashService>()
            .CloseSingleAccountBulkAsync(account, selected, LedgerAccountType.Kasa)))).ToList();

        // Hepsi No-kilidinde bekleyene kadar bekle (bakiye çitini çoktan geçtiler).
        var limit = DateTime.UtcNow.AddSeconds(30);
        while (await PendingCountAsync(fx.AppConnectionString) < N && DateTime.UtcNow < limit)
            await Task.Delay(100);
        await btx.CommitAsync();

        var result = await Task.WhenAll(jobs);
        foreach (var s in scopes) s.Dispose();

        using var last = host.ScopeFor(tenant);
        var balance = await last.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(account);

        // ELLE: 1000 borç, 1000'lik tek kalem → EN FAZLA bir kapatma geçmeli; bakiye 0'ın altına inemez.
        Assert.True(result.Count(r => r.ok) == 1 && balance == 0m,
            $"BULGU-2: {result.Count(r => r.ok)} kapatma geçti, bakiye {balance} (beklenen 1 / 0).");
    }

    // ------------------------------------------------------------------------------------
    // BULGU-3 (i/j): var olmayan (ya da başka tenant'ın) FinansalHesapId → ValidationException DEĞİL
    // ham DbUpdateException. Web ucu yalnız ValidationException yakalıyor → 500.
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task M1_gecersiz_finansal_hesap_TEMIZ_red_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid otherTenantAccount;
        using (var s0 = host.ScopeFor(Guid.NewGuid()))
            otherTenantAccount = await s0.ServiceProvider.GetRequiredService<FinancialAccountService>()
                .CreateAsync(new FinancialAccountInput { Kod = "X-1", Ad = "Başka tenant hesabı", Tur = "Banka" });

        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var fabricated = await Wrap(svc.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, FinansalHesapId = Guid.NewGuid() }
        ]));
        var crossTenant = await Wrap(svc.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, FinansalHesapId = otherTenantAccount }
        ]));

        Assert.True(fabricated.ex is ValidationException,
            $"BULGU-3a: uydurma hesap id → {fabricated.ex?.GetType().Name ?? "BAŞARILI(!)"} (ValidationException bekleniyordu).");
        Assert.True(crossTenant.ex is ValidationException,
            $"BULGU-3b: çapraz-tenant hesap id → {crossTenant.ex?.GetType().Name ?? "BAŞARILI(!)"} (ValidationException bekleniyordu).");
    }

    // ------------------------------------------------------------------------------------
    // BULGU-4 (l/k): yeni FK (Restrict) → kullanılan hesabın silinmesi artık ham DB hatası
    // fırlatıyor; /hesaplar/delete ucu yalnız ValidationException yakalıyor → 500.
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task M2_kullanilan_hesabin_silinmesi_TEMIZ_red_verir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var accounts = sp.GetRequiredService<FinancialAccountService>();
        var account = await accounts.CreateAsync(new FinancialAccountInput { Kod = "ZR", Ad = "Ziraat", Tur = "Banka" });

        await sp.GetRequiredService<ExpenseService>().BatchCreateAsync(
        [
            // FAZ-50: hesap türü ile ödeme yöntemi ARTIK çelişemez (banka hesabı → Banka ödeme).
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m,
                OdemeYontemi = PaymentMethod.Banka, FinansalHesapId = account }
        ]);

        var remove = await Wrap(accounts.DeleteAsync(account));
        Assert.True(remove.ex is ValidationException,
            $"BULGU-4: kullanılan hesabı silme → {remove.ex?.GetType().Name ?? "BAŞARILI(!)"} (temiz red bekleniyordu).");
    }

    // ------------------------------------------------------------------------------------
    // (d) YUVARLAMA: 2 haneye yuvarlanmış toplam gerçek bakiyeyi aşabilir → bakiye eksiye düşer.
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task M3_dovizli_kapatma_yuvarlama_kalintisi_birakmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp, "Dovizli");

        // ELLE: 100 EUR × 35,123456 = 3.512,3456 baz borç.
        await DebitAsync(sp, account, 100m, "EUR borç", "EUR", 35.123456m);
        Assert.Equal(3512.3456m, await cash.GetAccountBalanceAsync(account));

        var selected = await DebitLinesAsync(sp, account);
        var collect = await cash.CloseSingleAccountBulkAsync(account, selected, LedgerAccountType.Kasa);
        var balance = await cash.GetAccountBalanceAsync(account);

        Assert.True(balance >= 0m,
            $"BULGU-5: kapatma sonrası bakiye {balance} (< 0 → fazla tahsilat). Tahsil edilen {collect}.");
    }

    // ------------------------------------------------------------------------------------
    // (f) KONTROL — dönem kilidi + gelecek tarih. Bulgu BEKLENMİYOR (savunma doğrulaması).
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task Kilit_donem_kilidi_ve_gelecek_tarih_reddediliyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp, "Kilit");
        var history = DateTimeOffset.UtcNow.AddDays(-30);
        await sp.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = account, Tutar = 500m, Hesap = LedgerAccountType.Kasa, Tarih = history, Aciklama = "Eski borç" });
        var selected = await DebitLinesAsync(sp, account);

        await sp.GetRequiredService<RentACar.Application.Periods.PeriodLockService>()
            .LockAsync(DateTimeOffset.UtcNow.AddDays(-1));

        // Kapalı döneme geri-tarihli kapatma → red.
        await Assert.ThrowsAsync<ValidationException>(() => cash.CloseSingleAccountBulkAsync(
            account, selected, LedgerAccountType.Kasa, date: history));
        // Gelecek tarih → red (TarihPolitikasi).
        await Assert.ThrowsAsync<ValidationException>(() => cash.CloseSingleAccountBulkAsync(
            account, selected, LedgerAccountType.Kasa, date: DateTimeOffset.UtcNow.AddDays(5)));

        Assert.Equal(500m, await cash.GetAccountBalanceAsync(account));
    }

    // ------------------------------------------------------------------------------------
    // (e) KONTROL — kapatma tahsilatının ters kaydı sonrası bakiye/çit tutarlı mı?
    // ------------------------------------------------------------------------------------
    [Fact]
    public async Task Kilit_ters_kayit_sonrasi_bakiye_ve_defter_tutarli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cash = sp.GetRequiredService<CashService>();
        var account = await CustomerAsync(sp, "Ters");

        await DebitAsync(sp, account, 500m, "Borç");
        var selected = await DebitLinesAsync(sp, account);
        Assert.Equal(500m, await cash.CloseSingleAccountBulkAsync(account, selected, LedgerAccountType.Kasa));
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(account));

        // Kapatma tahsilatını bul (TH- ile başlayan, ters olmayan son kayıt) ve ters çevir.
        var tx = (await cash.ListAsync()).First(t => t.Tip == CashTransactionType.Tahsilat && !t.TersKayitMi);
        await cash.ReverseAsync(tx.Id);
        Assert.Equal(500m, await cash.GetAccountBalanceAsync(account)); // borç geri geldi

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        Assert.Equal(rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase),
                     rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase));

        // Ters kaydın BORÇ satırı ekstrede seçilebilir hale geliyor; toplam bakiyeyi aşamamalı.
        var all = await DebitLinesAsync(sp, account);
        Assert.Equal(2, all.Count); // orijinal borç + ters kayıt borcu
        await Assert.ThrowsAsync<ValidationException>(
            () => cash.CloseSingleAccountBulkAsync(account, all, LedgerAccountType.Kasa)); // 1000 > 500
    }
}
