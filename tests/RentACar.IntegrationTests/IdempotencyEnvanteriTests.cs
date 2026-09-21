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
/// (<see cref="Bakiye"/>: Borç +, Alacak −, baz para).</para>
///
/// <para><b>Test adı = envanter satır no.</b> Tabloyu değiştiren PR bu dosyayı da değiştirmek ZORUNDA.</para>
/// </summary>
[Collection("postgres")]
public sealed class IdempotencyEnvanteriTests(PostgresFixture fx)
{
    // =====================================================================================
    // yardımcılar
    // =====================================================================================

    private static async Task<decimal> Bakiye(IServiceProvider sp, LedgerAccountType tip, Guid? referans = null, bool referansFiltresi = false)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        var q = db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == tip);
        if (referansFiltresi || referans is not null) q = q.Where(e => e.AccountRef == referans);
        var rows = await q.Select(e => new { e.Direction, A = e.Amount.Amount, R = e.Amount.Rate }).ToListAsync();
        return rows.Sum(r => (r.Direction == LedgerDirection.Debit ? 1m : -1m) * r.A * r.R);
    }

    private static Task<decimal> Cari(IServiceProvider sp, Guid cari) => Bakiye(sp, LedgerAccountType.Cari, cari);

    private static async Task<int> DefterSatir(IServiceProvider sp, string sourceType)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await db.AccountLedgerEntries.AsNoTracking().CountAsync(e => e.SourceType == sourceType);
    }

    /// <summary>Tüm defter DENGELİ mi (Σ borç == Σ alacak) — her testin sonunda.</summary>
    private static async Task DengeAsync(IServiceProvider sp)
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

    private static Task<Guid> CariOlustur(IServiceProvider sp, string ad = "Idem") =>
        sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Cari" });

    private static readonly DateTimeOffset KiraBas = new(2027, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private static async Task<(Guid Kira, Guid Musteri, Guid Arac)> KiraOlustur(
        IServiceProvider sp, string plaka, DateTimeOffset bas, int gun)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await CariOlustur(sp, "Kiraci");
        var k = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bas.AddDays(gun), GunlukUcret = 100m });
        return (k, m, v);
    }

    /// <summary>Eşzamanlı iki gönderim: ayrı scope (ayrı DbContext) + aynı kiracı.</summary>
    private static async Task<(T? Deger, Exception? Hata)[]> IkiEsZamanli<T>(
        TestHost host, Guid tenant, Func<IServiceProvider, Task<T>> islem)
    {
        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        async Task<(T?, Exception?)> Sar(IServiceProvider sp)
        {
            try { return (await islem(sp), null); }
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
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();

        await kasa.CollectAsync(new CashInput { CariId = cari, Tutar = 250m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            kasa.CollectAsync(new CashInput { CariId = cari, Tutar = 250m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k }));

        Assert.Equal(-250m, await Cari(sp, cari));                      // ELLE: tahsilat 250 → cari −250
        Assert.Equal(250m, await Bakiye(sp, LedgerAccountType.Kasa));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E01_Tahsilat_anahtarli_ESZAMANLI_yalniz_biri_yazilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();

        var sonuc = await IkiEsZamanli(host, tenant, s => s.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = cari, Tutar = 250m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k }));

        Assert.Single(sonuc, r => r.Hata is null);
        Assert.IsType<MukerrerIslemException>(Assert.Single(sonuc, r => r.Hata is not null).Hata);
        Assert.Equal(-250m, await Cari(sp, cari));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E01_Tahsilat_anahtarsiz_iki_gonderim_iki_ayri_islem()
    {
        // Mekanizma yalnız anahtarla çalışır: anahtarsız iki çağrı iki MEŞRU tahsilattır (bugünkü sözleşme).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);

        await kasa.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        await kasa.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        Assert.Equal(-200m, await Cari(sp, cari));
    }

    [Fact]
    public async Task E02_Odeme_anahtarli_ikinci_gonderim_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();

        await kasa.PayAsync(new CashInput { CariId = cari, Tutar = 400m, Hesap = LedgerAccountType.Banka, IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            kasa.PayAsync(new CashInput { CariId = cari, Tutar = 400m, Hesap = LedgerAccountType.Banka, IslemAnahtari = k }));

        Assert.Equal(400m, await Cari(sp, cari));                       // ELLE: ödeme 400 → cari +400
        Assert.Equal(-400m, await Bakiye(sp, LedgerAccountType.Banka));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E03_E04_Toplu_tahsilat_ve_odeme_RowKey_ikinci_gonderim_409_hic_satir_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var a = await CariOlustur(sp, "A");
        var b = await CariOlustur(sp, "B");
        var tahsilatParti = Guid.NewGuid();
        var odemeParti = Guid.NewGuid();
        CashInput[] Satirlar() =>
        [
            new() { CariId = a, Tutar = 100m, Hesap = LedgerAccountType.Kasa },
            new() { CariId = b, Tutar = 200m, Hesap = LedgerAccountType.Kasa }
        ];

        await kasa.BatchCollectAsync(Satirlar(), tahsilatParti);
        await Assert.ThrowsAsync<MukerrerIslemException>(() => kasa.BatchCollectAsync(Satirlar(), tahsilatParti));
        await kasa.BatchPayAsync(Satirlar(), odemeParti);
        await Assert.ThrowsAsync<MukerrerIslemException>(() => kasa.BatchPayAsync(Satirlar(), odemeParti));

        // ELLE: tahsilat −100/−200 + ödeme +100/+200 → iki cari de 0; 4 belge (2+2), mükerrerler 0 satır.
        Assert.Equal(0m, await Cari(sp, a));
        Assert.Equal(0m, await Cari(sp, b));
        Assert.Equal(4, await Say(sp, db => db.CashTransactions));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E05_Tek_cari_kapatma_TAM_kapatmada_da_ikinci_gonderim_409()
    {
        // ÖNCE: ilk gönderim kalemi tamamen kapattıysa ikinci "zaten kapatılmış" (400) alıyordu;
        // kısmi kapattıysa kısıt (409). F1.4: anahtar önce → her iki halde 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        await kasa.PayAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        var kalem = (await kasa.GetStatementAsync(cari)).Satirlar.Single(x => x.Direction == LedgerDirection.Debit).Id;
        var k = Guid.NewGuid();

        Assert.Equal(100m, await kasa.TekCariTopluKapatAsync(cari, [kalem], LedgerAccountType.Kasa, islemAnahtari: k));
        var ex = await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            kasa.TekCariTopluKapatAsync(cari, [kalem], LedgerAccountType.Kasa, islemAnahtari: k));
        Assert.Equal("Bu işlem zaten kaydedilmiş (çift gönderim / mükerrer).", ex.Message);

        Assert.Equal(0m, await Cari(sp, cari));                          // ELLE: borç 100 − tahsilat 100
        Assert.Equal(1, await Say(sp, db => db.CashTransactions.Where(t => t.Tip == CashTransactionType.Tahsilat)));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E05_Tek_cari_kapatma_KISMI_kapatmada_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        await kasa.PayAsync(new CashInput { CariId = cari, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });
        var kalem = (await kasa.GetStatementAsync(cari)).Satirlar.Single(x => x.Direction == LedgerDirection.Debit).Id;
        var k = Guid.NewGuid();
        var secim = new Dictionary<Guid, decimal?> { [kalem] = 400m };

        await kasa.TekCariTopluKapatAsync(cari, secim, LedgerAccountType.Kasa, islemAnahtari: k);
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            kasa.TekCariTopluKapatAsync(cari, secim, LedgerAccountType.Kasa, islemAnahtari: k));

        Assert.Equal(600m, await Cari(sp, cari));                        // ELLE: 1000 − 400
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E06_Kasa_banka_virman_anahtarli_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var k = Guid.NewGuid();

        await kasa.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m, islemAnahtari: k);
        await kasa.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 500m, islemAnahtari: k); // istisna YOK

        Assert.Equal(-500m, await Bakiye(sp, LedgerAccountType.Kasa));   // ELLE: tek virman 500
        Assert.Equal(500m, await Bakiye(sp, LedgerAccountType.Banka));
        Assert.Equal(2, await DefterSatir(sp, "Virman"));
        Assert.Equal(1, await Say(sp, db => db.Set<KasaVirmanBilgi>()));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E07_Cari_virman_anahtarli_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var kaynak = await CariOlustur(sp, "Kaynak");
        var hedef = await CariOlustur(sp, "Hedef");
        var k = Guid.NewGuid();

        await kasa.TransferBetweenCariAsync(kaynak, hedef, 300m, islemAnahtari: k);
        await kasa.TransferBetweenCariAsync(kaynak, hedef, 300m, islemAnahtari: k);

        Assert.Equal(300m, await Cari(sp, hedef));                        // ELLE: hedef Borç 300
        Assert.Equal(-300m, await Cari(sp, kaynak));                      //       kaynak Alacak 300
        Assert.Equal(1, await Say(sp, db => db.Set<CariVirmanBilgi>()));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E08_Ters_kayit_sirali_ikinci_istek_ARTIK_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        var id = await kasa.CollectAsync(new CashInput { CariId = cari, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });

        await kasa.ReverseAsync(id);
        var ex = await Assert.ThrowsAsync<MukerrerIslemException>(() => kasa.ReverseAsync(id));
        Assert.Equal("Bu işlem zaten ters kaydedilmiş.", ex.Message);

        Assert.Equal(0m, await Cari(sp, cari));                           // ELLE: −1000 + 1000
        Assert.Equal(1, await Say(sp, db => db.CashTransactions.Where(t => t.TersKayitMi)));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E08_Ters_kayit_ESZAMANLI_kaybeden_ayni_tip_ve_ayni_mesaj()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var cari = await CariOlustur(sp);
        var id = await sp.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = cari, Tutar = 1000m, Hesap = LedgerAccountType.Kasa });

        var sonuc = await IkiEsZamanli(host, tenant, s => s.GetRequiredService<CashService>().ReverseAsync(id));

        Assert.Single(sonuc, r => r.Hata is null);
        var hata = Assert.IsType<MukerrerIslemException>(Assert.Single(sonuc, r => r.Hata is not null).Hata);
        // Yarışı ön-kontrolde de kaybetse kısıtta da kaybetse AYNI metin (zamanlamadan bağımsız).
        Assert.Equal("Bu işlem zaten ters kaydedilmiş.", hata.Message);
        Assert.Equal(0m, await Cari(sp, cari));
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
        var dep = sp.GetRequiredService<DepozitoService>();
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();

        var id1 = await dep.AlAsync(cari, 500m, LedgerAccountType.Kasa, islemAnahtari: k);
        var id2 = await dep.AlAsync(cari, 500m, LedgerAccountType.Kasa, islemAnahtari: k);

        Assert.Equal(k, id1);
        Assert.Equal(k, id2);
        Assert.Equal(500m, await dep.GetBakiyeAsync(cari));               // ELLE: tek 500
        Assert.Equal(500m, await Bakiye(sp, LedgerAccountType.Kasa));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E10_Depozito_iade_TUTULANIN_TAMAMI_icin_de_ikinci_gonderim_sessiz()
    {
        // ÖNCE: tamamını iade eden gönderimin tekrarı bakiye çitine takılıp 400 alıyordu (kısmi iade
        // sessiz geçiyordu) — sonuç tutara bağlıydı. F1.4: anahtar önce → sessiz.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepozitoService>();
        var cari = await CariOlustur(sp);
        await dep.AlAsync(cari, 500m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.IadeAsync(cari, 500m, LedgerAccountType.Kasa, islemAnahtari: k);
        await dep.IadeAsync(cari, 500m, LedgerAccountType.Kasa, islemAnahtari: k);

        Assert.Equal(0m, await dep.GetBakiyeAsync(cari));                 // ELLE: 500 − 500
        Assert.Equal(0m, await Bakiye(sp, LedgerAccountType.Kasa));
        Assert.Equal(2, await DefterSatir(sp, "DepozitoIade"));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E10_Depozito_iade_kismi_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepozitoService>();
        var cari = await CariOlustur(sp);
        await dep.AlAsync(cari, 500m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.IadeAsync(cari, 200m, LedgerAccountType.Kasa, islemAnahtari: k);
        await dep.IadeAsync(cari, 200m, LedgerAccountType.Kasa, islemAnahtari: k);

        Assert.Equal(300m, await dep.GetBakiyeAsync(cari));               // ELLE: 500 − 200
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E11_Depozito_mahsup_tamami_ikinci_gonderim_sessiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepozitoService>();
        var cari = await CariOlustur(sp);
        await dep.AlAsync(cari, 300m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        await dep.MahsupAsync(cari, 300m, islemAnahtari: k);
        await dep.MahsupAsync(cari, 300m, islemAnahtari: k);

        Assert.Equal(0m, await dep.GetBakiyeAsync(cari));
        Assert.Equal(-300m, await Cari(sp, cari));                        // ELLE: Alacak Cari 300 (tek)
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E12_Depozito_irat_tamami_ikinci_gonderim_sessiz_ayni_id()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var dep = sp.GetRequiredService<DepozitoService>();
        var cari = await CariOlustur(sp);
        await dep.AlAsync(cari, 400m, LedgerAccountType.Kasa);
        var k = Guid.NewGuid();

        Assert.Equal(k, await dep.IratAsync(cari, 400m, islemAnahtari: k));
        Assert.Equal(k, await dep.IratAsync(cari, 400m, islemAnahtari: k));

        Assert.Equal(0m, await dep.GetBakiyeAsync(cari));
        Assert.Equal(-400m, await Bakiye(sp, LedgerAccountType.Gelir));   // ELLE: gelir 400 (Alacak)
        Assert.Equal(1, await Say(sp, db => db.DepozitoIratlar));
        await DengeAsync(sp);
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
        var svc = sp.GetRequiredService<BakiyeDuzeltmeService>();
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();
        BakiyeDuzeltmeInput Girdi() => new() { CariId = cari, Tutar = 150m, Yon = BakiyeDuzeltmeYonu.Borclandir, IslemAnahtari = k };

        Assert.Equal(k, await svc.AdjustAsync(Girdi()));
        Assert.Equal(k, await svc.AdjustAsync(Girdi()));

        Assert.Equal(150m, await Cari(sp, cari));                          // ELLE: borçlandırma 150 (tek)
        await DengeAsync(sp);
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
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();
        ManualInvoiceInput Girdi() => new() { CariId = cari, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = k };

        Assert.Equal(k, await fat.CreateManualAsync(Girdi()));
        Assert.Equal(k, await fat.CreateManualAsync(Girdi()));

        Assert.Equal(1200m, await Cari(sp, cari));                         // ELLE: 1000 + %20 = 1200
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E14_Manuel_fatura_ESZAMANLI_ikisi_de_ayni_id_ile_sessiz_basari()
    {
        // ÖNCE: yarışı kaybeden PK'ye çarpıp "Kira zaten faturalanmış." (400) alıyordu.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();

        var sonuc = await IkiEsZamanli(host, tenant, s => s.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 1000m, KdvOrani = 0.20m, IslemAnahtari = k }));

        Assert.All(sonuc, r => Assert.Null(r.Hata));
        Assert.All(sonuc, r => Assert.Equal(k, r.Deger));
        Assert.Equal(1200m, await Cari(sp, cari));
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E15_Kira_faturasi_ikinci_kesim_400_tam_faturalanmis()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (kira, musteri, _) = await KiraOlustur(sp, "34 ID 15", KiraBas, 3);

        await fat.CreateFromRentalAsync(kira);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => fat.CreateFromRentalAsync(kira));
        Assert.Equal("Kira zaten tam faturalanmış (yeni ek bedel yok).", ex.Message);

        Assert.Equal(300m, await Cari(sp, musteri));                       // ELLE: 3 gün × 100 = 300 brüt
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E15_Kira_faturasi_ESZAMANLI_kaybeden_400_dogrulama()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var (kira, musteri, _) = await KiraOlustur(sp, "34 ID 16", KiraBas, 3);

        var sonuc = await IkiEsZamanli(host, tenant, s => s.GetRequiredService<InvoiceService>().CreateFromRentalAsync(kira));

        Assert.Single(sonuc, r => r.Hata is null);
        // Tip TAM ValidationException (mükerrer değil): kira-fatura kısıtı iş kuralıdır, idempotency kısıtı değil.
        Assert.Equal(typeof(ValidationException), Assert.Single(sonuc, r => r.Hata is not null).Hata!.GetType());
        Assert.Equal(300m, await Cari(sp, musteri));
        Assert.Equal(1, await Say(sp, db => db.Invoices));
    }

    [Fact]
    public async Task E16_Toplu_fatura_ikinci_gonderim_yeni_belge_uretmez_atlananlara_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (k1, m1, _) = await KiraOlustur(sp, "34 ID 17", KiraBas, 3);
        var (k2, m2, _) = await KiraOlustur(sp, "34 ID 18", KiraBas, 2);

        var ilk = await fat.BatchCreateFromRentalsAsync([k1, k2]);
        var ikinci = await fat.BatchCreateFromRentalsAsync([k1, k2]);

        Assert.Equal(2, ilk.Kesilen.Count);
        Assert.Empty(ikinci.Kesilen);
        Assert.Equal(2, ikinci.Atlananlar.Count);
        Assert.All(ikinci.Atlananlar, a => Assert.Contains("tam faturalanmış", a));
        Assert.Equal(300m, await Cari(sp, m1));                            // ELLE: 3 × 100
        Assert.Equal(200m, await Cari(sp, m2));                            // ELLE: 2 × 100
        Assert.Equal(2, await Say(sp, db => db.Invoices));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E17_Iade_faturasi_ikinci_iade_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var cari = await CariOlustur(sp);
        var kaynak = await fat.CreateManualAsync(new ManualInvoiceInput { CariId = cari, NetTutar = 1000m, KdvOrani = 0.20m });

        await fat.CreateIadeAsync(kaynak);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => fat.CreateIadeAsync(kaynak));
        Assert.Equal("Bu fatura zaten iade edilmiş.", ex.Message);

        Assert.Equal(0m, await Cari(sp, cari));                            // ELLE: +1200 − 1200
        Assert.Equal(1, await Say(sp, db => db.Invoices.Where(i => i.IadeMi)));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E18_Donem_faturasi_ikinci_kesim_sessiz_ayni_fatura()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var fat = sp.GetRequiredService<InvoiceService>();
        var (kira, musteri, _) = await KiraOlustur(sp, "34 ID 19", KiraBas, 90);

        var f1 = await fat.CreateDonemFaturasiAsync(kira, 1);
        Assert.Equal(f1, await fat.CreateDonemFaturasiAsync(kira, 1));

        Assert.Equal(3100m, await Cari(sp, musteri));                      // ELLE: D1 = 31 gün × 100
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        await DengeAsync(sp);
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
        var (kira, musteri, _) = await KiraOlustur(sp, "34 ID 20", KiraBas, 90);

        var sonuc = await IkiEsZamanli(host, tenant, s => s.GetRequiredService<InvoiceService>().CreateDonemFaturasiAsync(kira, 1));

        Assert.All(sonuc, r => Assert.Null(r.Hata));
        Assert.Equal(sonuc[0].Deger, sonuc[1].Deger);
        Assert.Equal(3100m, await Cari(sp, musteri));
        Assert.Equal(1, await Say(sp, db => db.Invoices));
        var donem = (await sp.GetRequiredService<IFaturaDonemRepository>().ListForRentalAsync(kira)).Single(d => d.DonemSira == 1);
        Assert.Equal(FaturaDonemDurum.Kesildi, donem.Durum);                // Atlandi'ye DÜŞMEDİ
    }

    [Fact]
    public async Task E19_Donem_kes_ve_tahsil_et_ikinci_gonderim_sessiz_tahsilat_yazilmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<DonemTahsilatService>();
        var (kira, musteri, _) = await KiraOlustur(sp, "34 ID 21", KiraBas, 90);

        var ilk = await svc.KesVeTahsilEtDetayAsync(kira, 1, true, LedgerAccountType.Kasa);
        var ikinci = await svc.KesVeTahsilEtDetayAsync(kira, 1, true, LedgerAccountType.Kasa);

        Assert.True(ilk.TahsilatYazildi);
        Assert.False(ikinci.TahsilatYazildi);
        Assert.Equal(ilk.InvoiceId, ikinci.InvoiceId);
        Assert.Equal(0m, await Cari(sp, musteri));                          // ELLE: fatura 3100 − tahsilat 3100
        Assert.Equal(3100m, await Bakiye(sp, LedgerAccountType.Kasa));
        Assert.Equal(1, await Say(sp, db => db.CashTransactions));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E20_Otomatik_tahsilat_ikinci_calistirma_hic_bir_sey_yazmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<OtomatikTahsilatService>();
        var bas = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 ID 22" });
        var m = await CariOlustur(sp, "Oto");
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        { MusteriId = m, VehicleId = v, BasTar = bas, BitTar = bas.AddDays(90), GunlukUcret = 100m, DonemselFaturalama = true });

        var secim = (await svc.AdaylarAsync()).Select(a => (a.RentalId, a.DonemSira)).ToList();
        Assert.Equal(2, secim.Count);                                       // ELLE: 65 gün → 2 dönem vadeli
        var ilk = await svc.CalistirAsync(secim, tahsilatYap: true, LedgerAccountType.Kasa);
        var cariSonra = await Cari(sp, m);
        var kasaSonra = await Bakiye(sp, LedgerAccountType.Kasa);

        var ikinci = await svc.CalistirAsync(secim, tahsilatYap: true, LedgerAccountType.Kasa);

        Assert.Equal(2, ilk.Kesilen);
        Assert.Equal(2, ilk.Tahsilat);
        Assert.Equal(0, ikinci.Kesilen);
        Assert.Equal(0, ikinci.Tahsilat);
        Assert.Equal(2, ikinci.Atlananlar.Count);
        Assert.Equal(0m, cariSonra);                                        // ELLE: kesilen = tahsil edilen
        Assert.Equal(cariSonra, await Cari(sp, m));                          // çift çalıştırma bakiyeyi DEĞİŞTİRMEDİ
        Assert.Equal(kasaSonra, await Bakiye(sp, LedgerAccountType.Kasa));
        Assert.Equal(2, await Say(sp, db => db.Invoices));
        Assert.Equal(2, await Say(sp, db => db.CashTransactions));
        await DengeAsync(sp);
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
        ExpenseInput Girdi() => new()
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.Nakit, IslemAnahtari = k };

        await gid.CreateAsync(Girdi());
        var ex = await Assert.ThrowsAsync<MukerrerIslemException>(() => gid.CreateAsync(Girdi()));
        Assert.Equal("Bu gider zaten kaydedilmiş (çift gönderim).", ex.Message);

        Assert.Equal(1000m, await Bakiye(sp, LedgerAccountType.Gider));      // ELLE: tek gider 1000
        Assert.Equal(-1000m, await Bakiye(sp, LedgerAccountType.Kasa));
        Assert.Equal(1, await Say(sp, db => db.Expenses));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E21_Tekil_gider_anahtarsiz_iki_gonderim_iki_gider()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        ExpenseInput Girdi() => new() { Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.Nakit };

        await gid.CreateAsync(Girdi());
        await gid.CreateAsync(Girdi());
        Assert.Equal(200m, await Bakiye(sp, LedgerAccountType.Gider));
    }

    [Fact]
    public async Task E22_Toplu_gider_RowKey_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var parti = Guid.NewGuid();
        ExpenseInput[] Kalemler() =>
        [
            new() { Tip = ExpenseType.Genel, NetTutar = 300m, KdvOrani = 0m },
            new() { Tip = ExpenseType.Genel, NetTutar = 200m, KdvOrani = 0m }
        ];

        await gid.BatchCreateAsync(Kalemler(), parti);
        await Assert.ThrowsAsync<MukerrerIslemException>(() => gid.BatchCreateAsync(Kalemler(), parti));

        Assert.Equal(500m, await Bakiye(sp, LedgerAccountType.Gider));       // ELLE: 300 + 200
        Assert.Equal(2, await Say(sp, db => db.Expenses));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E23_Gider_odemesi_KALANIN_TAMAMI_icin_de_ikinci_gonderim_sessiz_null()
    {
        // ÖNCE: kalanın tamamını kapatan ödemenin tekrarı "kalanı yok" (400) alıyordu. F1.4: sessiz null.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var tedarikci = await CariOlustur(sp, "Tedarikci");
        var giderId = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = OdemeYontemi.AcikHesap, CariId = tedarikci });
        var k = Guid.NewGuid();

        Assert.NotNull(await gid.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, IslemAnahtari = k }));
        Assert.Null(await gid.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, IslemAnahtari = k }));

        var durum = (await gid.OdemeDurumlariAsync(await gid.ListAsync()))[giderId];
        Assert.Equal(1200m, durum.Odenen);                                  // ELLE: 1000 + %20, tek ödeme
        Assert.Equal(0m, durum.Kalan);
        Assert.Equal(1, await Say(sp, db => db.Set<GiderOdeme>()));
    }

    [Fact]
    public async Task E23_Gider_odemesi_kismi_ikinci_gonderim_sessiz_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var gid = sp.GetRequiredService<ExpenseService>();
        var tedarikci = await CariOlustur(sp, "Tedarikci");
        var giderId = await gid.CreateAsync(new ExpenseInput
        { Tip = ExpenseType.Genel, NetTutar = 1000m, KdvOrani = 0.20m, OdemeYontemi = OdemeYontemi.AcikHesap, CariId = tedarikci });
        var k = Guid.NewGuid();

        Assert.NotNull(await gid.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 400m, IslemAnahtari = k }));
        Assert.Null(await gid.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 400m, IslemAnahtari = k }));

        Assert.Equal(800m, (await gid.OdemeDurumlariAsync(await gid.ListAsync()))[giderId].Kalan); // ELLE: 1200 − 400
    }

    [Fact]
    public async Task E24_Gelen_efatura_ikinci_giderlestirme_ARTIK_409_mukerrer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<GelenEFaturaService>();
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc), TimeSpan.Zero);
        var id = await svc.CreateManualAsync(new GelenEFaturaInput
        {
            Ettn = "IDEM-F14", GonderenVkn = "1234567890", GonderenUnvan = "Tedarikçi A.Ş.", Tarih = t,
            NetTutar = 1000m, KdvTutar = 200m, GenelToplam = 1200m, Currency = "TRY"
        });
        await svc.OnaylaAsync(id);

        await svc.GiderlestirAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = OdemeYontemi.Nakit });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            svc.GiderlestirAsync(new GelenEFaturaGiderInput { Id = id, OdemeYontemi = OdemeYontemi.Nakit }));

        Assert.Equal(-1200m, await Bakiye(sp, LedgerAccountType.Kasa));     // ELLE: tek çıkış 1200
        Assert.Equal(1, await Say(sp, db => db.Expenses));
        await DengeAsync(sp);
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
        var cari = Guid.NewGuid();
        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = cari, Tutar = 300m });

        Assert.True(await svc.YansitAsync(id));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.YansitAsync(id));
        Assert.Equal("Yalnız 'Yeni' durumundaki ceza yansıtılabilir.", ex.Message);
        Assert.Equal(300m, await Cari(sp, cari));
        Assert.Equal(2, await DefterSatir(sp, "Ceza"));
    }

    [Fact]
    public async Task E25_Ceza_yansitma_ESZAMANLI_kaybeden_ARTIK_400_sessiz_false_degil()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        var cari = Guid.NewGuid();
        var id = await sp.GetRequiredService<PenaltyService>().CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = cari, Tutar = 300m });

        var sonuc = await IkiEsZamanli(host, tenant, s => s.GetRequiredService<PenaltyService>().YansitAsync(id));

        Assert.Single(sonuc, r => r.Hata is null && r.Deger);
        Assert.Equal(typeof(ValidationException), Assert.Single(sonuc, r => r.Hata is not null).Hata!.GetType());
        Assert.Equal(300m, await Cari(sp, cari));
        Assert.Equal(2, await DefterSatir(sp, "Ceza"));
    }

    private static async Task<(Guid Ceza, Guid Satir)> CezaKalemi(IServiceProvider sp, decimal tutar)
    {
        var svc = sp.GetRequiredService<PenaltyService>();
        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Satirlar = [new PenaltySatirInput { Tutar = tutar, Sebep = "Hız" }] });
        return (id, (await svc.ListSatirAsync(id)).Single().Id);
    }

    [Fact]
    public async Task E26_Ceza_odemesi_TAM_odemede_de_ikinci_gonderim_409()
    {
        // ÖNCE: kalemi kapatan ödemenin tekrarı "ödenecek bakiye yok" (400) alıyordu. F1.4: 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var (ceza, satir) = await CezaKalemi(sp, 500m);
        var k = Guid.NewGuid();

        await svc.KismiOdeAsync(ceza, new CezaOdemeInput { SatirId = satir, IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() => svc.KismiOdeAsync(ceza, new CezaOdemeInput { SatirId = satir, IslemAnahtari = k }));

        Assert.Equal(-500m, await Bakiye(sp, LedgerAccountType.Kasa));      // ELLE: tek ödeme 500
        Assert.Equal(1, (await svc.ListOdemeAsync(ceza)).Count);
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E26_Ceza_odemesi_kismi_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var (ceza, satir) = await CezaKalemi(sp, 500m);
        var k = Guid.NewGuid();

        await svc.KismiOdeAsync(ceza, new CezaOdemeInput { SatirId = satir, Tutar = 200m, IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            svc.KismiOdeAsync(ceza, new CezaOdemeInput { SatirId = satir, Tutar = 200m, IslemAnahtari = k }));

        Assert.Equal(300m, (await svc.GetAsync(ceza))!.Kalan);             // ELLE: 500 − 200
        await DengeAsync(sp);
    }

    // =====================================================================================
    // MTV / Muayene / Sigorta
    // =====================================================================================

    private static async Task<(RegulationService Reg, Guid Arac)> Regulasyon(IServiceProvider sp, string plaka)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka, Durum = VehicleStatus.Musait });
        return (sp.GetRequiredService<RegulationService>(), v);
    }

    [Fact]
    public async Task E27_Mtv_anahtarli_TAM_odemede_de_ikinci_gonderim_409()
    {
        // ÖNCE: kaydı kapatan anahtarlı ödemenin tekrarı "MTV zaten ödendi." (400) alıyordu. F1.4: 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulasyon(sp, "34 ID 27");
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        var k = Guid.NewGuid();

        await reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa, odeme: new RegulasyonOdemeInput { IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa, odeme: new RegulasyonOdemeInput { IslemAnahtari = k }));

        Assert.Equal(1000m, await Bakiye(sp, LedgerAccountType.Gider, v));  // ELLE: tek ödeme 1000
        Assert.Equal(2, await DefterSatir(sp, "MtvOdeme"));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E27_Mtv_anahtarsiz_tam_odeme_ikinci_istek_400_zaten_odendi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulasyon(sp, "34 ID 28");
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        await reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa));
        Assert.Equal("MTV zaten ödendi.", ex.Message);
        Assert.Equal(1000m, await Bakiye(sp, LedgerAccountType.Gider, v));
    }

    [Fact]
    public async Task E27_Mtv_kismi_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulasyon(sp, "34 ID 29");
        var mtv = await reg.AddMtvAsync(v, "2026/1", 1000m, new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
        var k = Guid.NewGuid();

        await reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa, odeme: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            reg.MtvOdeAsync(mtv, LedgerAccountType.Kasa, odeme: new RegulasyonOdemeInput { Tutar = 400m, IslemAnahtari = k }));

        Assert.Equal(400m, await Bakiye(sp, LedgerAccountType.Gider, v));   // ELLE: tek 400
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E28_Muayene_anahtarli_TAM_odemede_de_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulasyon(sp, "34 ID 30");
        var insp = await reg.AddInspectionAsync(v, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2028, 3, 1, 0, 0, 0, TimeSpan.Zero), 800m);
        var k = Guid.NewGuid();

        await reg.MuayeneOdeAsync(insp, LedgerAccountType.Kasa, odeme: new RegulasyonOdemeInput { IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            reg.MuayeneOdeAsync(insp, LedgerAccountType.Kasa, odeme: new RegulasyonOdemeInput { IslemAnahtari = k }));

        Assert.Equal(800m, await Bakiye(sp, LedgerAccountType.Gider, v));   // ELLE: tek 800
        Assert.Equal(2, await DefterSatir(sp, "MuayeneOdeme"));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E29_Sigorta_ikinci_odeme_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (reg, v) = await Regulasyon(sp, "34 ID 31");
        var pol = await reg.AddInsuranceAsync(v, InsuranceType.Kasko,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            1200m, "POL-IDEM", "Firma", null);

        await reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa));
        Assert.Equal("Sigorta zaten ödendi.", ex.Message);

        Assert.Equal(1200m, await Bakiye(sp, LedgerAccountType.Gider, v));  // ELLE: prim 1200 (tek)
        await DengeAsync(sp);
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
        var cari = await CariOlustur(sp, "Rucu");
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = v, Tip = ServisTipi.Ariza, GirisKm = 0, HasarSorumlu = HasarSorumlu.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "Tampon", Tutar = 1000m }]
        });
        await svc.BaslatAsync(id);
        await svc.TamamlaAsync(id, cikisKm: 100);

        await svc.YansitAsync(id, cari);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.YansitAsync(id, cari));
        Assert.Equal("Servis maliyeti zaten yansıtıldı.", ex.Message);
        Assert.Equal(500m, await Cari(sp, cari));                           // ELLE: 1000 × 0,5
    }

    private sealed class SahteHgs(IReadOnlyList<TollCrossing> gecisler) : IHgsService
    {
        public Task<IReadOnlyList<TollCrossing>> GetCrossingsAsync(string plaka, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => Task.FromResult(gecisler);
    }

    [Fact]
    public async Task E31_Hgs_yansitma_ikinci_istek_sessiz_deterministik_anahtar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var cari = Guid.NewGuid();
        var t = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var hgs = new HgsReflectionService(new SahteHgs([new TollCrossing(t, "Köprü", 100m)]),
            sp.GetRequiredService<ILedgerPoster>(), sp.GetRequiredService<IPeriodLockGuard>(), sp.GetRequiredService<ICurrentUser>());

        var r1 = await hgs.ReflectAsync(cari, "34ID33", t, t.AddDays(1));
        var r2 = await hgs.ReflectAsync(cari, "34ID33", t, t.AddDays(1));

        Assert.Equal(103m, r1.YansitilanTutar);
        Assert.Equal(103m, r2.YansitilanTutar);                              // sessiz: aynı sonuç, ikinci yazım yok
        Assert.Equal(103m, await Cari(sp, cari));                            // ELLE: 100 × 1,03 (tek)
        Assert.Equal(2, await DefterSatir(sp, "Hgs"));
    }

    [Fact]
    public async Task E32_Arac_kredi_taksiti_anahtarli_ikinci_gonderim_ARTIK_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AracKrediService>();
        var kredi = await svc.CreateAsync(new AracKrediInput { BankaAdi = "Banka", KrediTutari = 12000m, FaizOran = 0m, TaksitSayisi = 12 });
        var k = Guid.NewGuid();

        Assert.True(await svc.TaksitOdeAsync(kredi, islemAnahtari: k));
        var ex = await Assert.ThrowsAsync<MukerrerIslemException>(() => svc.TaksitOdeAsync(kredi, islemAnahtari: k));
        Assert.Equal("Bu taksit ödemesi zaten kaydedilmiş (çift gönderim).", ex.Message);

        Assert.Equal(1, (await svc.GetAsync(kredi))!.OdenenTaksit);
        Assert.Equal(1000m, await Bakiye(sp, LedgerAccountType.Gider));      // ELLE: 12000 / 12, faizsiz
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E32_Arac_kredi_SON_taksit_tekrari_da_409_sessiz_false_degil()
    {
        // ÖNCE: son taksidin anahtarlı tekrarı "tüm taksitler ödendi" çitine takılıp sessiz false
        // dönüyordu (ara taksitte kısıt reddi). F1.4: anahtar önce → 409.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<AracKrediService>();
        var kredi = await svc.CreateAsync(new AracKrediInput { BankaAdi = "Banka", KrediTutari = 1000m, FaizOran = 0m, TaksitSayisi = 1 });
        var k = Guid.NewGuid();

        Assert.True(await svc.TaksitOdeAsync(kredi, islemAnahtari: k));
        await Assert.ThrowsAsync<MukerrerIslemException>(() => svc.TaksitOdeAsync(kredi, islemAnahtari: k));
        // Anahtarsız tekrar: bugünkü sözleşme (tüm taksitler ödendi → false, yazım yok).
        Assert.False(await svc.TaksitOdeAsync(kredi));

        Assert.Equal(1000m, await Bakiye(sp, LedgerAccountType.Gider));      // ELLE: tek taksit 1000
        await DengeAsync(sp);
    }

    private static async Task<(Guid Kira, Guid Tedarikci)> DisHizmetKur(IServiceProvider sp, string plaka)
    {
        var (kira, _, _) = await KiraOlustur(sp, plaka, KiraBas, 3);
        var t = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = "Tedarikçi AŞ" });
        return (kira, t);
    }

    [Fact]
    public async Task E33_Dis_hizmet_anahtarli_ikinci_gonderim_409()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<DisHizmetService>();
        var (kira, tedarikci) = await DisHizmetKur(sp, "34 ID 34");
        var k = Guid.NewGuid();
        DisHizmetInput Girdi() => new()
        {
            RentalId = kira, FaturaKesilecekCariId = tedarikci, AlinanHizmet = "Transfer",
            HizmetBedeli = 1000m, TedarikciKomisyonOran = 10m, IslemAnahtari = k
        };

        await svc.CreateAsync(Girdi());
        await Assert.ThrowsAsync<MukerrerIslemException>(() => svc.CreateAsync(Girdi()));

        Assert.Equal(-900m, await Cari(sp, tedarikci));                     // ELLE: alacak 1000 − komisyon 100
        Assert.Single(await svc.ListForRentalAsync(kira));
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E34_Dis_hizmet_iptal_ikinci_istek_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<DisHizmetService>();
        var (kira, tedarikci) = await DisHizmetKur(sp, "34 ID 35");
        var id = await svc.CreateAsync(new DisHizmetInput
        { RentalId = kira, FaturaKesilecekCariId = tedarikci, AlinanHizmet = "Transfer", HizmetBedeli = 1000m, TedarikciKomisyonOran = 10m });

        await svc.IptalEtAsync(id);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.IptalEtAsync(id));
        Assert.Equal("Kayıt zaten iptal edilmiş.", ex.Message);

        Assert.Equal(0m, await Cari(sp, tedarikci));                        // ELLE: −900 + 900
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E35_Arac_satis_ikinci_satis_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var satis = sp.GetRequiredService<VehicleSaleService>();
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 ID 36" });
        var alici = await CariOlustur(sp, "Alici");
        VehicleSaleInput Girdi() => new() { VehicleId = v, AliciCariId = alici, SatisNet = 1000m, KdvOrani = 0.20m };

        await satis.CreateAsync(Girdi());
        var ex = await Assert.ThrowsAsync<ValidationException>(() => satis.CreateAsync(Girdi()));
        Assert.Equal("Araç zaten satılmış.", ex.Message);

        Assert.Equal(1200m, await Cari(sp, alici));                         // ELLE: 1000 + %20
        await DengeAsync(sp);
    }

    [Fact]
    public async Task E36_Donem_kapanis_ikinci_istek_400()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var isTarih = new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var kapanis = new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);
        var cari = await CariOlustur(sp);
        await sp.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        { CariId = cari, NetTutar = 1000m, KdvOrani = 0m, Tarih = isTarih });
        var svc = sp.GetRequiredService<DonemKapanisFisiService>();

        await svc.KapatAsync(kapanis);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.KapatAsync(kapanis));
        Assert.StartsWith("Dönem zaten 2026-06-30 tarihine kapalı.", ex.Message);

        Assert.Equal(0m, await Bakiye(sp, LedgerAccountType.Gelir));        // ELLE: gelir 1000 kapatıldı (tek fiş)
        Assert.Equal(-1000m, await Bakiye(sp, LedgerAccountType.DonemSonucu));
        await DengeAsync(sp);
    }

    // =====================================================================================
    // Adversarial (F1.4 öz-denetim) — başlıktan türetilen anahtarın uçtan uca davranışı
    // =====================================================================================

    [Fact]
    public async Task A1_Iki_kullanici_ayni_baslik_degeri_CAKISMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        const string baslik = "ortak-istemci-anahtari-0001";
        var ali = Guid.NewGuid();
        var ayse = Guid.NewGuid();
        using var s1 = host.ScopeFor(tenant, ali);
        using var s2 = host.ScopeFor(tenant, ayse);
        var cari = await CariOlustur(s1.ServiceProvider);

        await s1.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = IslemAnahtariTuretici.Turet(tenant, ali, baslik) });
        await s2.ServiceProvider.GetRequiredService<CashService>().CollectAsync(new CashInput
        { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = IslemAnahtariTuretici.Turet(tenant, ayse, baslik) });

        Assert.Equal(-200m, await Cari(s1.ServiceProvider, cari));          // ELLE: iki MEŞRU tahsilat
    }

    [Fact]
    public async Task A2_Iki_kiraci_ayni_kullanici_id_ve_baslik_CAKISMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        const string baslik = "ortak-istemci-anahtari-0002";
        var user = Guid.NewGuid();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        using var s1 = host.ScopeFor(t1, user);
        using var s2 = host.ScopeFor(t2, user);
        var c1 = await CariOlustur(s1.ServiceProvider);
        var c2 = await CariOlustur(s2.ServiceProvider);

        // Manuel fatura: anahtar = faturanın PK'si (kiracı-GLOBAL). Ham başlık PK olsaydı ikinci kiracı
        // birincinin faturasına çarpardı; türetilmiş anahtar kiracıyı içerdiği için çarpmaz.
        var k1 = IslemAnahtariTuretici.Turet(t1, user, baslik);
        var k2 = IslemAnahtariTuretici.Turet(t2, user, baslik);
        Assert.NotEqual(k1, k2);
        Assert.Equal(k1, await s1.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c1, NetTutar = 100m, KdvOrani = 0m, IslemAnahtari = k1 }));
        Assert.Equal(k2, await s2.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c2, NetTutar = 100m, KdvOrani = 0m, IslemAnahtari = k2 }));

        Assert.Equal(100m, await Cari(s1.ServiceProvider, c1));
        Assert.Equal(100m, await Cari(s2.ServiceProvider, c2));
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
        var c1 = await CariOlustur(s1.ServiceProvider);
        var c2 = await CariOlustur(s2.ServiceProvider);
        var yabanci = await s1.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c1, NetTutar = 100m, KdvOrani = 0m });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s2.ServiceProvider.GetRequiredService<InvoiceService>()
            .CreateManualAsync(new ManualInvoiceInput { CariId = c2, NetTutar = 500m, KdvOrani = 0m, IslemAnahtari = yabanci }));
        Assert.Contains("başka bir kayıtla çakıştı", ex.Message);
        Assert.Equal(0m, await Cari(s2.ServiceProvider, c2));
        Assert.Equal(100m, await Cari(s1.ServiceProvider, c1));             // diğer kiracıya DOKUNULMADI
    }

    [Fact]
    public async Task A3_Ayni_baslik_farkli_islem_para_kaybi_yok()
    {
        // Sözleşme: istemci anahtarı her 2xx'ten sonra yeniler. Buna uymayan istemcide bile sonuç ya
        // görünür red (aynı tablo: tahsilat→ödeme 409) ya da bağımsız işlem (farklı tablo) olur —
        // SESSİZ para kaybı ya da çift yazım yok.
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant, user);
        var sp = scope.ServiceProvider;
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        var k = IslemAnahtariTuretici.Turet(tenant, user, "yeniden-kullanilan-anahtar");

        await kasa.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            kasa.PayAsync(new CashInput { CariId = cari, Tutar = 70m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = k }));
        await sp.GetRequiredService<DepozitoService>().AlAsync(cari, 50m, LedgerAccountType.Kasa, islemAnahtari: k);

        Assert.Equal(-100m, await Cari(sp, cari));                          // ELLE: yalnız tahsilat
        Assert.Equal(50m, await sp.GetRequiredService<DepozitoService>().GetBakiyeAsync(cari));
        Assert.Equal(150m, await Bakiye(sp, LedgerAccountType.Kasa));        // ELLE: 100 + 50
        await DengeAsync(sp);
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
        var (kira, musteri, _) = await KiraOlustur(sp, "34 ID 40", KiraBas, 3);
        var kasa = sp.GetRequiredService<CashService>();
        var panelAnahtari = RentACar.Web.Finance.TahsilatAnahtar.Uret(kira, 300m, 0);

        var sekme1 = IslemAnahtariTuretici.Sec(panelAnahtari, IslemAnahtariTuretici.Turet(tenant, user, "sekme-1-rastgele-anahtar"));
        var sekme2 = IslemAnahtariTuretici.Sec(panelAnahtari, IslemAnahtariTuretici.Turet(tenant, user, "sekme-2-rastgele-anahtar"));
        Assert.Equal(panelAnahtari, sekme1);
        Assert.Equal(panelAnahtari, sekme2);

        await kasa.CollectAsync(new CashInput { CariId = musteri, RentalId = kira, Tutar = 300m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = sekme1 });
        await Assert.ThrowsAsync<MukerrerIslemException>(() =>
            kasa.CollectAsync(new CashInput { CariId = musteri, RentalId = kira, Tutar = 300m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = sekme2 }));

        Assert.Equal(-300m, await Cari(sp, musteri));                       // ELLE: tek tahsilat 300
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
        var dep = sp.GetRequiredService<DepozitoService>();
        var kasa = sp.GetRequiredService<CashService>();
        var cari = await CariOlustur(sp);
        var k = Guid.NewGuid();

        // (a) Depozito: tutulan yokken iade → bakiye çiti (kilidin arkasında) reddeder, hiçbir şey yazılmaz.
        await Assert.ThrowsAsync<ValidationException>(() => dep.IadeAsync(cari, 200m, LedgerAccountType.Kasa, islemAnahtari: k));
        await dep.AlAsync(cari, 500m, LedgerAccountType.Kasa);
        await dep.IadeAsync(cari, 200m, LedgerAccountType.Kasa, islemAnahtari: k);   // AYNI anahtar → yazılır
        Assert.Equal(300m, await dep.GetBakiyeAsync(cari));                         // ELLE: 500 − 200

        // (b) Tek-cari kapatma: borç yokken → red; borç oluşunca AYNI anahtarla → yazılır.
        var k2 = Guid.NewGuid();
        var baska = await CariOlustur(sp, "Borclu");
        await kasa.PayAsync(new CashInput { CariId = baska, Tutar = 100m, Hesap = LedgerAccountType.Kasa });
        var kalem = (await kasa.GetStatementAsync(baska)).Satirlar.Single(x => x.Direction == LedgerDirection.Debit).Id;
        await Assert.ThrowsAsync<ValidationException>(() =>
            kasa.TekCariTopluKapatAsync(baska, new Dictionary<Guid, decimal?> { [kalem] = 150m }, LedgerAccountType.Kasa, islemAnahtari: k2));
        Assert.Equal(100m, await kasa.TekCariTopluKapatAsync(baska, [kalem], LedgerAccountType.Kasa, islemAnahtari: k2));
        Assert.Equal(0m, await Cari(sp, baska));
        await DengeAsync(sp);
    }
}
