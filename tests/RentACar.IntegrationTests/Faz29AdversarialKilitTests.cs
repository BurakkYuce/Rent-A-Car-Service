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
    private static async Task<Guid> CariAsync(IServiceProvider sp, string ad = "Probe") =>
        await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Cari" });

    /// <summary>Cariye borç yazar (Borç Cari / Alacak Kasa).</summary>
    private static Task BorclandirAsync(IServiceProvider sp, Guid cariId, decimal tutar,
        string aciklama, string doviz = "TRY", decimal? kur = 1m)
        => sp.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = cariId, Tutar = tutar, Doviz = doviz, Kur = kur, Hesap = LedgerAccountType.Kasa, Aciklama = aciklama });

    private static async Task<List<Guid>> BorcSatirlariAsync(IServiceProvider sp, Guid cariId, decimal? tutar = null)
        => (await sp.GetRequiredService<CashService>().GetStatementAsync(cariId)).Satirlar
            .Where(x => x.Direction == LedgerDirection.Debit
                        && (tutar is null || x.Amount.Amount == tutar))
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
    private static async Task<int> BekleyenSayisiAsync(string conn)
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
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariAsync(sp);

        // ELLE: iki borç kalemi — 100 ve 900 → bakiye 1000.
        await BorclandirAsync(sp, cari, 100m, "K1");
        await BorclandirAsync(sp, cari, 900m, "K2");
        Assert.Equal(1000m, await kasa.GetCariBalanceAsync(cari));

        var k1 = await BorcSatirlariAsync(sp, cari, 100m);
        Assert.Single(k1);

        // 1. kapatma: 100 tahsil → bakiye 900 (ELLE).
        Assert.Equal(100m, await kasa.TekCariTopluKapatAsync(cari, k1, LedgerAccountType.Kasa));
        Assert.Equal(900m, await kasa.GetCariBalanceAsync(cari));

        // 2. kapatma AYNI KALEM (yeni idempotency token'ı = ayrı form render'ı, gerçek senaryo:
        // kalem listede hâlâ "borç" göründüğü için ikinci operatör yeniden işaretler).
        var ikinci = await Wrap(kasa.TekCariTopluKapatAsync(cari, k1, LedgerAccountType.Kasa));
        var son = await kasa.GetCariBalanceAsync(cari);

        // İDDİA: gürültülü red → bakiye 900'de kalmalı. GERÇEK: kabul → 100'lük kalem İKİ kez
        // tahsil edildi, bakiye 800'e düştü (100 TL alacak sessizce silindi).
        Assert.True(!ikinci.ok && son == 900m,
            $"BULGU-1: aynı kalem ikinci kez kapatıldı (ikinci çağrı ok={ikinci.ok}); bakiye {son} (beklenen 900).");
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
        Guid cari;
        List<Guid> secilen;
        using (var s0 = host.ScopeFor(tenant))
        {
            cari = await CariAsync(s0.ServiceProvider, "Yaris");
            await BorclandirAsync(s0.ServiceProvider, cari, 1000m, "Tek borç");
            secilen = await BorcSatirlariAsync(s0.ServiceProvider, cari);
        }

        // DETERMİNİSTİK YARIŞ: "aynı anda çalıştır ve umut et" flaky'dir. Bunun yerine No tahsis
        // satırını (TenantSequences/CashNo) DIŞARIDAN kilitleriz → her oturum bakiye çitini GEÇİP
        // insert'te bloke olur. Hepsi bloke olunca kilidi bırakırız: çitin post ile arasında
        // hiçbir koruma olmadığı ampirik olarak sabitlenir.
        const int N = 8;
        await using var blokci = new NpgsqlConnection(fx.AppConnectionString);
        await blokci.OpenAsync();
        await Exec(blokci, null, $"SELECT set_config('app.tenant_id','{tenant}',false)");
        await using var btx = await blokci.BeginTransactionAsync();
        await Exec(blokci, btx,
            "INSERT INTO \"TenantSequences\" (\"TenantId\",\"Name\",\"NextValue\") " +
            $"VALUES ('{tenant}','CashNo',1) ON CONFLICT (\"TenantId\",\"Name\") " +
            "DO UPDATE SET \"NextValue\" = \"TenantSequences\".\"NextValue\" + 1");

        var scopes = Enumerable.Range(0, N).Select(_ => host.ScopeFor(tenant)).ToList();
        var isler = scopes.Select(s => Wrap(Task.Run(() => s.ServiceProvider
            .GetRequiredService<CashService>()
            .TekCariTopluKapatAsync(cari, secilen, LedgerAccountType.Kasa)))).ToList();

        // Hepsi No-kilidinde bekleyene kadar bekle (bakiye çitini çoktan geçtiler).
        var sinir = DateTime.UtcNow.AddSeconds(30);
        while (await BekleyenSayisiAsync(fx.AppConnectionString) < N && DateTime.UtcNow < sinir)
            await Task.Delay(100);
        await btx.CommitAsync();

        var sonuc = await Task.WhenAll(isler);
        foreach (var s in scopes) s.Dispose();

        using var son = host.ScopeFor(tenant);
        var bakiye = await son.ServiceProvider.GetRequiredService<CashService>().GetCariBalanceAsync(cari);

        // ELLE: 1000 borç, 1000'lik tek kalem → EN FAZLA bir kapatma geçmeli; bakiye 0'ın altına inemez.
        Assert.True(sonuc.Count(r => r.ok) == 1 && bakiye == 0m,
            $"BULGU-2: {sonuc.Count(r => r.ok)} kapatma geçti, bakiye {bakiye} (beklenen 1 / 0).");
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
        Guid baskaTenantHesap;
        using (var s0 = host.ScopeFor(Guid.NewGuid()))
            baskaTenantHesap = await s0.ServiceProvider.GetRequiredService<FinancialAccountService>()
                .CreateAsync(new FinancialAccountInput { Kod = "X-1", Ad = "Başka tenant hesabı" });

        using var scope = host.ScopeFor(tenant);
        var svc = scope.ServiceProvider.GetRequiredService<ExpenseService>();

        var uydurma = await Wrap(svc.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, FinansalHesapId = Guid.NewGuid() }
        ]));
        var caprazTenant = await Wrap(svc.BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, FinansalHesapId = baskaTenantHesap }
        ]));

        Assert.True(uydurma.ex is ValidationException,
            $"BULGU-3a: uydurma hesap id → {uydurma.ex?.GetType().Name ?? "BAŞARILI(!)"} (ValidationException bekleniyordu).");
        Assert.True(caprazTenant.ex is ValidationException,
            $"BULGU-3b: çapraz-tenant hesap id → {caprazTenant.ex?.GetType().Name ?? "BAŞARILI(!)"} (ValidationException bekleniyordu).");
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
        var hesaplar = sp.GetRequiredService<FinancialAccountService>();
        var hesap = await hesaplar.CreateAsync(new FinancialAccountInput { Kod = "ZR", Ad = "Ziraat" });

        await sp.GetRequiredService<ExpenseService>().BatchCreateAsync(
        [
            new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, FinansalHesapId = hesap }
        ]);

        var sil = await Wrap(hesaplar.DeleteAsync(hesap));
        Assert.True(sil.ex is ValidationException,
            $"BULGU-4: kullanılan hesabı silme → {sil.ex?.GetType().Name ?? "BAŞARILI(!)"} (temiz red bekleniyordu).");
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
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariAsync(sp, "Dovizli");

        // ELLE: 100 EUR × 35,123456 = 3.512,3456 baz borç.
        await BorclandirAsync(sp, cari, 100m, "EUR borç", "EUR", 35.123456m);
        Assert.Equal(3512.3456m, await kasa.GetCariBalanceAsync(cari));

        var secilen = await BorcSatirlariAsync(sp, cari);
        var tahsil = await kasa.TekCariTopluKapatAsync(cari, secilen, LedgerAccountType.Kasa);
        var bakiye = await kasa.GetCariBalanceAsync(cari);

        Assert.True(bakiye >= 0m,
            $"BULGU-5: kapatma sonrası bakiye {bakiye} (< 0 → fazla tahsilat). Tahsil edilen {tahsil}.");
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
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariAsync(sp, "Kilit");
        var gecmis = DateTimeOffset.UtcNow.AddDays(-30);
        await sp.GetRequiredService<CashService>().PayAsync(new CashInput
        { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa, Tarih = gecmis, Aciklama = "Eski borç" });
        var secilen = await BorcSatirlariAsync(sp, cari);

        await sp.GetRequiredService<RentACar.Application.Periods.DonemKilidiService>()
            .LockAsync(DateTimeOffset.UtcNow.AddDays(-1));

        // Kapalı döneme geri-tarihli kapatma → red.
        await Assert.ThrowsAsync<ValidationException>(() => kasa.TekCariTopluKapatAsync(
            cari, secilen, LedgerAccountType.Kasa, tarih: gecmis));
        // Gelecek tarih → red (TarihPolitikasi).
        await Assert.ThrowsAsync<ValidationException>(() => kasa.TekCariTopluKapatAsync(
            cari, secilen, LedgerAccountType.Kasa, tarih: DateTimeOffset.UtcNow.AddDays(5)));

        Assert.Equal(500m, await kasa.GetCariBalanceAsync(cari));
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
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariAsync(sp, "Ters");

        await BorclandirAsync(sp, cari, 500m, "Borç");
        var secilen = await BorcSatirlariAsync(sp, cari);
        Assert.Equal(500m, await kasa.TekCariTopluKapatAsync(cari, secilen, LedgerAccountType.Kasa));
        Assert.Equal(0m, await kasa.GetCariBalanceAsync(cari));

        // Kapatma tahsilatını bul (TH- ile başlayan, ters olmayan son kayıt) ve ters çevir.
        var tx = (await kasa.ListAsync()).First(t => t.Tip == CashTransactionType.Tahsilat && !t.TersKayitMi);
        await kasa.ReverseAsync(tx.Id);
        Assert.Equal(500m, await kasa.GetCariBalanceAsync(cari)); // borç geri geldi

        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking().ToListAsync();
        Assert.Equal(rows.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Amount.AmountInBase),
                     rows.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Amount.AmountInBase));

        // Ters kaydın BORÇ satırı ekstrede seçilebilir hale geliyor; toplam bakiyeyi aşamamalı.
        var hepsi = await BorcSatirlariAsync(sp, cari);
        Assert.Equal(2, hepsi.Count); // orijinal borç + ters kayıt borcu
        await Assert.ThrowsAsync<ValidationException>(
            () => kasa.TekCariTopluKapatAsync(cari, hepsi, LedgerAccountType.Kasa)); // 1000 > 500
    }
}
