using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.RentalAddOns;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// ADVERSARIAL DENETİM PROBE'LARI — K1/K2/O1/O2 para düzeltmelerini ÇÜRÜTME girişimi (canlı PostgreSQL).
/// Beklenen değerler elle kurulmuş senaryodan (bağımsız oracle), servis kodundan DEĞİL.
/// "BULGU" işaretli assert'ler MEVCUT YANLIŞ davranışı ampirik BELGELER (bulgu kanıtı) —
/// düzeltme yapılınca bu assert'ler güncellenmelidir.
/// </summary>
[Collection("postgres")]
public sealed class DenetimParaProbe(PostgresFixture fx)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    // ---------- ortak kurulum ----------

    /// <summary>
    /// TEST İZOLASYONU: dövizli kirada <c>KurService</c> bir kur bulmak ZORUNDA. Kur zinciri
    /// "tenant sabit kuru → TCMB" olduğu için TENANT-OWNED sabit kur yazılır (kod ISO'ya indirgenerek); paylaşımlı ulusal
    /// <c>KurKayitlari</c> tablosuna DOKUNULMAZ (o tablo tenant'lar arası ortaktır — satır eklemek
    /// başka testlerin kur beklentisini bozardı).
    ///
    /// <para><b>Neden gerekti:</b> bu sınıf EUR kurunun tabloda hazır olmasına güveniyordu, o satırı
    /// aslında BAŞKA bir test sınıfı yazıyordu. Tam suite yeşil, tek başına koşunca kırmızıydı.
    /// V6a zaten bu sabit-kur yolunu elle kuruyordu; kural ortak kuruluma alındı.</para>
    ///
    /// <para>Kurun DEĞERİ beklentileri etkilemez — tahsilat/ödeme uçları kendi açık kurlarını
    /// taşır; sabit kur yalnız "kur var mı" kapısını açar.</para>
    /// </summary>
    private static async Task<(IServiceProvider sp, Guid rentalId, Guid cariId)> Seed(
        IServiceScope scope, string plate, string? currency, int day = 3)
    {
        var sp = scope.ServiceProvider;
        // Kod NormalizeKod ile ISO'ya indirgenir (EURO→EUR, DOLAR→USD, TL/boş→TRY) — hangi döviz
        // etiketiyle çağrılırsa çağrılsın doğru koda sabit kur yazılsın diye. TRY baz para, kur istemez.
        var isoCode = RentACar.Application.Kur.ExchangeRateService.NormalizeCode(currency);
        if (isoCode != "TRY" && isoCode.Length == 3)
            await sp.GetRequiredService<FixedExchangeRateService>()
                .UpsertAsync(new SabitKurInput { Kod = isoCode, Kur = 40m });
        var account = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Probe-" + plate });
        var veh = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = plate, Durum = VehicleStatus.Musait });
        var id = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = account, VehicleId = veh, BasTar = Start, BitTar = Start.AddDays(day),
            GunlukUcret = 100m, KmLimit = 0, FazlaKmUcret = 0m, Doviz = currency
        });
        return (sp, id, account);
    }

    private static CashInput Ci(Guid account, Guid? rental, decimal amount, string currency, decimal exchangeRate) => new()
    { CariId = account, RentalId = rental, Tutar = amount, Doviz = currency, Kur = exchangeRate, Hesap = LedgerAccountType.Kasa };

    private static IDbContextFactory<AppDbContext> Factory(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    // =====================================================================================
    // VEKTÖR 1 — Uzat + GEÇ DÖNÜŞ kombinasyonu. Oracle: 3g kirala, +2g uzat (baz 5×100=500),
    // uzatılmış bitişten 1 gün geç dön → geç bedel 1×100 → GenelToplam 600; fatura 600; 600
    // tahsilatla Bakiye 0.
    // =====================================================================================
    [Fact]
    public async Task V1_Uzat_sonra_gec_donus_600_fatura_600_bakiye_0()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 01", null);
        var svc = sp.GetRequiredService<RentalService>();

        Assert.True(await svc.DeliverAsync(id, 1000, 8));
        Assert.True(await svc.ExtendAsync(id, Start.AddDays(5)));      // planlı +2 gün
        Assert.True(await svc.ReturnAsync(id, 1000, 8, Start.AddDays(6))); // 1 gün GEÇ

        var c = await svc.GetAsync(id);
        Assert.Equal(500m, c!.Tutar);         // baz: 5 × 100 (planlı uzatma bazda)
        Assert.Equal(1, c.UzatmaGun);         // yalnız geç dönüş
        Assert.Equal(100m, c.UzatmaBedeli);   // 1 × 100
        Assert.Equal(600m, c.GenelToplam);    // ORACLE: 6 gün fiili kullanım × 100

        var invoices = sp.GetRequiredService<InvoiceService>();
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(id));
        Assert.Equal(600m, inv!.GenelToplam); // fatura da 600 (çift sayım yok)

        await sp.GetRequiredService<CashService>().CollectAsync(Ci(account, id, 600m, "TRY", 1m));
        var c2 = await svc.GetAsync(id);
        Assert.Equal(600m, c2!.Tahsilat);
        Assert.Equal(0m, c2.Bakiye);
    }

    // =====================================================================================
    // VEKTÖR 2 — Uzat + ek hizmet + geç dönüş sıralamaları. Oracle her iki sırada da:
    // baz 500 + geç 100 + ek hizmet brüt 120 = 720.
    // =====================================================================================
    [Fact]
    public async Task V2a_Uzat_addon_gec_donus_720()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, _) = await Seed(scope, "34 DP 02", null);
        var svc = sp.GetRequiredService<RentalService>();

        Assert.True(await svc.DeliverAsync(id, 1000, 8));
        Assert.True(await svc.ExtendAsync(id, Start.AddDays(5)));      // baz 500

        var definition = await sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "KLT", Ad = "Koltuk", BirimUcret = 100m, KdvOrani = 0.20m });
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(id, definition, 1m); // +120 brüt
        Assert.Equal(620m, (await svc.GetAsync(id))!.GenelToplam);

        Assert.True(await svc.ReturnAsync(id, 1000, 8, Start.AddDays(6))); // 1 gün geç → +100
        var c = await svc.GetAsync(id);
        Assert.Equal(720m, c!.GenelToplam);   // ORACLE: 500 + 100 + 120 (addon korunur)
        Assert.Equal(100m, c.UzatmaBedeli);
    }

    [Fact]
    public async Task V2b_Addon_uzat_gec_donus_720()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, _) = await Seed(scope, "34 DP 03", null);
        var svc = sp.GetRequiredService<RentalService>();

        Assert.True(await svc.DeliverAsync(id, 1000, 8));
        var definition = await sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "KLT", Ad = "Koltuk", BirimUcret = 100m, KdvOrani = 0.20m });
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(id, definition, 1m); // 300+120=420
        Assert.True(await svc.ExtendAsync(id, Start.AddDays(5)));                    // +200 → 620
        Assert.Equal(620m, (await svc.GetAsync(id))!.GenelToplam);

        Assert.True(await svc.ReturnAsync(id, 1000, 8, Start.AddDays(6)));           // +100
        Assert.Equal(720m, (await svc.GetAsync(id))!.GenelToplam); // ORACLE: sıra bağımsız 720
    }

    // =====================================================================================
    // VEKTÖR 3a — Atomik SQL: iki ARDIŞIK tahsilatta Bakiye SET formülü ("GenelToplam" −
    // ("Tahsilat"+delta)) yeni-Tahsilat ile tutarlı mı? Oracle: 300 kira; 100 + 150 → 250/50.
    // =====================================================================================
    [Fact]
    public async Task V3a_Iki_ardisik_tahsilat_bakiye_tutarli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 04", null);
        var cash = sp.GetRequiredService<CashService>();

        await cash.CollectAsync(Ci(account, id, 100m, "TRY", 1m));
        var a = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(100m, a!.Tahsilat);
        Assert.Equal(200m, a.Bakiye);

        await cash.CollectAsync(Ci(account, id, 150m, "TRY", 1m));
        var b = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(250m, b!.Tahsilat); // eski Tahsilat SQL içinde okunur → 100+150
        Assert.Equal(50m, b.Bakiye);     // 300 − 250
    }

    // =====================================================================================
    // VEKTÖR 3b — Ters kaydı ÇİFT gönder (repo seviyesi, EŞZAMANLI → servis ön-kontrolü atlanır;
    // kısmi unique index son savunma). Oracle: 200 tahsilat; ters kayıt yalnız 1 kez işler →
    // Tahsilat 0 (−200 DEĞİL); kaybeden transaction'ın Rentals UPDATE'i de geri alınmış olmalı.
    // =====================================================================================
    [Fact]
    public async Task V3b_Cift_ters_kayit_tahsilat_bir_kez_geri_alinir()
    {
        var tenant = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);
        Guid id, account, txId;
        using (var s0 = host.ScopeFor(tenant))
        {
            (var sp, id, account) = await Seed(s0, "34 DP 05", null);
            txId = await sp.GetRequiredService<CashService>().CollectAsync(Ci(account, id, 200m, "TRY", 1m));
            Assert.Equal(200m, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.Tahsilat);
        }

        using var s1 = host.ScopeFor(tenant);
        using var s2 = host.ScopeFor(tenant);
        var orig = await s1.ServiceProvider.GetRequiredService<CashService>().GetAsync(txId);

        static (CashTransaction rev, List<AccountLedgerEntry> entries) Craft(CashTransaction o)
        {
            var rev = new CashTransaction
            {
                Tip = o.Tip, CariId = o.CariId, RentalId = o.RentalId, Tarih = DateTimeOffset.UtcNow,
                Amount = o.Amount, KarsiHesap = o.KarsiHesap, Aciklama = "probe ters",
                TersKayitMi = true, TersAlinanId = o.Id
            };
            var entries = new List<AccountLedgerEntry>
            {
                new() { EntryDateUtc = rev.Tarih, AccountType = o.KarsiHesap, AccountRef = null,
                    Direction = LedgerDirection.Credit, Amount = o.Amount, SourceType = "TersKayit", SourceId = rev.Id, Description = "probe" },
                new() { EntryDateUtc = rev.Tarih, AccountType = LedgerAccountType.Cari, AccountRef = o.CariId,
                    Direction = LedgerDirection.Debit, Amount = o.Amount, SourceType = "TersKayit", SourceId = rev.Id, Description = "probe" }
            };
            return (rev, entries);
        }

        async Task<Exception?> Post(IServiceScope s)
        {
            try
            {
                var (rev, entries) = Craft(orig!);
                await s.ServiceProvider.GetRequiredService<ICashRepository>().PostAsync(rev, entries);
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        var results = await Task.WhenAll(Post(s1), Post(s2)); // eşzamanlı çift gönderim
        Assert.Equal(1, results.Count(r => r is null));                   // tam 1 başarı
        Assert.Equal(1, results.Count(r => r is ValidationException));    // tam 1 idempotent red

        using var s3 = host.ScopeFor(tenant);
        var c = await s3.ServiceProvider.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(0m, c!.Tahsilat);  // yalnız BİR kez geri alındı (−200 olsaydı çift işlerdi)
        Assert.Equal(300m, c.Bakiye);
        Assert.Equal(0m, await s3.ServiceProvider.GetRequiredService<CashService>().GetAccountBalanceAsync(account));
    }

    // =====================================================================================
    // VEKTÖR 3c — RLS: başka tenant'ın rental'ına RentalId verilirse satır DOKUNULMAZ kalmalı.
    // (a) app yolu: tahsilat postlanır ama A'nın kirası değişmez; (b) ham SQL: RLS 0 satır etkiler.
    // =====================================================================================
    [Fact]
    public async Task V3c_Cross_tenant_rental_dokunulmaz()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var host = new TestHost(fx.AppConnectionString);

        Guid rentalA;
        using (var sa = host.ScopeFor(tenantA))
            (_, rentalA, _) = await Seed(sa, "34 DP 06", null);

        using (var sb = host.ScopeFor(tenantB))
        {
            var spB = sb.ServiceProvider;
            var accountB = await spB.GetRequiredService<CustomerService>()
                .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "B-cari" });

            // (a) B, A'nın rental id'siyle tahsilat: sessizce rental'sız işler (mevcut sözleşme).
            var txId = await spB.GetRequiredService<CashService>().CollectAsync(Ci(accountB, rentalA, 100m, "TRY", 1m));
            Assert.NotNull(await spB.GetRequiredService<CashService>().GetAsync(txId));

            // (b) ham SQL ile doğrudan UPDATE denemesi — RLS 0 satır etkilemeli.
            await using var dbB = await Factory(sb).CreateDbContextAsync();
            var affected = await dbB.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Rentals\" SET \"Tahsilat\" = 999 WHERE \"Id\" = {rentalA}");
            Assert.Equal(0, affected);
        }

        using var sa2 = host.ScopeFor(tenantA);
        var c = await sa2.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rentalA);
        Assert.Equal(0m, c!.Tahsilat);  // A'nın kirası el değmemiş
        Assert.Equal(300m, c.Bakiye);
    }

    // =====================================================================================
    // VEKTÖR 4 — NormalizeKod kenarları.
    // =====================================================================================
    [Fact]
    public async Task V4a_M1_FIX_Kira_USD_tahsilat_DOLAR_alias_normalize_edilip_islenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 07", "USD"); // 300 USD
        // M1 FIX: Money kurulurken NormalizeKodStrict → "DOLAR" ≡ USD (3 harf) → varchar(3) sığar, tahsilat işler.
        await sp.GetRequiredService<CashService>().CollectAsync(Ci(account, id, 100m, "DOLAR", 35m));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(100m, c!.Tahsilat); // kira dövizinde (USD)
        Assert.Equal(200m, c.Bakiye);    // 300 − 100
    }

    [Fact]
    public async Task V4a2_M1_FIX_Kira_EURO_tahsilat_ayni_etiket_EURO_islenir_gecersiz_kod_temiz_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 21", "EURO"); // rental formu "EURO" saklar
        // M1 FIX: "EURO" → EUR normalize edilir; K2'nin dayattığı doğal senaryo artık 500 vermez.
        await sp.GetRequiredService<CashService>().CollectAsync(Ci(account, id, 300m, "EURO", 40m));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(300m, c!.Tahsilat);
        Assert.Equal(0m, c.Bakiye);
        // Tanınmayan uzun etiket → 22001/500 DEĞİL, temiz ValidationException.
        await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<CashService>().CollectAsync(Ci(account, id, 10m, "KRONER", 5m)));
    }

    [Fact]
    public async Task V4b_Kira_doviz_null_TRY_sayilir_EUR_tahsilat_TL_baz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 08", null); // Doviz=null → TRY, 300 TL
        await sp.GetRequiredService<CashService>().CollectAsync(Ci(account, id, 5m, "EUR", 40m)); // 200 TL-baz
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(200m, c!.Tahsilat);
        Assert.Equal(100m, c.Bakiye);
    }

    [Fact]
    public async Task V4c_Kira_GBP_farkli_kurlu_iki_tahsilat_ham_toplam()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        // O5 (KurSnapshot): FX kira oluşturma kur ister → GBP sabit kuru seed (snapshot raporlama-amaçlı;
        // bu testin ham-toplam invariant'ını etkilemez).
        await scope.ServiceProvider.GetRequiredService<FixedExchangeRateService>()
            .UpsertAsync(new SabitKurInput { Kod = "GBP", Kur = 48m, Aktif = true });
        var (sp, id, account) = await Seed(scope, "34 DP 09", "GBP"); // 300 GBP
        var cash = sp.GetRequiredService<CashService>();
        await cash.CollectAsync(Ci(account, id, 100m, "GBP", 47m));
        await cash.CollectAsync(Ci(account, id, 100m, "GBP", 50m)); // farklı kur → ham toplam etkilenmez
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(200m, c!.Tahsilat);  // kur bağımsız 100+100 GBP
        Assert.Equal(100m, c.Bakiye);
        Assert.Equal(-9700m, await cash.GetAccountBalanceAsync(account)); // defter TL-baz: 4700+5000
    }

    // =====================================================================================
    // VEKTÖR 5 — Batch yolu.
    // =====================================================================================
    [Fact]
    public async Task V5a_Batch_ayni_kiraya_iki_satir_delta_iki_kez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 10", null);
        await sp.GetRequiredService<CashService>().BatchCollectAsync(
            [Ci(account, id, 100m, "TRY", 1m), Ci(account, id, 150m, "TRY", 1m)]);
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(250m, c!.Tahsilat); // aynı tx içinde iki UPDATE üst üste biner
        Assert.Equal(50m, c.Bakiye);
    }

    [Fact]
    public async Task V5b_Batch_FX_yanlis_doviz_tum_batch_geri_ve_no_bosluksuz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, idTry, accountT) = await Seed(scope, "34 DP 11", null);
        var spB = scope.ServiceProvider;
        var (_, idEur, accountE) = await Seed(scope, "34 DP 12", "EURO");
        var cash = spB.GetRequiredService<CashService>();
        var customerNo = await spB.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "NoTabani" }); // numaralandırma carisi

        var t1 = await cash.CollectAsync(Ci(customerNo, null, 10m, "TRY", 1m)); // No tabanı
        var no1 = (await cash.GetAsync(t1))!.No;
        DocumentNoOracle.OneOfExpected(5, 1, no1);

        // Satır 1 geçerli (TRY kira), satır 2 FX kiraya TRY tahsilat → repo K2 guard'ı satır 2'de patlar.
        await Assert.ThrowsAsync<ValidationException>(() => cash.BatchCollectAsync(
            [Ci(accountT, idTry, 100m, "TRY", 1m), Ci(accountE, idEur, 500m, "TRY", 1m)]));

        var cT = await spB.GetRequiredService<RentalService>().GetAsync(idTry);
        Assert.Equal(0m, cT!.Tahsilat);  // satır 1'in deltası da geri alındı (atomiklik)
        Assert.Equal(0m, await cash.GetAccountBalanceAsync(accountT)); // defter de yok

        await using (var db = await Factory(scope).CreateDbContextAsync())
            Assert.Equal(1, await db.CashTransactions.CountAsync()); // yalnız t1

        var t2 = await cash.CollectAsync(Ci(customerNo, null, 10m, "TRY", 1m));
        DocumentNoOracle.OneOfExpected(5, 2, (await cash.GetAsync(t2))!.No); // sıra boşluksuz (rollback no'yu iade etti)
    }

    // =====================================================================================
    // VEKTÖR 6 — Ödeme (iade) yönü FX kirada.
    // =====================================================================================
    [Fact]
    public async Task V6a_FX_odeme_tahsilati_azaltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 13", "EURO"); // 300 EUR
        var cash = sp.GetRequiredService<CashService>();
        await cash.CollectAsync(Ci(account, id, 300m, "EUR", 40m));
        await cash.PayAsync(Ci(account, id, 100m, "EUR", 40m)); // iade
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(200m, c!.Tahsilat); // 300 − 100
        Assert.Equal(100m, c.Bakiye);
    }

    [Fact]
    public async Task V6b_FX_odeme_negatif_tahsilat_sinirsiz_ve_tersi_geri_getirir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 14", "EURO");
        var cash = sp.GetRequiredService<CashService>();

        // SINIR: hiç tahsilat yokken 100 EUR "iade" — taban/floor yok, Tahsilat negatife düşer
        // (önceki davranışla aynı; regresyon değil — Low not).
        var payId = await cash.PayAsync(Ci(account, id, 100m, "EUR", 40m));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(-100m, c!.Tahsilat);
        Assert.Equal(400m, c.Bakiye);

        await cash.ReverseAsync(payId); // ödemenin tersi: yon = (−1)×(−1) = +1
        var c2 = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(0m, c2!.Tahsilat);
        Assert.Equal(300m, c2.Bakiye);
    }

    // =====================================================================================
    // VEKTÖR 7 — O2 kapsam kaçağı: guard yalnız ADD yolunda. ESKİ VERİ (guard öncesi eklenmiş
    // FX-kira addon'u) hâlâ faturalanabiliyor ve TL kalem kira dövizi sanılıp ×Kur çarpılıyor.
    // =====================================================================================
    [Fact]
    public async Task V7a_M3_FIX_Legacy_FX_addon_fatura_temiz_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 15", "EURO"); // 300 EUR kira
        await sp.GetRequiredService<FixedExchangeRateService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m });

        // Guard-öncesi veriyi simüle et: addon doğrudan DB'ye (repo guard'ı yoktu) — 100 TL net + 20 KDV.
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            db.RentalAddOns.Add(new RentalAddOn
            {
                RentalId = id, EkHizmetTanimId = Guid.NewGuid(), Ad = "Legacy Koltuk (TL)",
                Miktar = 1m, BirimNetFiyat = 100m, KdvOrani = 0.20m,
                NetTutar = 100m, KdvTutar = 20m, Toplam = 120m
            });
            await db.SaveChangesAsync();
        }

        // M3 FIX (ikinci savunma): legacy FX+addon kira FATURALANAMAZ (birim karışması yerine temiz red);
        // addon kaldırılınca (V7c) faturalama serbest.
        var invoices = sp.GetRequiredService<InvoiceService>();
        await Assert.ThrowsAsync<ValidationException>(() => invoices.CreateFromRentalAsync(id));
        Assert.Equal(0m, await sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account)); // hiçbir şey postlanmadı
    }

    [Fact]
    public async Task V7b_FX_kira_addon_override_yolu_da_red()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, _) = await Seed(scope, "34 DP 16", "EURO");
        var definition = await sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "GPS", Ad = "GPS", BirimUcret = 50m, KdvOrani = 0.20m });
        // Override fiyat yolu da aynı repo AddAsync'inden geçer → guard çalışmalı.
        await Assert.ThrowsAsync<ValidationException>(() =>
            sp.GetRequiredService<RentalAddOnService>().AddAsync(id, definition, 1m, unitNetOverride: 75m));
    }

    [Fact]
    public async Task V7c_Legacy_FX_addon_remove_serbest_ve_iyilesir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, _) = await Seed(scope, "34 DP 17", "EURO"); // 300 EUR
        Guid addOnId;
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var a = new RentalAddOn
            {
                RentalId = id, EkHizmetTanimId = Guid.NewGuid(), Ad = "Legacy",
                Miktar = 1m, BirimNetFiyat = 100m, KdvOrani = 0.20m,
                NetTutar = 100m, KdvTutar = 20m, Toplam = 120m
            };
            db.RentalAddOns.Add(a);
            await db.SaveChangesAsync();
            addOnId = a.Id;
        }
        // REMOVE yolu FX kirada açık (bilinçli: temizlik mümkün olsun) → Recompute karışıklığı GİDERİR.
        Assert.True(await sp.GetRequiredService<RentalAddOnService>().RemoveAsync(addOnId));
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(300m, c!.GenelToplam); // saf 300 EUR'a döndü
    }

    // =====================================================================================
    // VEKTÖR K1-RETRO — ESKİ KODLA uzatılmış (Tutar VE UzatmaBedeli'ne birlikte yazılmış),
    // hâlâ Kirada duran sözleşme: kod düzeltildi ama VERİ ONARIMI YOK → dönüş-öncesi fatura
    // hâlâ çift sayar.
    // =====================================================================================
    [Fact]
    public async Task V8_M2_FIX_Retro_onarim_SQLi_eski_uzatma_verisini_duzeltir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, _) = await Seed(scope, "34 DP 18", null); // 3g × 100 = 300 TL

        // ESKİ ExtendAsync'in bıraktığı durumu simüle et (+2 gün): Tutar VE UzatmaBedeli birlikte.
        await using (var db = await Factory(scope).CreateDbContextAsync())
        {
            var r = await db.Rentals.FirstAsync(x => x.Id == id);
            r.Gun = 5; r.Tutar = 500m;                 // eski kod: Tutar += 200
            r.UzatmaGun = 2; r.UzatmaBedeli = 200m;    // eski kod: UzatmaBedeli += 200 (çift yazım)
            r.GenelToplam = 500m; r.Bakiye = 500m;     // eski kod GenelToplam'ı tek artırırdı
            await db.SaveChangesAsync();

            // M2 FIX: DenetimParaOnarimi migration'ının onarım SQL'i (fixture DB'si migration'ları veriden
            // ÖNCE koştuğu için aynı ifade burada uygulanır — semantik birebir doğrulanır).
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Rentals\" SET \"UzatmaGun\" = 0, \"UzatmaBedeli\" = 0 " +
                "WHERE \"Durum\" = 0 AND \"UzatmaBedeli\" > 0;");
        }

        var invoices = sp.GetRequiredService<InvoiceService>();
        var inv = await invoices.GetAsync(await invoices.CreateFromRentalAsync(id));

        // ORACLE: 5 gün × 100 = 500 — onarım sonrası çift sayım YOK (eski bug: 700 keserdi).
        Assert.Equal(500m, inv!.GenelToplam);
    }

    // =====================================================================================
    // VEKTÖR 8 — TRY regresyon zinciri + çapraz akış (tahsilat → addon Recompute → tahsilat).
    // =====================================================================================
    [Fact]
    public async Task V9_TRY_tahsilat_odeme_ters_zinciri_eski_davranis()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 19", "TL"); // 300 TL
        var cash = sp.GetRequiredService<CashService>();

        await cash.CollectAsync(Ci(account, id, 200m, "TRY", 1m));            // +200
        var payId = await cash.PayAsync(Ci(account, id, 50m, "TRY", 1m));     // −50
        await cash.ReverseAsync(payId);                                    // +50
        var c = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(200m, c!.Tahsilat);
        Assert.Equal(100m, c.Bakiye);

        // Servis seviyesinde ikinci ters kayıt ön-kontrolle reddedilir (F1.4: mükerrer tipi).
        await Assert.ThrowsAsync<DuplicateOperationException>(() => cash.ReverseAsync(payId));
        Assert.Equal(200m, (await sp.GetRequiredService<RentalService>().GetAsync(id))!.Tahsilat);
    }

    [Fact]
    public async Task V10_Tahsilat_addon_tahsilat_bakiye_tutarli()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var (sp, id, account) = await Seed(scope, "34 DP 20", null); // 300 TL
        var cash = sp.GetRequiredService<CashService>();

        await cash.CollectAsync(Ci(account, id, 100m, "TRY", 1m)); // Tahsilat 100, Bakiye 200
        var definition = await sp.GetRequiredService<AddOnDefinitionService>().CreateAsync(
            new EkHizmetTanimInput { Kod = "BS", Ad = "Bebek koltuğu", BirimUcret = 100m, KdvOrani = 0.20m });
        await sp.GetRequiredService<RentalAddOnService>().AddAsync(id, definition, 1m); // GenelToplam 420
        var a = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(420m, a!.GenelToplam);
        Assert.Equal(320m, a.Bakiye);       // Recompute: 420 − 100

        await cash.CollectAsync(Ci(account, id, 100m, "TRY", 1m)); // atomik SQL taze GenelToplam okur
        var b = await sp.GetRequiredService<RentalService>().GetAsync(id);
        Assert.Equal(200m, b!.Tahsilat);
        Assert.Equal(220m, b.Bakiye);       // 420 − 200
    }
}
