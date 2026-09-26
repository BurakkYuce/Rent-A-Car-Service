using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Penalties;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-60 — Trafik cezası derinliği: ÇOK KALEMLİ ceza + SATIR BAZINDA kısmi ödeme (PARA).
///
/// <para><b>Bağımsız oracle:</b> beklenen değerler senaryodan ELLE türetilir (100+200+300 = 600
/// sabiti testte yazılıdır; 600 − 200 = 400 elle hesaplanmıştır) — servis/rapor kodundan
/// okunmaz.</para>
///
/// <para><b>Kilitlenen sözleşmeler:</b> (1) <c>Penalty.Tutar == Σ Kalem.Tutar</c> ve
/// <c>Penalty.OdenenTutar == Σ Kalem.Odenen == Σ Ödeme.Tutar</c> — KARARLAR.md'nin
/// "hangi satırın ne kadarı ödendi ile toplam tutarlı olmalı" şartı; (2) HER kısmi adım kendi
/// içinde dengeli defter yazar; (3) aşım ve çift-gönderim bakiyeyi DEĞİŞTİRMEZ; (4) eşzamanlı
/// iki ödeme toplamı kalemin tutarını AŞAMAZ (TOCTOU); (5) yansıtma defteri DEĞİŞMEDİ.</para>
/// </summary>
[Collection("postgres")]
public sealed class CezaDerinlikTests(PostgresFixture fx)
{
    private static async Task<List<AccountLedgerEntry>> LedgerAsync(IServiceProvider sp, string sourceType)
    {
        var f = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        return await db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceType == sourceType)
            .OrderBy(e => e.EntryDateUtc).ThenBy(e => e.Direction).ToListAsync();
    }

    /// <summary>Başlık/kalem/ödeme üçlüsünün TUTARLILIĞI — her para testinin sonunda çağrılır.</summary>
    private static async Task ConsistencyAsync(IServiceProvider sp, Guid penaltyId)
    {
        var svc = sp.GetRequiredService<PenaltyService>();
        var penalty = await svc.GetAsync(penaltyId);
        var rows = await svc.ListLinesAsync(penaltyId);
        var payments = await svc.ListPaymentsAsync(penaltyId);

        Assert.NotNull(penalty);
        Assert.Equal(penalty!.Tutar, rows.Sum(s => s.Tutar));
        Assert.Equal(penalty.OdenenTutar, rows.Sum(s => s.Odenen));
        Assert.Equal(penalty.Kalan, rows.Sum(s => s.Kalan));
        Assert.Equal(penalty.Tutar - penalty.OdenenTutar, penalty.Kalan);
        Assert.Equal(penalty.OdenenTutar, payments.Sum(o => o.Tutar));
        Assert.All(rows, s => Assert.True(s.Kalan >= 0m, "kalem kalanı negatif"));
        Assert.True(penalty.Kalan >= 0m, "ceza kalanı negatif");
        // Kalem bazında da tutarlı: o kaleme yazılan ödemelerin toplamı = kalemin Odenen'i.
        foreach (var s in rows)
            Assert.Equal(s.Odenen, payments.Where(o => o.SatirId == s.Id).Sum(o => o.Tutar));
    }

    // ---------------- çok kalemli ceza ----------------

    [Fact]
    public async Task Cok_kalemli_ceza_toplami_kalemlerden_turer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız",
            Satirlar =
            [
                new PenaltySatirInput { Tutar = 100m, Sebep = "Hız" },
                new PenaltySatirInput { Tutar = 200m, Sebep = "Park" },
                new PenaltySatirInput { Tutar = 300m, Sebep = "Emniyet kemeri" }
            ]
        });

        var p = await svc.GetAsync(id);
        Assert.Equal(600m, p!.Tutar);        // ELLE: 100 + 200 + 300
        Assert.Equal(0m, p.OdenenTutar);
        Assert.Equal(600m, p.Kalan);
        Assert.Equal(PenaltyStatus.Yeni, p.Durum);

        var rows = await svc.ListLinesAsync(id);
        Assert.Equal(3, rows.Count);
        Assert.Equal([1, 2, 3], rows.Select(s => s.Sira).ToArray());
        Assert.Equal([100m, 200m, 300m], rows.Select(s => s.Tutar).ToArray());
        Assert.All(rows, s => Assert.Equal(s.Tutar, s.Kalan));
        await ConsistencyAsync(scope.ServiceProvider, id);
    }

    [Fact]
    public async Task Kalemsiz_kayit_tek_kalem_olarak_maddelesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        // Eski (tek tutarlı) çağrı — geriye uyum: yine de 1 kalem doğar.
        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Park", Tutar = 250m, Sebep = "Yasak park" });
        var rows = await svc.ListLinesAsync(id);
        Assert.Single(rows);
        Assert.Equal(250m, rows[0].Tutar);
        Assert.Equal("Yasak park", rows[0].Sebep);
        await ConsistencyAsync(scope.ServiceProvider, id);
    }

    [Fact]
    public async Task Sifir_veya_negatif_kalem_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız",
            Satirlar = [new PenaltySatirInput { Tutar = 100m }, new PenaltySatirInput { Tutar = -50m }]
        }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new PenaltyInput { CezaTuru = "Hız", Tutar = 0m }));
    }

    // ---------------- kalem bazlı kısmi ödeme (PARA) ----------------

    [Fact]
    public async Task Kalem_bazli_kismi_odeme_kalani_dusurur_ve_HER_ADIM_dengeli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 CZ 01", Durum = VehicleStatus.Musait });
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", VehicleId = vehicle,
            Satirlar =
            [
                new PenaltySatirInput { Tutar = 100m, Sebep = "A" },
                new PenaltySatirInput { Tutar = 200m, Sebep = "B" },
                new PenaltySatirInput { Tutar = 300m, Sebep = "C" }
            ]
        });
        var rows = await svc.ListLinesAsync(id);

        // 2. kaleme (200) 200 ödeme → O KALEM kapanır, ceza kalanı 600 − 200 = 400 (ELLE).
        var s1 = await svc.PayPartialAsync(id, new CezaOdemeInput
        {
            SatirId = rows[1].Id, Tutar = 200m, Hesap = LedgerAccountType.Kasa,
            MakbuzNo = " MK-1 ", IslemYapan = "Ayşe", IslemAnahtari = Guid.NewGuid()
        });
        Assert.Equal(1, s1.Sira);
        Assert.Equal(0m, s1.SatirKalan);
        Assert.Equal(400m, s1.CezaKalan);
        Assert.Equal(PenaltyStatus.Kismi, s1.Durum);

        // 3. kaleme (300) KISMİ 120 → kalem kalanı 180 (ELLE), ceza kalanı 400 − 120 = 280 (ELLE).
        var s2 = await svc.PayPartialAsync(id, new CezaOdemeInput
        {
            SatirId = rows[2].Id, Tutar = 120m, Hesap = LedgerAccountType.Banka,
            IslemAnahtari = Guid.NewGuid()
        });
        Assert.Equal(180m, s2.SatirKalan);
        Assert.Equal(280m, s2.CezaKalan);
        Assert.Equal(PenaltyStatus.Kismi, s2.Durum);

        var p = await svc.GetAsync(id);
        Assert.Equal(320m, p!.OdenenTutar);   // ELLE: 200 + 120
        Assert.Equal(280m, p.Kalan);
        Assert.NotNull(p.OdenmeTarihi);

        // 1. kalem hiç ödenmedi — satır bazlı takibin ÖZÜ.
        var last = await svc.ListLinesAsync(id);
        Assert.Equal(0m, last[0].Odenen);
        Assert.Equal(100m, last[0].Kalan);
        Assert.Equal(200m, last[1].Odenen);
        Assert.Equal(0m, last[1].Kalan);
        Assert.Equal(120m, last[2].Odenen);
        Assert.Equal(180m, last[2].Kalan);

        // Makbuz no trim'lendi, ödeme geçmişi birbirini EZMEDİ.
        var payments = await svc.ListPaymentsAsync(id);
        Assert.Equal(2, payments.Count);
        Assert.Contains(payments, o => o.MakbuzNo == "MK-1" && o.IslemYapan == "Ayşe");

        // DEFTER: adım başına 1 borç + 1 alacak, HER ADIM kendi içinde dengeli.
        var ledgerLines = await LedgerAsync(sp, "CezaOdeme");
        Assert.Equal(4, ledgerLines.Count);
        foreach (var group in ledgerLines.GroupBy(e => e.SourceId))
        {
            var debit = group.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase);
            var credit = group.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase);
            Assert.Equal(debit, credit);
            Assert.NotEqual(0m, debit);
        }
        // 200 ve 120 AYRI AYRI postlandı (toplamları karıştırılmadı).
        Assert.Equal([120m, 200m], ledgerLines.Where(e => e.Direction == LedgerDirection.Debit)
            .Select(e => e.Amount.Amount).OrderBy(x => x).ToArray());
        // Gider ARACA atıflı; alacak tarafı Kasa ve Banka.
        Assert.All(ledgerLines.Where(e => e.AccountType == LedgerAccountType.Gider),
            e => Assert.Equal(vehicle, e.AccountRef));
        Assert.Contains(ledgerLines, e => e.AccountType == LedgerAccountType.Kasa);
        Assert.Contains(ledgerLines, e => e.AccountType == LedgerAccountType.Banka);

        // Gider raporu toplamı: ELLE 320.
        Assert.Equal(320m, (await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync()).GiderToplam);

        await ConsistencyAsync(sp, id);
    }

    [Fact]
    public async Task Tutar_verilmezse_kalemin_KALANI_odenir_ve_ceza_kapanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız",
            Satirlar = [new PenaltySatirInput { Tutar = 400m }, new PenaltySatirInput { Tutar = 100m }]
        });
        var rows = await svc.ListLinesAsync(id);

        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = rows[0].Id, Tutar = 150m });
        // Tutar null → kalemin kalanı: 400 − 150 = 250 (ELLE).
        var remainingPayment = await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = rows[0].Id });
        Assert.Equal(250m, remainingPayment.Tutar);
        Assert.Equal(0m, remainingPayment.SatirKalan);
        Assert.Equal(100m, remainingPayment.CezaKalan);   // 2. kalem hâlâ açık
        Assert.Equal(PenaltyStatus.Kismi, remainingPayment.Durum);

        var last = await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = rows[1].Id });
        Assert.Equal(100m, last.Tutar);
        Assert.Equal(0m, last.CezaKalan);
        Assert.Equal(PenaltyStatus.Odendi, last.Durum);

        await ConsistencyAsync(scope.ServiceProvider, id);
        // Kapanmış cezaya yeni ödeme YOK.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = rows[0].Id, Tutar = 1m }));
    }

    [Fact]
    public async Task Tamamini_ode_her_kaleme_AYRI_dengeli_cift_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız",
            Satirlar = [new PenaltySatirInput { Tutar = 100m }, new PenaltySatirInput { Tutar = 250m }]
        });
        Assert.True(await svc.PayAsync(id));

        var p = await svc.GetAsync(id);
        Assert.Equal(PenaltyStatus.Odendi, p!.Durum);
        Assert.Equal(350m, p.OdenenTutar);   // ELLE: 100 + 250
        Assert.Equal(0m, p.Kalan);

        var ledger = await LedgerAsync(sp, "CezaOdeme");
        Assert.Equal(4, ledger.Count);       // kalem başına 1 borç + 1 alacak
        Assert.Equal([100m, 250m], ledger.Where(e => e.Direction == LedgerDirection.Debit)
            .Select(e => e.Amount.Amount).OrderBy(x => x).ToArray());
        await ConsistencyAsync(sp, id);

        await Assert.ThrowsAsync<ValidationException>(() => svc.PayAsync(id)); // ikinci kez ödenmez
    }

    // ---------------- adversarial: aşım / çift gönderim / yarış ----------------

    [Fact]
    public async Task Asiri_odeme_reddedilir_ve_defter_YAZILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", Satirlar = [new PenaltySatirInput { Tutar = 200m }]
        });
        var row = (await svc.ListLinesAsync(id))[0];

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 250m }));
        Assert.Empty(await LedgerAsync(sp, "CezaOdeme"));
        Assert.Equal(200m, (await svc.GetAsync(id))!.Kalan);

        // Kısmi ödedikten SONRA kalanı aşan ikinci ödeme de reddedilir (120 + 100 > 200).
        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 120m });
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 100m }));
        Assert.Equal(80m, (await svc.GetAsync(id))!.Kalan);   // ELLE: 200 − 120
        Assert.Equal(2, (await LedgerAsync(sp, "CezaOdeme")).Count);
        await ConsistencyAsync(sp, id);
    }

    [Fact]
    public async Task Ayni_islem_anahtariyla_cift_gonderim_bakiyeyi_DEGISTIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", Satirlar = [new PenaltySatirInput { Tutar = 500m }]
        });
        var row = (await svc.ListLinesAsync(id))[0];
        var key = Guid.NewGuid();

        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 200m, IslemAnahtari = key });
        // Kullanıcı "geri"ye basıp AYNI formu tekrar gönderdi.
        await Assert.ThrowsAsync<DuplicateOperationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 200m, IslemAnahtari = key }));

        Assert.Equal(300m, (await svc.GetAsync(id))!.Kalan);   // ELLE: 500 − 200 (tek kez)
        Assert.Single(await svc.ListPaymentsAsync(id));
        Assert.Equal(2, (await LedgerAsync(sp, "CezaOdeme")).Count);
        await ConsistencyAsync(sp, id);
    }

    [Fact]
    public async Task Eszamanli_odemeler_kalemi_ASAMAZ_ve_kalan_negatife_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id, lineId;
        using (var scope = host.ScopeFor(tenant))
        {
            var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();
            id = await svc.CreateAsync(new PenaltyInput
            {
                CezaTuru = "Hız", Satirlar = [new PenaltySatirInput { Tutar = 300m }]
            });
            lineId = (await svc.ListLinesAsync(id))[0].Id;
        }

        // 4 paralel 100'lük ödeme. Kilitsiz "önce oku sonra yaz" olsaydı hepsi 300 kalanı görüp
        // toplam 400 yazardı (kalan −100). Danışma kilidi + aşım çiti bunu imkânsız kılar.
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            try
            {
                await s.ServiceProvider.GetRequiredService<PenaltyService>()
                    .PayPartialAsync(id, new CezaOdemeInput { SatirId = lineId, Tutar = 100m });
                return true;
            }
            catch (ValidationException) { return false; }
        }));
        var results = await Task.WhenAll(tasks);

        using var check = host.ScopeFor(tenant);
        var svc2 = check.ServiceProvider.GetRequiredService<PenaltyService>();
        var p = await svc2.GetAsync(id);
        Assert.Equal(3, results.Count(x => x));   // ELLE: 300 / 100 = tam 3 ödeme sığar
        Assert.Equal(300m, p!.OdenenTutar);
        Assert.Equal(0m, p.Kalan);
        Assert.Equal(PenaltyStatus.Odendi, p.Durum);
        Assert.Equal(3, (await svc2.ListPaymentsAsync(id)).Count);
        Assert.Equal(6, (await LedgerAsync(check.ServiceProvider, "CezaOdeme")).Count);
        await ConsistencyAsync(check.ServiceProvider, id);
    }

    [Fact]
    public async Task Eszamanli_FARKLI_kalem_odemeleri_baslik_toplamini_kaybetmez()
    {
        // KARARLAR.md FAZ-60'ın ayrıca sınanmasını istediği nokta: "hangi satırın ne kadarı
        // ödendi" ile TOPLAM tutarlılığı. Kilit ceza kapsamlı olduğu için farklı kalemlere
        // eşzamanlı ödemeler de serileşir; başlıktaki toplam hiçbir katkıyı kaybetmez.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id;
        List<Guid> lineIds;
        using (var scope = host.ScopeFor(tenant))
        {
            var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();
            id = await svc.CreateAsync(new PenaltyInput
            {
                CezaTuru = "Hız",
                Satirlar =
                [
                    new PenaltySatirInput { Tutar = 100m },
                    new PenaltySatirInput { Tutar = 100m },
                    new PenaltySatirInput { Tutar = 100m }
                ]
            });
            lineIds = (await svc.ListLinesAsync(id)).Select(s => s.Id).ToList();
        }

        var tasks = lineIds.Select(sid => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            await s.ServiceProvider.GetRequiredService<PenaltyService>()
                .PayPartialAsync(id, new CezaOdemeInput { SatirId = sid, Tutar = 100m });
        }));
        await Task.WhenAll(tasks);

        using var check = host.ScopeFor(tenant);
        var p = await check.ServiceProvider.GetRequiredService<PenaltyService>().GetAsync(id);
        Assert.Equal(300m, p!.OdenenTutar);   // ELLE: 3 × 100, hiçbiri kaybolmadı
        Assert.Equal(0m, p.Kalan);
        Assert.Equal(PenaltyStatus.Odendi, p.Durum);
        await ConsistencyAsync(check.ServiceProvider, id);
    }

    [Fact]
    public async Task Kurus_altina_yuvarlanan_odeme_reddedilir()
    {
        // Yuvarlama saldırısı: 0.00004 satır bazında 4 haneye yuvarlanınca 0 olur. Guard'sız
        // "0 TL ödeme" kaydı + dengeli-ama-sıfır defter çifti yazılabilirdi.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 100m });
        var row = (await svc.ListLinesAsync(id))[0];

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 0.00004m }));
        Assert.Empty(await LedgerAsync(sp, "CezaOdeme"));
        Assert.Equal(100m, (await svc.GetAsync(id))!.Kalan);

        // Negatif "ödeme" de reddedilir (işaret hatası ile bakiye BÜYÜTÜLEMEZ).
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = -50m }));
        Assert.Equal(100m, (await svc.GetAsync(id))!.Kalan);

        // Saçma büyüklükte kalem numeric(19,4) taşması yerine temiz redle karşılanır.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new PenaltyInput { CezaTuru = "Hız", Tutar = 5_000_000_000_000m }));
    }

    [Fact]
    public async Task Kismi_odenmis_ceza_yansitilamaz_DAVRANIS_KILIDI()
    {
        // Bilinçli sözleşme: yansıtma yalnız 'Yeni' cezada yapılır (mevcut kural). Kısmi ödeme
        // durumu Kismi'ye çektiği için ödeme SONRASI yansıtma kapanır — doğal sıra "önce
        // yansıt, sonra öde"dir. Bu test kuralı kilitler (sessizce gevşemesin).
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", CariId = Guid.NewGuid(), Tutar = 400m });
        var row = (await svc.ListLinesAsync(id))[0];
        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 100m });

        await Assert.ThrowsAsync<ValidationException>(() => svc.ReflectAsync(id));
        Assert.Equal(2, (await LedgerAsync(scope.ServiceProvider, "CezaOdeme")).Count);
        Assert.Empty(await LedgerAsync(scope.ServiceProvider, "Ceza"));
    }

    [Fact]
    public async Task Baska_cezanin_kalemine_odeme_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var a = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 100m });
        var b = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Park", Tutar = 900m });
        var bRow = (await svc.ListLinesAsync(b))[0];

        // Crafted POST: A cezası + B'nin kalemi → B'nin bakiyesi A üzerinden düşürülemez.
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(a, new CezaOdemeInput { SatirId = bRow.Id, Tutar = 50m }));
        Assert.Equal(100m, (await svc.GetAsync(a))!.Kalan);
        Assert.Equal(900m, (await svc.GetAsync(b))!.Kalan);
        Assert.Empty(await LedgerAsync(sp, "CezaOdeme"));
    }

    [Fact]
    public async Task Backfill_edilmis_ESKI_odenmis_ceza_YENIDEN_odenemez()
    {
        // Migration backfill'i eski "Odendi" cezaları Odenen=Tutar / Kalan=0 olarak işaretler,
        // ama o ödemelerin PenaltyOdeme satırı YOKTUR (o zaman böyle bir tablo yoktu). Kalan
        // yalnız ödeme satırları toplanarak hesaplansaydı kapanmış eski ceza YENİDEN ödenir ve
        // deftere ikinci kez gider yazılırdı. Bu test o senaryoyu birebir kurar.
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 500m });
        var lineId = (await svc.ListLinesAsync(id))[0].Id;

        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var s = await db.PenaltySatirlari.FirstAsync(x => x.Id == lineId);
            s.Odenen = 500m; s.Kalan = 0m;                       // backfill'in yazdığı hâl
            var c = await db.Penalties.FirstAsync(x => x.Id == id);
            c.OdenenTutar = 500m; c.Kalan = 0m; c.Durum = PenaltyStatus.Odendi;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = lineId, Tutar = 100m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.PayAsync(id));
        Assert.Empty(await LedgerAsync(sp, "CezaOdeme"));   // ikinci gider YAZILMADI
    }

    [Fact]
    public async Task Odemesi_olan_ceza_iptal_edilemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 500m });
        var row = (await svc.ListLinesAsync(id))[0];
        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 100m });

        // Defterde gider/kasa hareketi var — iptal onları ters kayıtsız görünmez kılardı.
        await Assert.ThrowsAsync<ValidationException>(() => svc.CancelAsync(id));
        Assert.Equal(PenaltyStatus.Kismi, (await svc.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Iptal_cezaya_odeme_yazilamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 500m });
        var row = (await svc.ListLinesAsync(id))[0];
        Assert.True(await svc.CancelAsync(id));

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 100m }));
        Assert.Empty(await LedgerAsync(scope.ServiceProvider, "CezaOdeme"));
    }

    [Fact]
    public async Task Gelecek_tarihli_odeme_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 500m });
        var row = (await svc.ListLinesAsync(id))[0];

        // CI-vs-lokal zaman: tam saniyeye hizalı taban.
        var t = new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.AddDays(3), DateTimeKind.Utc), TimeSpan.Zero);
        t = t.AddTicks(-(t.Ticks % TimeSpan.TicksPerSecond));

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 100m, Tarih = t }));
        Assert.Equal(500m, (await svc.GetAsync(id))!.Kalan);
    }

    [Fact]
    public async Task Odeme_yetkisi_FinanceWrite_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid id, lineId;
        using (var admin = host.ScopeFor(tenant))
        {
            var svc = admin.ServiceProvider.GetRequiredService<PenaltyService>();
            id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 500m });
            lineId = (await svc.ListLinesAsync(id))[0].Id;
        }

        // Operatör OperationsWrite taşır, FinanceWrite TAŞIMAZ → para yolu kapalı.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "operator", UserRole.Operator);
        var opSvc = op.ServiceProvider.GetRequiredService<PenaltyService>();
        await Assert.ThrowsAsync<NoPermissionException>(() =>
            opSvc.PayPartialAsync(id, new CezaOdemeInput { SatirId = lineId, Tutar = 100m }));
        await Assert.ThrowsAsync<NoPermissionException>(() => opSvc.PayAsync(id));
        Assert.Empty(await LedgerAsync(op.ServiceProvider, "CezaOdeme"));
    }

    // ---------------- izolasyon / değişmezlik ----------------

    [Fact]
    public async Task Kalem_ve_odeme_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        Guid id, lineId;

        using (var s1 = host.ScopeFor(t1))
        {
            var svc = s1.ServiceProvider.GetRequiredService<PenaltyService>();
            id = await svc.CreateAsync(new PenaltyInput
            {
                CezaTuru = "Hız", Satirlar = [new PenaltySatirInput { Tutar = 700m }]
            });
            lineId = (await svc.ListLinesAsync(id))[0].Id;
            await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = lineId, Tutar = 300m });
        }

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<PenaltyService>();
        Assert.Empty(await svc2.ListLinesAsync(id));      // racar_app + RLS: başka tenant göremez
        Assert.Empty(await svc2.ListPaymentsAsync(id));
        Assert.Empty(await svc2.ListRowsAsync());
        // Ve ödeyemez de (kalem "bulunamadı").
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc2.PayPartialAsync(id, new CezaOdemeInput { SatirId = lineId, Tutar = 100m }));

        using var back = host.ScopeFor(t1);
        Assert.Equal(400m, (await back.ServiceProvider.GetRequiredService<PenaltyService>()
            .GetAsync(id))!.Kalan);   // ELLE: 700 − 300, çapraz istek bozmadı
    }

    [Fact]
    public async Task Odeme_satiri_DB_seviyesinde_degismez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();

        var id = await svc.CreateAsync(new PenaltyInput { CezaTuru = "Hız", Tutar = 500m });
        var row = (await svc.ListLinesAsync(id))[0];
        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = row.Id, Tutar = 200m });

        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var payment = await db.PenaltyOdemeleri.FirstAsync();
        payment.Tutar = 1m;   // tahrif denemesi
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        var entry = await db.AccountLedgerEntries.FirstAsync(e => e.SourceType == "CezaOdeme");
        entry.Description = "tahrif";
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---------------- regresyon: yansıtma defteri değişmedi ----------------

    [Fact]
    public async Task Yansitma_defteri_DEGISMEDI_ve_odemeden_bagimsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var cash = sp.GetRequiredService<CashService>();
        var account = Guid.NewGuid();

        var id = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", CariId = account,
            Satirlar = [new PenaltySatirInput { Tutar = 400m }, new PenaltySatirInput { Tutar = 200m }]
        });
        Assert.True(await svc.ReflectAsync(id));

        // Yansıtma: Borç Cari 600 (ELLE: 400 + 200) / Alacak Gelir 600 — kalem sayısından bağımsız.
        Assert.Equal(600m, await cash.GetAccountBalanceAsync(account));
        var reflection = await LedgerAsync(sp, "Ceza");
        Assert.Equal(2, reflection.Count);
        Assert.Equal(600m, reflection.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
        Assert.Equal(LedgerAccountType.Gelir, reflection.Single(e => e.Direction == LedgerDirection.Credit).AccountType);

        // Kısmi ödeme yansıtma defterine DOKUNMAZ ve cari bakiyeyi DEĞİŞTİRMEZ (ayrı SourceType).
        var rows = await svc.ListLinesAsync(id);
        await svc.PayPartialAsync(id, new CezaOdemeInput { SatirId = rows[0].Id, Tutar = 400m });
        Assert.Equal(2, (await LedgerAsync(sp, "Ceza")).Count);
        Assert.Equal(600m, await cash.GetAccountBalanceAsync(account));
        Assert.Equal(2, (await LedgerAsync(sp, "CezaOdeme")).Count);

        // Gelir 600 / Gider 400 → net 200 (ELLE). Ceza yansıtması ile ödemesi ÇİFT SAYILMAZ.
        var gg = await sp.GetRequiredService<ReportService>().GetRevenueExpenseAsync();
        Assert.Equal(600m, gg.GelirToplam);
        Assert.Equal(400m, gg.GiderToplam);
        await ConsistencyAsync(sp, id);
    }

    // ---------------- filtre / kolon ----------------

    [Fact]
    public async Task Filtre_kombinasyonlari_dogru_sayida_satir_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "06 AB 123", Durum = VehicleStatus.Musait });

        var january = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var mart = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);

        var a = await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", TebligTarihi = january, VehicleId = vehicle,
            MakbuzNo = "MK-100", IslemSube = "Merkez", Tutar = 500m
        });
        await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Park", TebligTarihi = mart, MakbuzNo = "MK-200", IslemSube = "Kadıköy", Tutar = 300m
        });
        await svc.CreateAsync(new PenaltyInput { CezaTuru = "Park", TebligTarihi = mart, Tutar = 200m });

        // Toplam 3 kayıt (ELLE).
        Assert.Equal(3, (await svc.ListRowsAsync()).Count);
        // Makbuz parçası "MK-1" yalnız 1 kayıtta (ELLE).
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { MakbuzNo = "MK-1" }));
        // Plaka (boşluk duyarsız) yalnız 1 kayıtta (ELLE).
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { Plaka = "06ab123" }));
        // Şube "Kadıköy" 1 kayıt (ELLE).
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { IslemSube = "kadıköy" }));
        // Tarih aralığı: yalnız Ocak → 1 kayıt (üst sınır GÜN DAHİL).
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter
        {
            Bas = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Bit = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero)
        }));
        // Mart'takiler → 2 kayıt (ELLE).
        Assert.Equal(2, (await svc.ListRowsAsync(new PenaltyFilter
        {
            Bas = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)
        })).Count);

        // Ödeme durumu: hepsi ödenmemiş → 3; kısmi ödeme sonrası 2 ödenmemiş + 1 kısmi (ELLE).
        Assert.Equal(3, (await svc.ListRowsAsync(new PenaltyFilter { OdemeDurum = PenaltyPaymentStatus.Odenmemis })).Count);
        var aRow = (await svc.ListLinesAsync(a))[0];
        await svc.PayPartialAsync(a, new CezaOdemeInput { SatirId = aRow.Id, Tutar = 100m });
        Assert.Equal(2, (await svc.ListRowsAsync(new PenaltyFilter { OdemeDurum = PenaltyPaymentStatus.Odenmemis })).Count);
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { OdemeDurum = PenaltyPaymentStatus.Kismi }));
        Assert.Empty(await svc.ListRowsAsync(new PenaltyFilter { OdemeDurum = PenaltyPaymentStatus.Odendi }));
        // Durum süzgeci: Kismi 1 kayıt.
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { Durum = PenaltyStatus.Kismi }));
        // Birleşik filtre: Mart + Park türü ceza durumu Yeni → 2 kayıt.
        Assert.Equal(2, (await svc.ListRowsAsync(new PenaltyFilter
        {
            Durum = PenaltyStatus.Yeni,
            Bas = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)
        })).Count);
    }

    [Fact]
    public async Task Liste_satiri_bilgi_kolonlarini_cozer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PenaltyService>();
        var vehicle = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 XY 99", Durum = VehicleStatus.Musait });
        var account = await sp.GetRequiredService<RentACar.Application.Customers.CustomerService>()
            .CreateAsync(new RentACar.Application.Customers.CustomerInput
            {
                Tip = CustomerType.Bireysel, Ad = "Ali", Soyad = "Veli", Email = "ali@example.com"
            });

        await svc.CreateAsync(new PenaltyInput
        {
            CezaTuru = "Hız", VehicleId = vehicle, CariId = account, Tutar = 250m,
            Saat = "14:35", Yer = "E-5 Kartal", CepTel = "05551112233", MakbuzNo = "MK-777", IslemSube = "Merkez"
        });

        var row = (await svc.ListRowsAsync()).Single();
        Assert.Equal("34XY99", row.Plaka);   // VehicleService plakayı boşluksuz normalize eder
        // Plaka süzgeci boşluk duyarsız: kullanıcı boşluklu yazsa da bulur.
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { Plaka = "34 xy 99" }));
        Assert.Equal("Ali Veli", row.MusteriAd);
        Assert.Equal("ali@example.com", row.MusteriEposta);
        Assert.Equal("14:35", row.Ceza.Saat);
        Assert.Equal("E-5 Kartal", row.Ceza.Yer);
        Assert.Equal("05551112233", row.Ceza.CepTel);
        Assert.Equal("MK-777", row.Ceza.MakbuzNo);
        Assert.Equal("Merkez", row.Ceza.IslemSube);
        Assert.Single(row.Satirlar);
        Assert.Empty(row.Odemeler);

        // Müşteri süzgeci ad ve e-posta üzerinden de çalışır.
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { Musteri = "veli" }));
        Assert.Single(await svc.ListRowsAsync(new PenaltyFilter { Musteri = "ali@example" }));
        Assert.Empty(await svc.ListRowsAsync(new PenaltyFilter { Musteri = "bulunmayan" }));
    }
}
