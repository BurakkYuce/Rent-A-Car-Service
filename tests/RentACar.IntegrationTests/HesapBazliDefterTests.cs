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
    private static Task<Guid> SeedCustomerAsync(IServiceScope scope, string name)
        => scope.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = name, Soyad = "Test" });

    private static Task<Guid> SeedAccountAsync(IServiceScope scope, string code, string name, string? type)
        => scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = code, Ad = name, Tur = type });

    /// <summary>Bir işlemin defter satırları (SourceType + AccountType + AccountRef + yön + baz).</summary>
    private static async Task<List<(LedgerAccountType Tur, Guid? Ref, LedgerDirection Yon, decimal Baz)>>
        RowsAsync(TestHost host, Guid tenant, string sourceType)
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

        var bankA = await SeedAccountAsync(scope, "ZIRAAT", "Ziraat TL", "Banka");
        var bankB = await SeedAccountAsync(scope, "ISBANK", "İş Bankası TL", "Banka");

        // ÖNCEDEN bu çağrı ValidationException atıyordu (ikisi de LedgerAccountType.Banka).
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            sourceAccountId: bankA, targetAccountId: bankB);

        var rows = await RowsAsync(host, tenant, "Virman");
        Assert.Equal(2, rows.Count);

        // Elle kurulan beklenti: hedef (B) BORÇ 1000, kaynak (A) ALACAK 1000.
        var debit = Assert.Single(rows.Where(x => x.Yon == LedgerDirection.Debit));
        var credit = Assert.Single(rows.Where(x => x.Yon == LedgerDirection.Credit));
        Assert.Equal(bankB, debit.Ref);
        Assert.Equal(1000m, debit.Baz);
        Assert.Equal(bankA, credit.Ref);
        Assert.Equal(1000m, credit.Baz);

        // Defter dengesi: Σ borç(baz) == Σ alacak(baz).
        Assert.Equal(
            rows.Where(x => x.Yon == LedgerDirection.Debit).Sum(x => x.Baz),
            rows.Where(x => x.Yon == LedgerDirection.Credit).Sum(x => x.Baz));
    }

    [Fact]
    public async Task Ayni_hesap_kaynak_ve_hedefse_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var bank = await SeedAccountAsync(scope, "TEK", "Tek Banka", "Banka");

        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(
            LedgerAccountType.Banka, LedgerAccountType.Banka, 500m,
            sourceAccountId: bank, targetAccountId: bank));
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
        var bank = await SeedAccountAsync(scope, "TEKYAN", "Tek Yan", "Banka");
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(
            LedgerAccountType.Banka, LedgerAccountType.Banka, 500m, sourceAccountId: bank));
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
        var account = await SeedCustomerAsync(scope, "Kova");
        var headOffice = await SeedAccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");
        var branch = await SeedAccountAsync(scope, "SB1", "Şube Kasa", "Kasa");

        // Elle kurulan senaryo: 300 merkez kasaya, 200 şube kasasına, 500 hesapsız (eski usul).
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = headOffice });
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 200m, Hesap = LedgerAccountType.Kasa, HesapId = branch });
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 500m, Hesap = LedgerAccountType.Kasa });

        var summary = await reports.GetAccountBasedSummaryAsync();
        Assert.Equal(3, summary.Count);
        Assert.Equal(300m, Assert.Single(summary.Where(o => o.HesapId == headOffice)).Bakiye);
        Assert.Equal(200m, Assert.Single(summary.Where(o => o.HesapId == branch)).Bakiye);
        // Legacy kova AYRI satır — 500 hiçbir gerçek hesaba yedirilmedi.
        Assert.Equal(500m, Assert.Single(summary.Where(o => o.HesapId is null)).Bakiye);

        // Toplam eski özetle birebir aynı (regresyon: hesap ayrımı toplamı DEĞİŞTİRMEZ).
        var old = await reports.GetCashBankSummaryAsync();
        Assert.Equal(1000m, old.KasaBakiye);
        Assert.Equal(1000m, summary.Sum(o => o.Bakiye));
    }

    [Fact]
    public async Task Defter_hesap_bazli_filtrelenir_ve_legacy_ayri_secilebilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await SeedCustomerAsync(scope, "Filtre");
        var headOffice = await SeedAccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 300m, Hesap = LedgerAccountType.Kasa, HesapId = headOffice });
        await cash.CollectAsync(new CashInput { CariId = account, Tutar = 500m, Hesap = LedgerAccountType.Kasa });

        // Tümü
        Assert.Equal(2, (await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa)).Count);

        // Yalnız merkez kasa: 300 ve yürüyen bakiye SADECE görünen satırdan hesaplanır.
        var headOfficeRow = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, accountId: headOffice));
        Assert.Equal(300m, headOfficeRow.Borc);
        Assert.Equal(300m, headOfficeRow.YuruyenBakiye);

        // Yalnız "hesap belirtilmemiş" (Guid.Empty sözleşmesi)
        var legacy = Assert.Single(await reports.GetAccountLedgerAsync(LedgerAccountType.Kasa, accountId: Guid.Empty));
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
        var a = await SeedAccountAsync(scope, "A", "Banka A", "Banka");
        var b = await SeedAccountAsync(scope, "B", "Banka B", "Banka");

        // (a) Hesaplı, aynı tür — iki bacak da AccountType=Banka; ayrım AccountRef'ten gelir.
        var key1 = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            operationKey: key1, sourceAccountId: a, targetAccountId: b);
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            operationKey: key1, sourceAccountId: a, targetAccountId: b);

        // (b) Hesapsız, farklı tür — iki bacak da AccountRef=NULL; NULLS NOT DISTINCT olmasa
        // ikinci gönderim SESSİZCE geçer ve para iki kez yazılırdı.
        var key2 = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 400m, operationKey: key2);
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 400m, operationKey: key2);

        var rows = await RowsAsync(host, tenant, "Virman");
        Assert.Equal(4, rows.Count);   // 2 virman × 2 bacak — tekrarlar yutuldu

        // Elle beklenti: Banka bacağı = +1000 (B) −1000 (A) +400 (hedef) = +400; Kasa = −400.
        var bankNet = rows.Where(x => x.Tur == LedgerAccountType.Banka)
            .Sum(x => x.Yon == LedgerDirection.Debit ? x.Baz : -x.Baz);
        var cashNet = rows.Where(x => x.Tur == LedgerAccountType.Kasa)
            .Sum(x => x.Yon == LedgerDirection.Debit ? x.Baz : -x.Baz);
        Assert.Equal(400m, bankNet);
        Assert.Equal(-400m, cashNet);
    }

    // ---------------------------------------------------------------- 4) Doğrulama / güvenlik

    [Fact]
    public async Task Baska_tenantin_hesabi_kullanilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid foreignAccount;
        using (var s1 = host.ScopeFor(t1))
            foreignAccount = await SeedAccountAsync(s1, "GIZLI", "T1 Kasa", "Kasa");

        using var s2 = host.ScopeFor(t2);
        var cash = s2.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(s2, "T2");

        // Uydurma/kapsam dışı hesap kimliği sessizce null'a düşürülmez — gürültülü red.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = foreignAccount }));
        Assert.Contains("bulunamadı", ex.Message);
    }

    [Fact]
    public async Task Hesap_turu_islem_turuyle_celisirse_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "Celiski");
        var bank = await SeedAccountAsync(scope, "ZR", "Ziraat", "Banka");

        // Banka hesabı seçilip işlem "Kasa" olarak işaretlenmiş: para yanlış kovaya yazılırdı.
        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = bank }));

        // ADVERSARIAL L1 — "Banka Kasası" GERÇEKTE bir kasadır; salt ön ek bakışı onu Banka sanıp
        // meşru işlemi reddediyordu. Artık "…kasa/kasası" ile biten metin Kasa'ya çözülür.
        var bankCash = await SeedAccountAsync(scope, "BK", "Şube Kasası", "Banka Kasası");
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = bankCash });
    }

    /// <summary>
    /// ADVERSARIAL H1 — türü ÇÖZÜLEMEYEN hesap kullanılamaz. Önce yok sayılıyordu ve AYNI hesap
    /// hem Kasa hem Banka bacağında kullanılıp bakiyesi İKİYE bölünüyordu; birleşik bakiyeyi
    /// hiçbir ekran göstermiyordu.
    /// </summary>
    [Fact]
    public async Task Turu_belirsiz_hesap_kullanilamaz_ve_tanimda_zorunlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var accounts = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();

        // Tanım: tür olmadan/çözülemeyen türle hesap AÇILAMAZ.
        await Assert.ThrowsAsync<ValidationException>(() => accounts.CreateAsync(
            new FinancialAccountInput { Kod = "TURSUZ", Ad = "Türsüz" }));
        await Assert.ThrowsAsync<ValidationException>(() => accounts.CreateAsync(
            new FinancialAccountInput { Kod = "POS1", Ad = "POS Cihazı", Tur = "POS Terminali" }));

        // Kullanım: geçmişten kalmış türsüz bir kayıt DOĞRUDAN yazılsa bile işlem reddedilir.
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "Belirsiz");
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        Guid legacyId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var legacy = new FinancialAccount { Kod = "ESKI", Ad = "Eski Hesap", Tur = "POS" };
            db.FinancialAccounts.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
        }
        var ex = await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = legacyId }));
        Assert.Contains("türü belirsiz", ex.Message);
    }

    /// <summary>ADVERSARIAL M4 — hesabın dövizi ile işlem dövizi çelişkisi reddedilir.</summary>
    [Fact]
    public async Task Hesap_dovizi_celiskisi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var account = await SeedCustomerAsync(scope, "Doviz");
        var usd = await scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "ZRUSD", Ad = "Ziraat USD", Tur = "Banka", Doviz = "USD" });

        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 1000m, Doviz = "TRY", Hesap = LedgerAccountType.Banka, HesapId = usd }));

        // Dövizi TANIMSIZ hesapta karışmayız (eski kayıtlar).
        var free = await scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "SRB", Ad = "Serbest", Tur = "Banka" });
        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 1000m, Doviz = "TRY", Hesap = LedgerAccountType.Banka, HesapId = free });
    }

    /// <summary>ADVERSARIAL M1 — kural türden BAĞIMSIZ: bir taraf seçildiyse diğeri de zorunlu.</summary>
    [Fact]
    public async Task Farkli_turde_virmanda_da_tek_taraf_hesap_secilemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var bank = await SeedAccountAsync(scope, "BNK", "Banka", "Banka");

        // Kasa → Banka, yalnız hedef seçili: paranın diğer ucu legacy kovaya düşerdi.
        await Assert.ThrowsAsync<ValidationException>(() => cash.TransferAsync(
            LedgerAccountType.Kasa, LedgerAccountType.Banka, 1000m, targetAccountId: bank));

        // İkisi de seçilmezse eski davranış korunur (legacy virman hâlâ mümkün).
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Banka, 1000m);
    }

    /// <summary>ADVERSARIAL M2 — defter hareketi olan hesap SİLİNEMEZ (bakiye yetim kalırdı).</summary>
    [Fact]
    public async Task Gecmisi_olan_hesap_silinemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var accounts = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();
        var account = await SeedCustomerAsync(scope, "Silme");
        var cashAccount = await SeedAccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // Hareketsiz hesap silinebilir.
        var empty = await SeedAccountAsync(scope, "BOS", "Boş Kasa", "Kasa");
        Assert.True(await accounts.DeleteAsync(empty));

        await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 500m, Hesap = LedgerAccountType.Kasa, HesapId = cashAccount });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => accounts.DeleteAsync(cashAccount));
        Assert.Contains("defter hareketi var", ex.Message);
    }

    /// <summary>ADVERSARIAL M7 — künye salt-yazılır değil: makbuz no / şube geri okunabiliyor.</summary>
    [Fact]
    public async Task Virman_kunyesi_geri_okunabiliyor()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await SeedAccountAsync(scope, "A", "Banka A", "Banka");
        var b = await SeedAccountAsync(scope, "B", "Banka B", "Banka");

        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1500m,
            sourceAccountId: a, targetAccountId: b, receiptNo: "MK-77", branch: "Kadıköy");

        var row = Assert.Single(await cash.ListCashTransfersAsync());
        Assert.Equal("MK-77", row.MakbuzNo);
        Assert.Equal("Kadıköy", row.Sube);
        Assert.Equal(a, row.KaynakHesapId);
        Assert.Equal(b, row.HedefHesapId);
        // Tutar DEFTERDEN gelir (künye para taşımaz) — elle: 1500.
        Assert.Equal(1500m, row.Tutar);
    }

    [Fact]
    public async Task Pasif_hesap_secilemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var accounts = scope.ServiceProvider.GetRequiredService<FinancialAccountService>();
        var account = await SeedCustomerAsync(scope, "Pasif");
        var id = await SeedAccountAsync(scope, "ESKI", "Kapanan Kasa", "Kasa");
        await accounts.UpdateAsync(id, new FinancialAccountInput { Kod = "ESKI", Ad = "Kapanan Kasa", Tur = "Kasa", Aktif = false });

        await Assert.ThrowsAsync<ValidationException>(() => cash.CollectAsync(
            new CashInput { CariId = account, Tutar = 100m, Hesap = LedgerAccountType.Kasa, HesapId = id }));
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
        var account = await SeedCustomerAsync(scope, "Ters");
        var headOffice = await SeedAccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        var tx = await cash.CollectAsync(new CashInput
        { CariId = account, Tutar = 750m, Hesap = LedgerAccountType.Kasa, HesapId = headOffice });
        await cash.ReverseAsync(tx);

        // Para hangi kasadan girdiyse ORADAN çıkar: o hesabın bakiyesi 0'a döner, legacy kova hiç oluşmaz.
        var summary = await reports.GetAccountBasedSummaryAsync();
        Assert.Equal(0m, Assert.Single(summary.Where(o => o.HesapId == headOffice)).Bakiye);
        Assert.Empty(summary.Where(o => o.HesapId is null));
    }

    [Fact]
    public async Task Virman_kunyesi_defterle_ayni_kimlikle_yazilir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await SeedAccountAsync(scope, "A", "Banka A", "Banka");
        var b = await SeedAccountAsync(scope, "B", "Banka B", "Banka");

        var key = Guid.NewGuid();
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 250m,
            operationKey: key, sourceAccountId: a, targetAccountId: b,
            receiptNo: "MK-42", branch: "Merkez");

        using var read = host.ScopeFor(tenant);
        var factory = read.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var profile = Assert.Single(await db.KasaVirmanBilgileri.AsNoTracking().ToListAsync());

        Assert.Equal(key, profile.Id);          // defterdeki SourceId ile AYNI
        Assert.Equal("MK-42", profile.MakbuzNo);
        Assert.Equal("Merkez", profile.Sube);
        Assert.Equal(a, profile.KaynakHesapId);
        Assert.Equal(b, profile.HedefHesapId);
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
            var a = await SeedAccountAsync(s1, "A", "Banka A", "Banka");
            var b = await SeedAccountAsync(s1, "B", "Banka B", "Banka");
            await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 100m,
                sourceAccountId: a, targetAccountId: b, receiptNo: "T1-GIZLI");
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
        var account = await SeedCustomerAsync(scope, "Tedarikci");
        var cash = await SeedAccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        // Nakit gider 100 (KDV yok) → Merkez Kasa'dan çıkar.
        await expenses.CreateAsync(new RentACar.Application.Expenses.ExpenseInput
        {
            NetTutar = 100m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, FinansalHesapId = cash
        });
        // Açık hesap gideri 500: para kasadan ÇIKMAZ → hesap seçilse bile nakit bacağı yok.
        await expenses.CreateAsync(new RentACar.Application.Expenses.ExpenseInput
        {
            NetTutar = 500m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.AcikHesap,
            CariId = account, FinansalHesapId = cash
        });

        var summary = await reports.GetAccountBasedSummaryAsync();
        Assert.Equal(-100m, Assert.Single(summary.Where(o => o.HesapId == cash)).Bakiye);
        Assert.Empty(summary.Where(o => o.HesapId is null));   // açık hesap legacy kovaya da düşmez
    }

    [Fact]
    public async Task Depozito_al_ve_iade_secilen_hesaptan_gecer()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var deposit = scope.ServiceProvider.GetRequiredService<DepositService>();
        var reports = scope.ServiceProvider.GetRequiredService<ReportService>();
        var account = await SeedCustomerAsync(scope, "Depozito");
        var cash = await SeedAccountAsync(scope, "MRK", "Merkez Kasa", "Kasa");

        await deposit.GetAsync(account, 1000m, LedgerAccountType.Kasa, accountId: cash);
        await deposit.RefundAsync(account, 400m, LedgerAccountType.Kasa, accountId: cash);

        // Elle beklenti: +1000 − 400 = 600 o kasada.
        var summary = await reports.GetAccountBasedSummaryAsync();
        Assert.Equal(600m, Assert.Single(summary.Where(o => o.HesapId == cash)).Bakiye);
    }
}
