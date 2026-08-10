using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-50 — hesap-bazlı (FinancialAccount) kasa/banka defteri.
///
/// <para><b>Bağımsız oracle:</b> beklenen tutarlar test içinde ELLE kurulan senaryodan gelir
/// (1000 aktarıldı → A −1000, B +1000); hiçbir beklenti servis/rapor kodundan türetilmez.</para>
///
/// <para>Kilitlenen sözleşmeler: (1) aynı türde iki hesap arası virman ARTIK mümkün, (2) kaynak==hedef
/// hâlâ yasak, (3) hesap seçilmeyen kayıtlar "hesap belirtilmemiş" kovasında AYRI kalır, (4) çift-submit
/// idempotency hem hesaplı hem hesapsız virmanda çalışır, (5) başka tenant'ın hesabı kullanılamaz.</para>
/// </summary>
[Collection("postgres")]
public sealed class HesapBazliDefterTests(PostgresFixture fx)
{
    private static Task<Guid> SeedCariAsync(IServiceScope scope, string ad)
        => scope.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = ad, Soyad = "Test" });

    private static Task<Guid> SeedHesapAsync(IServiceScope scope, string kod, string ad, string? tur)
        => scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = kod, Ad = ad, Tur = tur });

    /// <summary>Bir işlemin defter satırları (SourceType + AccountType + AccountRef + yön + baz).</summary>
    private static async Task<List<(LedgerAccountType Tur, Guid? Ref, LedgerDirection Yon, decimal Baz)>>
        SatirlarAsync(TestHost host, Guid tenant, string sourceType)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rows = await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == sourceType)
            .Select(e => new { e.AccountType, e.AccountRef, e.Direction, A = e.Amount.Amount, R = e.Amount.Rate })
            .ToListAsync();
        return [.. rows.Select(r => (r.AccountType, r.AccountRef, r.Direction, r.A * r.R))];
    }

    // ---------------------------------------------------------------- 1) Aynı türde virman

    [Fact]
    public async Task Iki_banka_hesabi_arasinda_virman_artik_yapilabilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var bankaA = await SeedHesapAsync(scope, "ZIRAAT", "Ziraat TL", "Banka");
        var bankaB = await SeedHesapAsync(scope, "ISBANK", "İş Bankası TL", "Banka");

        // ÖNCEDEN bu çağrı ValidationException atıyordu (ikisi de LedgerAccountType.Banka).
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            kaynakHesapId: bankaA, hedefHesapId: bankaB);

        var satirlar = await SatirlarAsync(host, tenant, "Virman");
        Assert.Equal(2, satirlar.Count);

        // Elle kurulan beklenti: hedef (B) BORÇ 1000, kaynak (A) ALACAK 1000.
        var borc = Assert.Single(satirlar.Where(x => x.Yon == LedgerDirection.Debit));
        var alacak = Assert.Single(satirlar.Where(x => x.Yon == LedgerDirection.Credit));
        Assert.Equal(bankaB, borc.Ref);
        Assert.Equal(1000m, borc.Baz);
        Assert.Equal(bankaA, alacak.Ref);
        Assert.Equal(1000m, alacak.Baz);

        // Defter dengesi: Σ borç(baz) == Σ alacak(baz).
        Assert.Equal(
            satirlar.Where(x => x.Yon == LedgerDirection.Debit).Sum(x => x.Baz),
            satirlar.Where(x => x.Yon == LedgerDirection.Credit).Sum(x => x.Baz));
    }

    [Fact]
    public async Task Ayni_hesap_kaynak_ve_hedefse_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var banka = await SeedHesapAsync(scope, "TEK", "Tek Banka", "Banka");

        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(
            LedgerAccountType.Banka, LedgerAccountType.Banka, 500m,
            kaynakHesapId: banka, hedefHesapId: banka));
    }

    [Fact]
    public async Task Ayni_turde_virmanda_hesap_secilmezse_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        // Hesap verilmeden Banka→Banka: eski davranış (enum eşitliği) korunur.
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(
            LedgerAccountType.Banka, LedgerAccountType.Banka, 500m));

        // Yalnız BİR taraf seçilirse de reddedilir: diğer bacak "hesap belirtilmemiş" kovasına
        // düşer ve o kovanın bakiyesi sebepsiz oynardı.
        var banka = await SeedHesapAsync(scope, "TEKYAN", "Tek Yan", "Banka");
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(
            LedgerAccountType.Banka, LedgerAccountType.Banka, 500m, kaynakHesapId: banka));
    }

    // ---------------------------------------------------------------- 2) Legacy kova

    [Fact]
    public async Task Hesapsiz_tahsilat_legacy_kovada_kalir_ve_hesapliyla_karismaz()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Kova");
        var merkez = await SeedHesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");
        var sube = await SeedHesapAsync(scope, "SB1", "Şube Kasa", "Kasa");

        // Elle kurulan senaryo: 300 merkez kasaya, 200 şube kasasına, 500 hesapsız (eski usul).
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = merkez });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 200m, Hesap = LedgerAccountType.Kasa, HesapId = sube });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa });

        var ozet = await reports.GetHesapBazliOzetAsync();
        Assert.Equal(3, ozet.Count);
        Assert.Equal(300m, Assert.Single(ozet.Where(o => o.HesapId == merkez)).Bakiye);
        Assert.Equal(200m, Assert.Single(ozet.Where(o => o.HesapId == sube)).Bakiye);
        // Legacy kova AYRI satır — 500 hiçbir gerçek hesaba yedirilmedi.
        Assert.Equal(500m, Assert.Single(ozet.Where(o => o.HesapId is null)).Bakiye);

        // Toplam eski özetle birebir aynı (regresyon: hesap ayrımı toplamı DEĞİŞTİRMEZ).
        var eski = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(1000m, eski.KasaBakiye);
        Assert.Equal(1000m, ozet.Sum(o => o.Bakiye));
    }

    [Fact]
    public async Task Defter_hesap_bazli_filtrelenir_ve_legacy_ayri_secilebilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Filtre");
        var merkez = await SeedHesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = merkez });
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 500m, Hesap = LedgerAccountType.Kasa });

        // Tümü
        Assert.Equal(2, (await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa)).Count);

        // Yalnız merkez kasa: 300 ve yürüyen bakiye SADECE görünen satırdan hesaplanır.
        var merkezSatir = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, hesapId: merkez));
        Assert.Equal(300m, merkezSatir.Borc);
        Assert.Equal(300m, merkezSatir.YuruyenBakiye);

        // Yalnız "hesap belirtilmemiş" (Guid.Empty sözleşmesi)
        var legacy = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, hesapId: Guid.Empty));
        Assert.Equal(500m, legacy.Borc);
        Assert.Null(legacy.HesapId);
    }

    // ---------------------------------------------------------------- 3) İdempotency

    [Fact]
    public async Task Ayni_anahtarla_ikinci_virman_yutulur_hesapli_ve_hesapsiz()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await SeedHesapAsync(scope, "A", "Banka A", "Banka");
        var b = await SeedHesapAsync(scope, "B", "Banka B", "Banka");

        // (a) Hesaplı, aynı tür — iki bacak da AccountType=Banka; ayrım AccountRef'ten gelir.
        var anahtar1 = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            islemAnahtari: anahtar1, kaynakHesapId: a, hedefHesapId: b);
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            islemAnahtari: anahtar1, kaynakHesapId: a, hedefHesapId: b);

        // (b) Hesapsız, farklı tür — iki bacak da AccountRef=NULL; NULLS NOT DISTINCT olmasa
        // ikinci gönderim SESSİZCE geçer ve para iki kez yazılırdı.
        var anahtar2 = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 400m, islemAnahtari: anahtar2);
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 400m, islemAnahtari: anahtar2);

        var satirlar = await SatirlarAsync(host, tenant, "Virman");
        Assert.Equal(4, satirlar.Count);   // 2 virman × 2 bacak — tekrarlar yutuldu

        // Elle beklenti: Banka bacağı = +1000 (B) −1000 (A) +400 (hedef) = +400; Kasa = −400.
        var bankaNet = satirlar.Where(x => x.Tur == LedgerAccountType.Banka)
            .Sum(x => x.Yon == LedgerDirection.Debit ? x.Baz : -x.Baz);
        var kasaNet = satirlar.Where(x => x.Tur == LedgerAccountType.Kasa)
            .Sum(x => x.Yon == LedgerDirection.Debit ? x.Baz : -x.Baz);
        Assert.Equal(400m, bankaNet);
        Assert.Equal(-400m, kasaNet);
    }

    // ---------------------------------------------------------------- 4) Doğrulama / güvenlik

    [Fact]
    public async Task Baska_tenantin_hesabi_kullanilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid yabanciHesap;
        using (var s1 = host.ScopeFor(t1))
            yabanciHesap = await SeedHesapAsync(s1, "GIZLI", "T1 Kasa", "Kasa");

        using var s2 = host.ScopeFor(t2);
        var cash = s2.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(s2, "T2");

        // Uydurma/kapsam dışı hesap kimliği sessizce null'a düşürülmez — gürültülü red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = yabanciHesap }));
        Assert.Contains("bulunamadı", ex.Message);
    }

    [Fact]
    public async Task Hesap_turu_islem_turuyle_celisirse_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var cari = await SeedCariAsync(scope, "Celiski");
        var banka = await SeedHesapAsync(scope, "ZR", "Ziraat", "Banka");

        // Banka hesabı seçilip işlem "Kasa" olarak işaretlenmiş: para yanlış kovaya yazılırdı.
        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = banka }));

        // Türü ÇÖZÜLEMEYEN serbest metin (ör. "POS") çelişki sayılmaz — yanlış-pozitif red yok.
        var pos = await SeedHesapAsync(scope, "POS1", "POS Cihazı", "POS Terminali");
        await cash.CollectAsync(new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = pos });
    }

    [Fact]
    public async Task Pasif_hesap_secilemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var hesaplar = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();
        var cari = await SeedCariAsync(scope, "Pasif");
        var id = await SeedHesapAsync(scope, "ESKI", "Kapanan Kasa", "Kasa");
        await hesaplar.UpdateAsync(id, new FinancialAccountInput { Kod = "ESKI", Ad = "Kapanan Kasa", Tur = "Kasa", Aktif = false });

        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = cari, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = id }));
    }

    // ---------------------------------------------------------------- 5) Ters kayıt + künye

    [Fact]
    public async Task Ters_kayit_ayni_hesaba_yazilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Ters");
        var merkez = await SeedHesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        var tx = await cash.CollectAsync(new CashInput
        { CariId = cari, Tutar = 750m, Hesap = LedgerAccountType.Kasa, HesapId = merkez });
        await cash.ReverseAsync(tx);

        // Para hangi kasadan girdiyse ORADAN çıkar: o hesabın bakiyesi 0'a döner, legacy kova hiç oluşmaz.
        var ozet = await reports.GetHesapBazliOzetAsync();
        Assert.Equal(0m, Assert.Single(ozet.Where(o => o.HesapId == merkez)).Bakiye);
        Assert.Empty(ozet.Where(o => o.HesapId is null));
    }

    [Fact]
    public async Task Virman_kunyesi_defterle_ayni_kimlikle_yazilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await SeedHesapAsync(scope, "A", "Banka A", "Banka");
        var b = await SeedHesapAsync(scope, "B", "Banka B", "Banka");

        var anahtar = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 250m,
            islemAnahtari: anahtar, kaynakHesapId: a, hedefHesapId: b,
            makbuzNo: "MK-42", sube: "Merkez");

        using var oku = host.ScopeFor(tenant);
        var factory = oku.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var kunye = Assert.Single(await db.KasaVirmanBilgileri.AsNoTracking().ToListAsync());

        Assert.Equal(anahtar, kunye.Id);          // defterdeki SourceId ile AYNI
        Assert.Equal("MK-42", kunye.MakbuzNo);
        Assert.Equal("Merkez", kunye.Sube);
        Assert.Equal(a, kunye.KaynakHesapId);
        Assert.Equal(b, kunye.HedefHesapId);
    }

    /// <summary>
    /// Künye tablosu tenant izolasyonu — <c>racar_app</c> ile (RLS zorunlu). Fixture uygulama
    /// bağlantısını bu rolle kurar; T1'in künyesi T2 bağlamında GÖRÜNMEZ.
    /// </summary>
    [Fact]
    public async Task Kunye_tenant_izolasyonu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var cash = s1.ServiceProvider.GetRequiredService<CashService>();
            var a = await SeedHesapAsync(s1, "A", "Banka A", "Banka");
            var b = await SeedHesapAsync(s1, "B", "Banka B", "Banka");
            await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 100m,
                kaynakHesapId: a, hedefHesapId: b, makbuzNo: "T1-GIZLI");
        }

        using var s2 = host.ScopeFor(t2);
        var factory = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.KasaVirmanBilgileri.AsNoTracking().ToListAsync());

        using var s1b = host.ScopeFor(t1);
        var f1 = s1b.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db1 = await f1.CreateDbContextAsync();
        Assert.Single(await db1.KasaVirmanBilgileri.AsNoTracking().ToListAsync());
    }

    // ---------------------------------------------------------------- 6) Diğer para yolları

    [Fact]
    public async Task Gider_odemesi_secilen_hesaba_yazilir_acik_hesapta_yazilmaz()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var expenses = scope.ServiceProvider.GetRequiredService<RentACar.Application.Expenses.ExpenseService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Tedarikci");
        var kasa = await SeedHesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // Nakit gider 100 (KDV yok) → Merkez Kasa'dan çıkar.
        await expenses.CreateAsync(new RentACar.Application.Expenses.ExpenseInput
        {
            NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.Nakit, FinansalHesapId = kasa
        });
        // Açık hesap gideri 500: para kasadan ÇIKMAZ → hesap seçilse bile nakit bacağı yok.
        await expenses.CreateAsync(new RentACar.Application.Expenses.ExpenseInput
        {
            NetTutar = 500m, KdvOrani = 0m, OdemeYontemi = OdemeYontemi.AcikHesap,
            CariId = cari, FinansalHesapId = kasa
        });

        var ozet = await reports.GetHesapBazliOzetAsync();
        Assert.Equal(-100m, Assert.Single(ozet.Where(o => o.HesapId == kasa)).Bakiye);
        Assert.Empty(ozet.Where(o => o.HesapId is null));   // açık hesap legacy kovaya da düşmez
    }

    [Fact]
    public async Task Depozito_al_ve_iade_secilen_hesaptan_gecer()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var depozito = scope.ServiceProvider.GetRequiredService<DepozitoService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var cari = await SeedCariAsync(scope, "Depozito");
        var kasa = await SeedHesapAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await depozito.AlAsync(cari, 1000m, LedgerAccountType.Kasa, hesapId: kasa);
        await depozito.IadeAsync(cari, 400m, LedgerAccountType.Kasa, hesapId: kasa);

        // Elle beklenti: +1000 − 400 = 600 o kasada.
        var ozet = await reports.GetHesapBazliOzetAsync();
        Assert.Equal(600m, Assert.Single(ozet.Where(o => o.HesapId == kasa)).Bakiye);
    }
}
