using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-58 — kasa/banka virman geçmişi listelenebilirliği.
///
/// <para><b>En kritik sözleşme:</b> liste DEFTERDEN okunur, künye tablosundan DEĞİL. FAZ-50'de
/// eklenen ilk sürüm künyeden başlıyordu ve künye tablosu o fazda açıldığı için <b>ondan önceki
/// bütün virmanları sessizce gizliyordu</b>. Buradaki "künyesiz virman da listelenir" testi o
/// deliği kalıcı olarak kapatıyor.</para>
///
/// <para><b>Bağımsız oracle:</b> beklenen satır sayısı ve tutarlar elle kurulan virmanlardan
/// sayılır (ör. "2 virman, biri 1000 biri 250"), servis kodundan türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class VirmanGecmisiTests(PostgresFixture fx)
{
    private static DateTimeOffset Gun(int fark)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fark), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> HesapAsync(IServiceScope s, string kod, string ad, string tur)
        => s.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = kod, Ad = ad, Tur = tur });

    /// <summary>
    /// FAZ-50 ÖNCESİ bir virmanı taklit eder: yalnız defter satırları, künye kaydı YOK.
    /// (Gerçek eski kayıtlar tam olarak böyle duruyor.)
    /// </summary>
    private static async Task EskiVirmanYazAsync(
        TestHost host, Guid tenant, decimal tutar, DateTimeOffset tarih)
    {
        using var scope = host.ScopeFor(tenant);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var sourceId = Guid.NewGuid();
        var money = new Money(tutar, "TRY", 1m);
        db.AccountLedgerEntries.AddRange(
            new AccountLedgerEntry
            {
                EntryDateUtc = tarih, AccountType = LedgerAccountType.Banka, AccountRef = null,
                Direction = LedgerDirection.Debit, Amount = money,
                SourceType = "Virman", SourceId = sourceId, Description = "Eski virman"
            },
            new AccountLedgerEntry
            {
                EntryDateUtc = tarih, AccountType = LedgerAccountType.Kasa, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money,
                SourceType = "Virman", SourceId = sourceId, Description = "Eski virman"
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Virmanlar_hesap_adlariyla_listelenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await HesapAsync(scope, "ZR", "Ziraat TL", "Banka");
        var b = await HesapAsync(scope, "IS", "İş Bankası TL", "Banka");

        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 1000m,
            sourceAccountId: a, targetAccountId: b, receiptNo: "MK-1", branch: "Merkez");

        var satir = Assert.Single(await cash.ListCashTransfersAsync());
        // Elle kurulan değerler: Debit = hedef (İş Bankası), Credit = kaynak (Ziraat).
        Assert.Equal(a, satir.KaynakHesapId);
        Assert.Equal(b, satir.HedefHesapId);
        Assert.Equal(1000m, satir.Tutar);
        Assert.Equal("MK-1", satir.MakbuzNo);
        Assert.Equal("Merkez", satir.Sube);
        Assert.True(satir.KunyeVar);
    }

    [Fact]
    public async Task KUNYESIZ_eski_virman_da_listelenir()
    {
        // FAZ-50'nin künye-öncelikli listesi bu kaydı GÖRMÜYORDU (sessiz veri kaybı gibi davranıyordu).
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        await EskiVirmanYazAsync(host, tenant, 750m, Gun(-5));

        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();

        var satir = Assert.Single(await cash.ListCashTransfersAsync());
        Assert.False(satir.KunyeVar);
        Assert.Equal(750m, satir.Tutar);
        // Tür defterden okunur; hesap seçilmemiş (legacy) olduğu için null.
        Assert.Equal(LedgerAccountType.Kasa, satir.KaynakTur);
        Assert.Equal(LedgerAccountType.Banka, satir.HedefTur);
        Assert.Null(satir.KaynakHesapId);
        Assert.Null(satir.HedefHesapId);
        Assert.Null(satir.MakbuzNo);
    }

    [Fact]
    public async Task Kunyeli_ve_kunyesiz_virmanlar_AYNI_listede()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        await EskiVirmanYazAsync(host, tenant, 250m, Gun(-5));

        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await HesapAsync(scope, "A", "Kasa A", "Kasa");
        var b = await HesapAsync(scope, "B", "Kasa B", "Kasa");
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Kasa, 1000m,
            sourceAccountId: a, targetAccountId: b);

        // Elle: 2 virman (biri eski/künyesiz 250, biri yeni 1000).
        var satirlar = await cash.ListCashTransfersAsync();
        Assert.Equal(2, satirlar.Count);
        Assert.Equal(1250m, satirlar.Sum(x => x.Tutar));
        Assert.Single(satirlar, x => !x.KunyeVar);
    }

    [Fact]
    public async Task Tarih_hesap_ve_arama_suzgecleri_daraltir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        await EskiVirmanYazAsync(host, tenant, 250m, Gun(-30));   // pencere DIŞI

        using var scope = host.ScopeFor(tenant);
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await HesapAsync(scope, "A", "Kasa A", "Kasa");
        var b = await HesapAsync(scope, "B", "Kasa B", "Kasa");
        var c = await HesapAsync(scope, "C", "Kasa C", "Kasa");
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Kasa, 1000m,
            sourceAccountId: a, targetAccountId: b, receiptNo: "MK-AB");
        await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Kasa, 500m,
            sourceAccountId: b, targetAccountId: c, receiptNo: "MK-BC");

        // Süzgeçsiz: 3.
        Assert.Equal(3, (await cash.ListCashTransfersAsync()).Count);
        // Tarih penceresi son 7 gün: eski kayıt düşer → 2.
        Assert.Equal(2, (await cash.ListCashTransfersAsync(new KasaVirmanFilter { Bas = Gun(-7) })).Count);
        // Hesap A: yalnız A→B → 1.
        Assert.Single(await cash.ListCashTransfersAsync(new KasaVirmanFilter { HesapId = a }));
        // Hesap B kaynak VEYA hedef olduğu iki virmanda da geçer → 2.
        Assert.Equal(2, (await cash.ListCashTransfersAsync(new KasaVirmanFilter { HesapId = b })).Count);
        // Makbuz araması.
        Assert.Single(await cash.ListCashTransfersAsync(new KasaVirmanFilter { Ara = "MK-BC" }));
        // Boş süzgeç daraltmaz.
        Assert.Equal(3, (await cash.ListCashTransfersAsync(new KasaVirmanFilter { Ara = "" })).Count);
    }

    [Fact]
    public async Task Tutar_DEFTERDEN_okunur_kunye_para_tasimaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var cash = scope.ServiceProvider.GetRequiredService<CashService>();
        var a = await HesapAsync(scope, "A", "Banka A", "Banka");
        var b = await HesapAsync(scope, "B", "Banka B", "Banka");

        // Dövizli virman: kur açıkça verilir → TL karşılığı elle: 100 × 40 = 4000.
        await cash.TransferAsync(LedgerAccountType.Banka, LedgerAccountType.Banka, 100m,
            currency: "EUR", exchangeRate: 40m, sourceAccountId: a, targetAccountId: b);

        var satir = Assert.Single(await cash.ListCashTransfersAsync());
        Assert.Equal(100m, satir.Tutar);
        Assert.Equal("EUR", satir.Doviz);
        Assert.Equal(4000m, satir.TutarTl);
    }

    [Fact]
    public async Task Virman_gecmisi_tenant_izolasyonu_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            var cash = s1.ServiceProvider.GetRequiredService<CashService>();
            var a = await HesapAsync(s1, "A", "Kasa A", "Kasa");
            var b = await HesapAsync(s1, "B", "Kasa B", "Kasa");
            await cash.TransferAsync(LedgerAccountType.Kasa, LedgerAccountType.Kasa, 300m,
                sourceAccountId: a, targetAccountId: b, receiptNo: "T1-GIZLI");
        }
        await EskiVirmanYazAsync(host, t1, 400m, Gun(-2));   // künyesiz kayıt da sızmamalı

        // racar_app ile bağlanan T2 bağlamı T1'in virmanlarını GÖRMEZ.
        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<CashService>().ListCashTransfersAsync());

        // Operatör raporu göremez (ViewReports yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<RentACar.Application.Common.NoPermissionException>(
            () => op.ServiceProvider.GetRequiredService<CashService>().ListCashTransfersAsync());
    }
}
