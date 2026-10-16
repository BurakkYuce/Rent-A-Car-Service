using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.EInvoicing;
using RentACar.Application.Finance;
using RentACar.Application.Integrations;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR #394 bağımsız adversarial incelemesinin KALICI testleri (probe'lardan çevrildi). M1: iade edilmiş fatura GİB'e
/// gitmez; M2: entegratörün TEST ortamı kabulü "gönderildi" sayılmaz; M3: gönderilmiş e-Arşiv iadesiz iptal edilmez.
/// Kontroller: üç fatura yolunda atomiklik (C1), paralel gönderim/iptal/kuyruk yükü (C2), çok oranlı + dövizli UBL (C3).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> tutarlar elle (1.000 × %20 = 200 → 1.200); beklenen mesajlar sabit cümleler (servis
/// sabitlerinden okunmaz); "kaç kez / hangi ortamda gönderildi" entegratör çiftinin kendi kaydından.</para>
/// </summary>
[Collection("postgres")]
public sealed class EDocumentAdversarialTests(PostgresFixture fx)
{
    private const string RefundedSourceCancelled = "Fatura iade edildi; e-belge gönderilmeden iptal edildi.";
    private const string RefundedSourceNotSent = "Bu fatura iade edildi; e-belgesi gönderilmez (defterde satış geri alındı).";
    private const string TestEnvironmentSent = "Entegratörün TEST ortamına gönderildi; GİB'e ulaşmadı. Canlı ortama geçince yeniden gönderin.";

    private sealed class Ctx(TestHost host, FakeEInvoiceIntegrator fake, Guid tenant) : IDisposable
    {
        public TestHost Host { get; } = host;
        public FakeEInvoiceIntegrator Fake { get; } = fake;
        public Guid Tenant { get; } = tenant;
        public IServiceScope Scope(UserRole role = UserRole.Admin) => Host.ScopeFor(Tenant, Guid.NewGuid(), role: role);
        public void Dispose() => Host.Dispose();
    }

    private Ctx New(bool adapter = true)
    {
        var fake = new FakeEInvoiceIntegrator();
        var host = new TestHost(fx.AppConnectionString, s => { if (adapter) s.AddSingleton<IEInvoiceIntegrator>(fake); });
        return new Ctx(host, fake, Guid.NewGuid());
    }

    private static TenantSettingsModel Settings(string? secret, bool live) => new()
    {
        FirmaUnvan = "Deneme Oto Kiralama A.Ş.", FirmaVergiNo = "1234567890", FirmaVergiDairesi = "Kadıköy",
        FirmaAdres = "Moda Cad. No:1", FirmaIl = "İstanbul", FirmaIlce = "Kadıköy",
        EFaturaEntegrator = FakeEInvoiceIntegrator.TestKey, EFaturaKullanici = "entegrator-kullanici", EFaturaSifre = secret,
        EFaturaCanliOrtam = live,
    };

    private static async Task ConnectAsync(Ctx c, bool live = true)
    {
        using var s = c.Scope();
        await s.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(Settings("S" + Guid.NewGuid().ToString("N"), live));
    }

    private static async Task<Guid> IndividualAsync(Ctx c)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Yılmaz", TcKimlik = TestNationalId.New(), Adres = "Gül Sk. 3", Il = "İzmir", Ilce = "Konak",
        });
    }

    private static async Task<Guid> InvoiceAsync(Ctx c, Guid customer, decimal net = 1000m)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        {
            CariId = customer, NetTutar = net, KdvOrani = 0.20m, Aciklama = "Araç kiralama hizmeti",
        });
    }

    private static async Task<Guid> RefundAsync(Ctx c, Guid source)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateRefundAsync(source);
    }

    private static async Task<EDocumentDispatcher.Sonuc> DispatchAsync(Ctx c, DateTimeOffset now)
    {
        using var s = c.Scope();
        var sp = s.ServiceProvider;
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await EDocumentDispatcher.RunAsync(db, c.Tenant, now, sp.GetRequiredService<EInvoiceIntegratorCatalog>(),
            sp.GetRequiredService<ISecretProtector>(), NullLogger.Instance);
    }

    private static async Task<T> DbAsync<T>(Ctx c, Func<AppDbContext, Task<T>> read)
    {
        using var s = c.Scope();
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await read(db);
    }

    private static Task<EDocument> DocAsync(Ctx c, Guid invoice)
        => DbAsync(c, db => db.EDocuments.AsNoTracking().SingleAsync(d => d.InvoiceId == invoice));

    /// <summary>KDV hesabının net bakiyesi (Alacak − Borç). Oracle: fatura +200, iade −200.</summary>
    private static Task<decimal> NetVatAsync(Ctx c)
        => DbAsync(c, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Kdv)
            .SumAsync(e => e.Direction == LedgerDirection.Credit ? e.Amount.Amount : -e.Amount.Amount));

    // ===================================================================== M1 — iade edilmiş fatura gönderilmez

    [Fact]
    public async Task M1_refund_before_dispatch_cancels_the_unsent_source_edocument_in_the_same_transaction()
    {
        using var c = New();
        await ConnectAsync(c);
        var source = await InvoiceAsync(c, await IndividualAsync(c));
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, source)).Durum);
        Assert.Equal(200m, await NetVatAsync(c));

        await RefundAsync(c, source);
        Assert.Equal(0m, await NetVatAsync(c)); // defterde geri alındı
        var doc = await DocAsync(c, source);
        Assert.Equal((EDocumentStatus.Iptal, RefundedSourceCancelled), (doc.Durum, doc.SonMesaj));

        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(c, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Empty(c.Fake.SendCalls);
    }

    [Fact]
    public async Task M1_refunded_invoice_issued_while_not_connected_cannot_be_queued_after_connecting()
    {
        using var c = New();
        var source = await InvoiceAsync(c, await IndividualAsync(c)); // bağlı değil → Taslak
        await RefundAsync(c, source);
        Assert.Equal(EDocumentStatus.Iptal, (await DocAsync(c, source)).Durum);
        await ConnectAsync(c);

        using (var s = c.Scope())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(source));
            Assert.Equal(RefundedSourceNotSent, ex.Message);
        }
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        Assert.Empty(c.Fake.SendCalls);
    }

    [Fact]
    public async Task M1_refund_is_refused_while_the_dispatcher_holds_the_source_and_nothing_is_written()
    {
        using var c = New();
        await ConnectAsync(c);
        var source = await InvoiceAsync(c, await IndividualAsync(c));
        // Gönderim işi satırı tutuyor (kilit gelecekte): belge o an entegratöre gidiyor olabilir.
        await DbAsync(c, db => db.EDocuments.Where(d => d.InvoiceId == source)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.KilitBitisUtc, DateTimeOffset.UtcNow.AddMinutes(5))));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => RefundAsync(c, source));
        Assert.Equal("Kaynak faturanın e-belgesi şu anda gönderiliyor; birkaç dakika sonra yeniden deneyin.", ex.Message);
        Assert.Equal(1, await DbAsync(c, db => db.Invoices.AsNoTracking().CountAsync())); // iade yazılmadı
        Assert.Equal(200m, await NetVatAsync(c));                                         // defter aynı
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, source)).Durum);
    }

    /// <summary>
    /// Önceki deneme sonucu belirsizken (geçici hata) iade: kayıt KUYRUKTA bırakılır (belge entegratöre ulaşmış
    /// olabilir). Sonraki koşu önce entegratöre sorar: bulunamadı → iade kontrolü belgeyi iptal eder, YENİDEN GÖNDERMEZ.
    /// </summary>
    [Fact]
    public async Task M1_uncertain_previous_attempt_is_asked_first_and_a_refunded_invoice_is_never_resent()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.OnSend = _ => new EInvoiceSendResult(EInvoiceOutcome.GeciciHata, null, null, "Servis geçici olarak kapalı");
        var source = await InvoiceAsync(c, await IndividualAsync(c));
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0);
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, source)).Durum);

        await RefundAsync(c, source);
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, source)).Durum); // belirsiz → dokunulmadı
        c.Fake.OnSend = null;

        await DispatchAsync(c, t0.AddHours(1));
        var doc = await DocAsync(c, source);
        Assert.Equal((EDocumentStatus.Iptal, RefundedSourceCancelled), (doc.Durum, doc.SonMesaj));
        Assert.Single(c.Fake.SendCalls);   // yalnız ilk (başarısız) deneme
        Assert.Single(c.Fake.StatusCalls); // ikinci koşu önce sordu
        Assert.Empty(c.Fake.Accepted);
    }

    // ===================================================================== M2 — test ortamı

    [Fact]
    public async Task M2_test_environment_acceptance_is_not_sent_and_is_resent_to_live_after_switching()
    {
        using var c = New();
        await ConnectAsync(c, live: false);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        Assert.Equal(new EDocumentDispatcher.Sonuc(1, 0, 0, 0), await DispatchAsync(c, DateTimeOffset.UtcNow));
        var doc = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.TestGonderildi, TestEnvironmentSent), (doc.Durum, doc.SonMesaj));
        Assert.False(c.Fake.AcceptedLive(doc.Ettn));

        using (var s = c.Scope()) // canlıya geç (sır boş = mevcut korunur)
            await s.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(Settings(null, live: true));
        using (var s = c.Scope())
            Assert.Equal(EDocumentStatus.Gonderilecek, (await s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(id)).Durum);
        var requeued = await DocAsync(c, id);
        Assert.Null(requeued.GonderimUtc); // test kabulünün izi silindi

        Assert.Equal(new EDocumentDispatcher.Sonuc(1, 0, 0, 0), await DispatchAsync(c, DateTimeOffset.UtcNow.AddMinutes(1)));
        var live = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderildi, live.Durum);
        Assert.True(c.Fake.AcceptedLive(live.Ettn));
        Assert.Equal(2, c.Fake.SendCalls.Count); // test + canlı, aynı ETTN
        Assert.All(c.Fake.SendCalls, x => Assert.Equal(doc.Ettn, x.Ettn));
        Assert.Single(c.Fake.StatusCalls);       // canlıya göndermeden önce soruldu (bulunamadı)
    }

    [Fact]
    public async Task M2_test_sent_document_is_cancelled_locally_and_refund_cancels_it_too()
    {
        using var c = New();
        await ConnectAsync(c, live: false);
        var customer = await IndividualAsync(c);
        var a = await InvoiceAsync(c, customer);
        var b = await InvoiceAsync(c, customer, 500m);
        await DispatchAsync(c, DateTimeOffset.UtcNow);

        using (var s = c.Scope())
        {
            var result = await s.ServiceProvider.GetRequiredService<EDocumentService>().CancelAsync(a, "Deneme belgesi");
            Assert.Equal(EDocumentStatus.Iptal, result.Durum);
        }
        Assert.Equal("Test ortamına gönderilmiş e-belge iptal edildi (GİB'e ulaşmamıştı): Deneme belgesi", (await DocAsync(c, a)).SonMesaj);
        Assert.Empty(c.Fake.Cancelled); // entegratöre iptal çağrısı yok

        await RefundAsync(c, b);
        Assert.Equal((EDocumentStatus.Iptal, RefundedSourceCancelled), ((await DocAsync(c, b)).Durum, (await DocAsync(c, b)).SonMesaj));
    }

    // ===================================================================== M3 — gönderilmiş e-Arşiv iadesiz iptal edilmez

    [Fact]
    public async Task M3_sent_e_archive_is_cancelled_only_after_the_invoice_is_refunded()
    {
        using var c = New();
        await ConnectAsync(c);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        var sent = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.Gonderildi, EDocumentKind.EArsiv), (sent.Durum, sent.Tur));

        using (var s = c.Scope())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                s.ServiceProvider.GetRequiredService<EDocumentService>().CancelAsync(id, "Yanlış kesildi"));
            Assert.StartsWith("Gönderilmiş e-Arşiv yalnız fatura iade edildikten sonra iptal edilebilir", ex.Message, StringComparison.Ordinal);
        }
        Assert.Empty(c.Fake.Cancelled);
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
        Assert.Equal(200m, await NetVatAsync(c));

        await RefundAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum); // gönderilmiş belgeye iade dokunmaz
        using (var s = c.Scope())
            Assert.Equal(EDocumentStatus.Iptal, (await s.ServiceProvider.GetRequiredService<EDocumentService>().CancelAsync(id, "Yanlış kesildi")).Durum);
        Assert.Equal((sent.Ettn, "Yanlış kesildi"), Assert.Single(c.Fake.Cancelled));
        Assert.Equal(0m, await NetVatAsync(c));
    }

    // ===================================================================== kontroller

    /// <summary>
    /// Saf: çok oranlı (%20 / %10 / %0) + döviz (EUR, kur 35,123456) fatura → XML tutarları fatura + defterle birebir.
    /// Oracle: 100,00×%20=20,00; 50,00×%10=5,00; 30,00×%0=0 → net 180,00, KDV 25,00, toplam 205,00 EUR.
    /// </summary>
    [Fact]
    public void C3_multi_rate_fx_ubl_amounts_match_invoice_and_ledger()
    {
        var inv = new Invoice
        {
            No = $"RNT{DateTime.UtcNow.Year}000000123", Tarih = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(9), TimeSpan.Zero),
            NetTutar = 180.00m, KdvTutar = 25.00m, GenelToplam = 205.00m, Currency = "EUR", Kur = 35.123456m, CariId = Guid.NewGuid(),
        };
        inv.Lines.Add(new InvoiceLine { Aciklama = "Kira", Miktar = 1m, BirimNetFiyat = 100m, KdvOrani = 0.20m, SatirNet = 100.00m, SatirKdv = 20.00m, SatirToplam = 120m });
        inv.Lines.Add(new InvoiceLine { Aciklama = "Koltuk", Miktar = 2m, BirimNetFiyat = 25m, KdvOrani = 0.10m, SatirNet = 50.00m, SatirKdv = 5.00m, SatirToplam = 55m });
        inv.Lines.Add(new InvoiceLine { Aciklama = "Muaf", Miktar = 1m, BirimNetFiyat = 30m, KdvOrani = 0m, SatirNet = 30.00m, SatirKdv = 0m, SatirToplam = 30m });
        var buyer = new Customer { Tip = CustomerType.Kurumsal, Unvan = "Alıcı A.Ş.", VergiNo = "9876543210", Il = "Ankara", Ilce = "Çankaya" };
        var settings = new TenantSettings { FirmaUnvan = "Satıcı A.Ş.", FirmaVergiNo = "1234567890", FirmaIl = "İstanbul", FirmaIlce = "Kadıköy" };
        var input = EDocumentInputFactory.Create(new EDocumentSource(inv, buyer, null, settings, Guid.NewGuid()),
            UblProfile.TemelFatura, TimeZoneInfo.Utc);
        var built = UblInvoiceBuilder.Build(input);
        var x = System.Xml.Linq.XDocument.Load(new MemoryStream(built.Xml));
        var cbc = UblInvoiceBuilder.Cbc; var cac = UblInvoiceBuilder.Cac;
        string V(System.Xml.Linq.XElement parent, string name) => parent.Element(cbc + name)!.Value;
        var lmt = x.Root!.Element(cac + "LegalMonetaryTotal")!;
        Assert.Equal(("180.00", "205.00", "205.00"), (V(lmt, "LineExtensionAmount"), V(lmt, "TaxInclusiveAmount"), V(lmt, "PayableAmount")));
        var tax = x.Root.Element(cac + "TaxTotal")!;
        Assert.Equal("25.00", V(tax, "TaxAmount"));
        var subs = tax.Elements(cac + "TaxSubtotal").Select(t => (V(t, "Percent"), V(t, "TaxableAmount"), V(t, "TaxAmount"))).ToList();
        Assert.Equal([("20", "100.00", "20.00"), ("10", "50.00", "5.00"), ("0", "30.00", "0.00")], subs);
        Assert.Equal("35.123456", V(x.Root.Element(cac + "PricingExchangeRate")!, "CalculationRate"));
        Assert.All(x.Root.Descendants().Where(e => e.Attribute("currencyID") is not null), e => Assert.Equal("EUR", e.Attribute("currencyID")!.Value));
        var ledger = InvoiceService.BuildEntries(inv);
        Assert.Equal(205.00m * 35.123456m, ledger.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase));
        Assert.Equal(25.00m, ledger.Single(e => e.AccountType == LedgerAccountType.Kdv).Amount.Amount);
    }

    /// <summary>
    /// Atomiklik: EBelgeler eklemesi DB'de patlarsa (bu kiracıya özel geçici trigger) fatura + defter de yazılmaz —
    /// üç yolun üçü: manuel (PostAsync), dönem (PostPeriodAsync), dönem job'ı (PeriodInvoiceGenerator).
    /// </summary>
    [Fact]
    public async Task C1_edocument_insert_failure_rolls_back_invoice_and_ledger_on_all_three_paths()
    {
        using var c = New(adapter: false);
        var trig = "test_ebelge_fail_" + c.Tenant.ToString("N")[..8];
        await using (var owner = new NpgsqlConnection(fx.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"CREATE OR REPLACE FUNCTION {trig}() RETURNS trigger AS $$ BEGIN IF NEW.\"TenantId\" = '{c.Tenant}' THEN RAISE EXCEPTION 'test'; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql; " +
                $"CREATE TRIGGER {trig} BEFORE INSERT ON \"EBelgeler\" FOR EACH ROW EXECUTE FUNCTION {trig}();", owner);
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            using var s = c.Scope();
            var sp = s.ServiceProvider;
            var customer = await IndividualAsync(c);
            await Assert.ThrowsAnyAsync<Exception>(() => InvoiceAsync(c, customer));

            await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(x => x.DonemselFaturalamaJob = true);
            var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
            var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 EA " + Random.Shared.Next(1000, 9999) });
            var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(90), GunlukUcret = 100m, DonemselFaturalama = true,
            });
            await Assert.ThrowsAnyAsync<Exception>(() => sp.GetRequiredService<InvoiceService>().CreatePeriodInvoiceAsync(rental, 1));
            PeriodInvoiceGenerator.Sonuc job;
            await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
                job = await PeriodInvoiceGenerator.RunAsync(db, c.Tenant, DateTimeOffset.UtcNow);

            Assert.Equal(0, job.Kesilen);
            Assert.Equal(0, await DbAsync(c, db => db.Invoices.AsNoTracking().CountAsync()));
            Assert.Equal(0, await DbAsync(c, db => db.AccountLedgerEntries.AsNoTracking()
                .CountAsync(e => e.SourceType == "Fatura" || e.SourceType == "FaturaIade")));
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fx.OwnerConnectionString);
            await owner.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {trig} ON \"EBelgeler\"; DROP FUNCTION IF EXISTS {trig}();", owner);
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Yük: 8 fatura, 4 paralel gönderim koşusu + aynı anda her belgeye iptal, kuyruğa alma ve İADE. Değişmezler: bir
    /// ETTN en çok bir kez kabul edilir; kabul edilen belge yerelde ya Gonderildi ya da entegratörde iptal edilmiş İptal;
    /// yerel iptal edilen belge hiç gönderilmez; iadesi kesilmiş ve kabul edilmemiş belge gönderilmez.
    /// </summary>
    [Fact]
    public async Task C2_parallel_dispatch_cancel_queue_refund_never_double_sends_or_hides_delivery()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.SendDelay = TimeSpan.FromMilliseconds(40);
        var customer = await IndividualAsync(c);
        var ids = new List<Guid>();
        for (var i = 0; i < 8; i++) ids.Add(await InvoiceAsync(c, customer, 100m + i));

        var tasks = new List<Task>();
        for (var i = 0; i < 4; i++) tasks.Add(Task.Run(() => DispatchAsync(c, DateTimeOffset.UtcNow)));
        foreach (var id in ids)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var s = c.Scope();
                try { await s.ServiceProvider.GetRequiredService<EDocumentService>().CancelAsync(id, "yük"); } catch (ValidationException) { }
            }));
            tasks.Add(Task.Run(async () =>
            {
                using var s = c.Scope();
                try { await s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(id); } catch (ValidationException) { }
            }));
            tasks.Add(Task.Run(async () =>
            {
                try { await RefundAsync(c, id); } catch (ValidationException) { }
            }));
        }
        await Task.WhenAll(tasks);
        await DispatchAsync(c, DateTimeOffset.UtcNow.AddHours(1));

        Assert.All(c.Fake.SendCalls.GroupBy(x => x.Ettn), g => Assert.Single(g));
        var cancelledAtIntegrator = c.Fake.Cancelled.Select(x => x.Ettn).ToHashSet();
        foreach (var id in ids)
        {
            var d = await DocAsync(c, id);
            var refunded = await DbAsync(c, db => db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == id));
            if (c.Fake.Accepted.ContainsKey(d.Ettn))
                Assert.True(d.Durum == EDocumentStatus.Gonderildi || (d.Durum == EDocumentStatus.Iptal && cancelledAtIntegrator.Contains(d.Ettn)),
                    $"Kabul edilen belge yerelde {d.Durum}");
            else
            {
                Assert.NotEqual(EDocumentStatus.Gonderildi, d.Durum);
                if (refunded) Assert.Equal(EDocumentStatus.Iptal, d.Durum); // iade + gönderilmemiş → kalıcı iptal
            }
        }
    }
}
