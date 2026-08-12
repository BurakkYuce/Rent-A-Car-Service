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
    private static DateTimeOffset Gun(int fark)
    {
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(fark), DateTimeKind.Utc), TimeSpan.Zero);
        return t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));
    }

    private static Task<Guid> CariAsync(IServiceScope s, string ad)
        => s.ServiceProvider.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = ad });

    /// <summary>Açık hesap gideri: 1000 net + %20 KDV = 1200 borç.</summary>
    private static async Task<Guid> AcikHesapGiderAsync(IServiceScope s, Guid cari, decimal net = 1000m)
    {
        await s.ServiceProvider.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = net, KdvOrani = 0.20m,
            OdemeYontemi = OdemeYontemi.AcikHesap, CariId = cari
        });
        var liste = await s.ServiceProvider.GetRequiredService<ExpenseService>().ListAsync();
        return liste.OrderByDescending(x => x.CreatedAtUtc).First().Id;
    }

    private static async Task<(int Satir, decimal Borc, decimal Alacak)> DefterAsync(
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
        var kasa = await scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa" });

        var odemeTar = Gun(-2);
        var rentalId = Guid.NewGuid();   // gevşek referans (FK yok) — bilgi
        await expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = 100m, KdvOrani = 0m,
            OdemeYontemi = OdemeYontemi.Nakit, FinansalHesapId = kasa,
            OdemeTarihi = odemeTar, HazirAciklama = "Yakıt", RentalId = rentalId
        });

        var gider = Assert.Single(await expenses.ListAsync());
        Assert.Equal(odemeTar, gider.OdemeTarihi);
        Assert.Equal("Yakıt", gider.HazirAciklama);
        Assert.Equal(rentalId, gider.RentalId);

        // Defter: elle 100 Gider Borç / 100 Kasa Alacak — bilgi alanları hiçbir satır üretmedi.
        var (satir, borc, alacak) = await DefterAsync(host, tenant);
        Assert.Equal(2, satir);
        Assert.Equal(100m, borc);
        Assert.Equal(100m, alacak);
    }

    // ---------------------------------------------------------------- Kısmi ödeme

    [Fact]
    public async Task Kismi_odeme_kalani_dusurur_ve_DEFTERE_YAZMAZ()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(tenant);
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cari = await CariAsync(scope, "Tedarikçi");
        var giderId = await AcikHesapGiderAsync(scope, cari);

        var defterOnce = await DefterAsync(host, tenant);

        // ELLE: 1200 borca 400 ödeme → kalan 800.
        await expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 400m });

        var durum = (await expenses.OdemeDurumlariAsync(await expenses.ListAsync()))[giderId];
        Assert.True(durum.TakipEdilir);
        Assert.Equal(1200m, durum.GenelToplam);
        Assert.Equal(400m, durum.Odenen);
        Assert.Equal(800m, durum.Kalan);
        Assert.False(durum.TamamenOdendi);

        // KRİTİK: defter BİT-BİREBİR aynı (karar (i) — ödeme takip alanıdır).
        Assert.Equal(defterOnce, await DefterAsync(host, tenant));

        // İkinci ödeme kalanı kapatır.
        await expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 800m });
        var durum2 = (await expenses.OdemeDurumlariAsync(await expenses.ListAsync()))[giderId];
        Assert.Equal(0m, durum2.Kalan);
        Assert.True(durum2.TamamenOdendi);
        Assert.Equal(defterOnce, await DefterAsync(host, tenant));
    }

    [Fact]
    public async Task Tutar_verilmezse_kalanin_tamami_odenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cari = await CariAsync(scope, "Tek Tık");
        var giderId = await AcikHesapGiderAsync(scope, cari);

        await expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 200m });
        await expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId });   // kalanın tamamı

        var durum = (await expenses.OdemeDurumlariAsync(await expenses.ListAsync()))[giderId];
        Assert.Equal(1200m, durum.Odenen);
        Assert.Equal(0m, durum.Kalan);
    }

    [Fact]
    public async Task Nakit_banka_giderinde_takip_YOK_borc_da_yok()
    {
        // Para kayıt anında kasadan çıktı (defter öyle yazıldı) → "kalan" kavramı yok.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var kasa = await scope.ServiceProvider.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa" });
        await expenses.CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Genel, NetTutar = 500m, KdvOrani = 0m,
            OdemeYontemi = OdemeYontemi.Nakit, FinansalHesapId = kasa
        });
        var gider = Assert.Single(await expenses.ListAsync());

        var durum = (await expenses.OdemeDurumlariAsync([gider]))[gider.Id];
        Assert.False(durum.TakipEdilir);
        Assert.Equal(500m, durum.Odenen);
        Assert.Equal(0m, durum.Kalan);

        // Nakit gidere ödeme kaydı GÜRÜLTÜLÜ reddedilir — sessizce kabul etmek sahte borç yaratırdı.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = gider.Id, Tutar = 100m }));
        Assert.Contains("açık hesap", ex.Message);
    }

    // ---------------------------------------------------------------- Adversarial kilitler

    [Fact]
    public async Task Asiri_odeme_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cari = await CariAsync(scope, "Asiri");
        var giderId = await AcikHesapGiderAsync(scope, cari);

        // Tek seferde aşırı.
        await Assert.ThrowsAsync<ValidationException>(() =>
            expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 1200.01m }));

        // Kısmi ödemeden SONRA aşırı (kalan 200 iken 300).
        await expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 1000m });
        await Assert.ThrowsAsync<ValidationException>(() =>
            expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 300m }));

        var durum = (await expenses.OdemeDurumlariAsync(await expenses.ListAsync()))[giderId];
        Assert.Equal(1000m, durum.Odenen);   // reddedilen ödeme hiç yazılmadı
    }

    [Fact]
    public async Task Cift_submit_yutulur_kalan_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var expenses = scope.ServiceProvider.GetRequiredService<ExpenseService>();
        var cari = await CariAsync(scope, "CiftSubmit");
        var giderId = await AcikHesapGiderAsync(scope, cari);

        var anahtar = Guid.NewGuid();
        var ilk = await expenses.OdemeEkleAsync(new GiderOdemeInput
        { ExpenseId = giderId, Tutar = 300m, IslemAnahtari = anahtar });
        var ikinci = await expenses.OdemeEkleAsync(new GiderOdemeInput
        { ExpenseId = giderId, Tutar = 300m, IslemAnahtari = anahtar });

        Assert.NotNull(ilk);
        Assert.Null(ikinci);   // yutuldu
        var durum = (await expenses.OdemeDurumlariAsync(await expenses.ListAsync()))[giderId];
        Assert.Equal(300m, durum.Odenen);   // 600 DEĞİL
    }

    [Fact]
    public async Task Escanzamanli_odemeler_kalani_asamaz()
    {
        // TOCTOU: kilitsiz "önce oku sonra yaz" olsaydı üç istek birlikte geçip 900 yazardı.
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid giderId;
        using (var scope = host.ScopeFor(tenant))
        {
            var cari = await CariAsync(scope, "Yaris");
            giderId = await AcikHesapGiderAsync(scope, cari, net: 250m);   // 300 borç
        }

        // 4 paralel × 100 → tam 3'ü geçmeli (300), dördüncü reddedilmeli.
        var gorevler = Enumerable.Range(0, 4).Select(async _ =>
        {
            using var s = host.ScopeFor(tenant);
            var svc = s.ServiceProvider.GetRequiredService<ExpenseService>();
            try { await svc.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 100m }); return true; }
            catch (ValidationException) { return false; }
        }).ToList();
        var sonuclar = await Task.WhenAll(gorevler);

        using var oku = host.ScopeFor(tenant);
        var expenses = oku.ServiceProvider.GetRequiredService<ExpenseService>();
        var durum = (await expenses.OdemeDurumlariAsync(await expenses.ListAsync()))[giderId];
        Assert.Equal(300m, durum.Odenen);
        Assert.Equal(0m, durum.Kalan);
        Assert.Equal(3, sonuclar.Count(x => x));
    }

    [Fact]
    public async Task Odeme_kaydi_DB_seviyesinde_degismez()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid giderId;
        using (var scope = host.ScopeFor(tenant))
        {
            var cari = await CariAsync(scope, "Degismez");
            giderId = await AcikHesapGiderAsync(scope, cari);
            await scope.ServiceProvider.GetRequiredService<ExpenseService>()
                .OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 100m });
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
        var cari = await CariAsync(scope, "Gelecek");
        var giderId = await AcikHesapGiderAsync(scope, cari);

        await Assert.ThrowsAsync<ValidationException>(() => expenses.OdemeEkleAsync(
            new GiderOdemeInput { ExpenseId = giderId, Tutar = 100m, Tarih = Gun(5) }));
    }

    [Fact]
    public async Task Odeme_tenant_izolasyonu_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        Guid giderId;
        using (var s1 = host.ScopeFor(t1))
        {
            var cari = await CariAsync(s1, "T1 Tedarikçi");
            giderId = await AcikHesapGiderAsync(s1, cari);
        }

        // Başka tenant'ın gider kimliğiyle ödeme: bulunamaz.
        using (var s2 = host.ScopeFor(t2))
        {
            var svc = s2.ServiceProvider.GetRequiredService<ExpenseService>();
            await Assert.ThrowsAsync<ValidationException>(() =>
                svc.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 100m }));
            Assert.Empty(await svc.ListAsync());
        }

        // Operatör ödeme yapamaz (FinanceWrite yok).
        using var op = host.ScopeFor(t1, role: UserRole.Operator);
        await Assert.ThrowsAsync<ValidationException>(() => op.ServiceProvider
            .GetRequiredService<ExpenseService>()
            .OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 100m }));
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
        var cari = await CariAsync(scope, "Rapor");
        var giderId = await AcikHesapGiderAsync(scope, cari);

        var ggOnce = await reports.GetGelirGiderAsync();
        var kbOnce = await reports.GetKasaBankaSummaryAsync();

        await expenses.OdemeEkleAsync(new GiderOdemeInput { ExpenseId = giderId, Tutar = 700m });

        var ggSonra = await reports.GetGelirGiderAsync();
        var kbSonra = await reports.GetKasaBankaSummaryAsync();
        Assert.Equal(ggOnce.GiderToplam, ggSonra.GiderToplam);
        Assert.Equal(ggOnce.NetKar, ggSonra.NetKar);
        Assert.Equal(kbOnce.KasaBakiye, kbSonra.KasaBakiye);
        Assert.Equal(kbOnce.BankaBakiye, kbSonra.BankaBakiye);
        // Elle: gider 1000 net (KDV indirilecek, gider toplamına girmez).
        Assert.Equal(1000m, ggSonra.GiderToplam);
    }
}
