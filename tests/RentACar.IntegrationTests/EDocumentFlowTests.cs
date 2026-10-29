using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// e-Belge akışı (PARA SINIFI — fatura tutarına dokunmaz, yalnız durum + belge): fatura kesimi → e-belge kaydı →
/// gönderim kuyruğu → entegratör (test çifti). Gerçek PostgreSQL + racar_app (RLS).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> tutarlar elle (1.000 × %20 = 200 → 1.200); beklenen durum metinleri sabit cümleler;
/// "kaç kez gönderildi" entegratör çiftinin kendi sayacından; XML GİB XSD + şematronuna karşı (UblTrValidator).
/// Şematronun <c>TimeCheck</c> kuralı (düzenleme tarihi ≤ bugün) bu testlerde DIŞARIDA: fatura "şimdi" kesilir ve
/// İstanbul günü, CI'ın UTC günü için 21:00–24:00 arasında "yarın" görünür — kural üretici testlerinde sınanıyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class EDocumentFlowTests(PostgresFixture fx)
{
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

    private const string SellerVkn = "1234567890";

    /// <summary>Entegratöre CANLI ortamda bağlanır: bu dosyanın senaryoları gerçek gönderimi (GİB'e ulaşan belge)
    /// sınar. Test ortamı davranışı (kabul "gönderildi" SAYILMAZ) <c>EDocumentAdversarialTests</c>'te.</summary>
    private static async Task<string> ConnectAsync(Ctx c)
    {
        var secret = "S" + Guid.NewGuid().ToString("N");
        using var s = c.Scope();
        await s.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(new TenantSettingsModel
        {
            FirmaUnvan = "Deneme Oto Kiralama A.Ş.", FirmaVergiNo = SellerVkn, FirmaVergiDairesi = "Kadıköy",
            FirmaAdres = "Moda Cad. No:1", FirmaIl = "İstanbul", FirmaIlce = "Kadıköy",
            EFaturaEntegrator = FakeEInvoiceIntegrator.TestKey, EFaturaKullanici = "entegrator-kullanici", EFaturaSifre = secret,
            EFaturaCanliOrtam = true,
        });
        return secret;
    }

    private static async Task<Guid> CorporateAsync(Ctx c, string vkn)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Kurumsal, Unvan = "Alıcı Lojistik Ltd. Şti.", VergiNo = vkn, VergiDairesi = "Çankaya",
            Adres = "Atatürk Blv. 5", Il = "Ankara", Ilce = "Çankaya",
        });
    }

    private static async Task<Guid> IndividualAsync(Ctx c, string? tckn)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Yılmaz", TcKimlik = tckn, Adres = "Gül Sk. 3", Il = "İzmir", Ilce = "Konak",
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

    private static async Task<EDocumentDispatcher.Sonuc> DispatchAsync(Ctx c, DateTimeOffset now, ILogger? log = null)
    {
        using var s = c.Scope();
        var sp = s.ServiceProvider;
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await EDocumentDispatcher.RunAsync(db, c.Tenant, now, sp.GetRequiredService<EInvoiceIntegratorCatalog>(),
            sp.GetRequiredService<ISecretProtector>(), log ?? NullLogger.Instance);
    }

    private static async Task<T> DbAsync<T>(Ctx c, Func<AppDbContext, Task<T>> read)
    {
        using var s = c.Scope();
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await read(db);
    }

    private static Task<EDocument> DocAsync(Ctx c, Guid invoice)
        => DbAsync(c, db => db.EDocuments.AsNoTracking().SingleAsync(d => d.InvoiceId == invoice));

    private static void AssertUbl(byte[] xml, string type)
    {
        var xsd = UblTrValidator.ValidateXsd(xml);
        Assert.True(xsd.All(e => e.StartsWith("Warning", StringComparison.Ordinal) && e.Contains("imza-yer-tutucu", StringComparison.Ordinal)),
            "XSD: " + string.Join(" | ", xsd));
        var sch = UblTrValidator.ValidateSchematron(xml, type).Where(e => !e.StartsWith("[TimeCheck]", StringComparison.Ordinal)).ToList();
        Assert.True(sch.Count == 0, "Şematron: " + string.Join(" | ", sch));
    }

    // ------------------------------------------------------------------ bağlı değil (bugünkü üretim gerçeği)

    [Fact]
    public async Task Not_connected_invoice_is_issued_unchanged_and_edocument_stays_draft_with_honest_reason()
    {
        using var c = New(adapter: false);
        var customer = await CorporateAsync(c, "9876543210");
        var id = await InvoiceAsync(c, customer);

        // Para değişmez: 1.000 + %20 = 1.200; defter Borç Cari 1.200 / Alacak Gelir 1.000 + KDV 200.
        var invoice = await DbAsync(c, db => db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id));
        Assert.Equal((1000m, 200m, 1200m), (invoice.NetTutar, invoice.KdvTutar, invoice.GenelToplam));
        Assert.Null(invoice.EFaturaEttn);
        Assert.False(invoice.EFaturaGonderildi);
        var ledger = await DbAsync(c, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == id).ToListAsync());
        Assert.Equal(1200m, ledger.Single(e => e.AccountType == LedgerAccountType.Cari).Amount.Amount);

        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Taslak, doc.Durum);
        Assert.Equal("e-Fatura gönderilmedi: entegratör bağlı değil (entegratör seçilmedi).", doc.SonMesaj);
        Assert.NotEqual(Guid.Empty, doc.Ettn);
        Assert.Null(doc.GonderimUtc);
        Assert.Equal(c.Tenant, doc.TenantId);

        using (var s = c.Scope())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(id));
            Assert.StartsWith(EInvoiceReadiness.NotConnectedPrefix, ex.Message, StringComparison.Ordinal);
        }
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(c, DateTimeOffset.UtcNow));
        Assert.Equal(EDocumentStatus.Taslak, (await DocAsync(c, id)).Durum);
    }

    [Fact]
    public async Task Unregistered_adapter_cannot_be_selected_and_a_removed_adapter_leaves_documents_draft()
    {
        using var c = New(adapter: false);
        using (var s = c.Scope())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider.GetRequiredService<TenantSettingsService>()
                .SaveAsync(new TenantSettingsModel { EFaturaEntegrator = FakeEInvoiceIntegrator.TestKey }));
            Assert.Equal("eFaturaEntegrator", ex.Alan);
            // Adaptör sonradan kaldırıldı: ayarda anahtar kalmış (repository'den doğrudan yazılır).
            var secret = s.ServiceProvider.GetRequiredService<ISecretProtector>().Protect("gizli-" + Guid.NewGuid().ToString("N"));
            await s.ServiceProvider.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(x =>
            {
                x.FirmaUnvan = "Deneme A.Ş."; x.FirmaVergiNo = SellerVkn; x.FirmaIl = "İstanbul"; x.FirmaIlce = "Kadıköy";
                x.EFaturaEntegrator = FakeEInvoiceIntegrator.TestKey; x.EFaturaKullanici = "k"; x.EFaturaSifreEnc = secret;
            });
        }
        var id = await InvoiceAsync(c, await CorporateAsync(c, "9876543210"));
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, id)).Durum); // ayar düzeyinde tam

        var result = await DispatchAsync(c, DateTimeOffset.UtcNow);
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 1), result);
        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Taslak, doc.Durum);
        Assert.Equal("e-Fatura gönderilmedi: entegratör bağlı değil (seçili entegratörün adaptörü bu sürümde yok).", doc.SonMesaj);
        Assert.Null(doc.GonderimUtc);
    }

    // ------------------------------------------------------------------ bağlı (test çifti)

    [Fact]
    public async Task Connected_e_invoice_is_sent_once_with_valid_ubl_and_amounts_unchanged()
    {
        using var c = New();
        var secret = await ConnectAsync(c);
        const string vkn = "9876543210";
        c.Fake.RegisteredRecipients.Add(vkn);
        var id = await InvoiceAsync(c, await CorporateAsync(c, vkn));
        var queued = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderilecek, queued.Durum);
        Assert.Null(queued.SonMesaj);

        var now = DateTimeOffset.UtcNow;
        Assert.Equal(new EDocumentDispatcher.Sonuc(1, 0, 0, 0), await DispatchAsync(c, now));
        var sent = Assert.Single(c.Fake.SendCalls);
        Assert.Equal(queued.Ettn, sent.Ettn);
        Assert.Equal((EDocumentKind.EFatura, "TEMELFATURA", vkn, "urn:mail:defaultpk@alici.example"),
            (sent.Tur, sent.Profil, sent.AliciVknTckn, sent.AliciEtiket));
        Assert.Equal(("entegrator-kullanici", secret, true), (c.Fake.LastCredentials!.Kullanici, c.Fake.LastCredentials.Sifre, c.Fake.LastCredentials.CanliOrtam));
        AssertUbl(sent.Xml, "efatura");

        var invoice = await DbAsync(c, db => db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id));
        var xml = XDocument.Parse(Encoding.UTF8.GetString(sent.Xml)).Root!;
        // GİB numarası iç fatura numarasından AYRI: e-Fatura serisi (varsayılan EFA), fatura yılı, sıra 1.
        var year = TimeZoneInfo.ConvertTime(invoice.Tarih, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).Year;
        Assert.Equal($"EFA{year}000000001", xml.Element(UblInvoiceBuilder.Cbc + "ID")!.Value);
        Assert.Equal($"EFA{year}000000001", sent.FaturaNo);
        Assert.StartsWith("RNT", invoice.No, StringComparison.Ordinal); // iç numara değişmedi
        Assert.Equal(sent.Ettn.ToString("D").ToUpperInvariant(), xml.Element(UblInvoiceBuilder.Cbc + "UUID")!.Value);
        Assert.Equal("1200.00", xml.Element(UblInvoiceBuilder.Cac + "LegalMonetaryTotal")!.Element(UblInvoiceBuilder.Cbc + "PayableAmount")!.Value);
        Assert.Equal((1000m, 200m, 1200m), (invoice.NetTutar, invoice.KdvTutar, invoice.GenelToplam)); // dokunulmadı

        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderildi, doc.Durum);
        Assert.Equal(now, doc.GonderimUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal((EDocumentKind.EFatura, "TEMELFATURA", "SATIS", 1), (doc.Tur, doc.Profil, doc.FaturaTipi, doc.DenemeSayisi));
        Assert.Equal(($"EFA{year}000000001", (bool?)true), (doc.BelgeNo, doc.SonDenemeCanli));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(sent.Xml)).ToLowerInvariant(), doc.XmlOzet);
        Assert.StartsWith("REF-", doc.EntegratorRef);
        Assert.Null(doc.KilitBitisUtc);

        // İkinci koşu: gönderilmiş belge kuyrukta değil → ikinci gönderim YOK.
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(c, now.AddHours(1)));
        Assert.Single(c.Fake.SendCalls);
        var after = await DbAsync(c, db => db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id));
        Assert.Equal((1000m, 200m, 1200m), (after.NetTutar, after.KdvTutar, after.GenelToplam));
        var ledger = await DbAsync(c, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == id).CountAsync());
        Assert.Equal(3, ledger); // gönderim deftere satır eklemez
    }

    [Fact]
    public async Task Concurrent_dispatch_runs_send_each_document_exactly_once()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.SendDelay = TimeSpan.FromMilliseconds(250);
        var customer = await CorporateAsync(c, "9876543210");
        var ids = new[] { await InvoiceAsync(c, customer, 100m), await InvoiceAsync(c, customer, 200m), await InvoiceAsync(c, customer, 300m) };

        var now = DateTimeOffset.UtcNow;
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => DispatchAsync(c, now))));
        Assert.Equal(3, runs.Sum(r => r.Gonderilen));
        Assert.Equal(3, c.Fake.SendCalls.Count);
        Assert.Equal(3, c.Fake.SendCalls.Select(x => x.Ettn).Distinct().Count());
        foreach (var id in ids) Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
    }

    [Fact]
    public async Task Lost_response_is_recovered_by_status_query_not_by_resending_the_same_ettn()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.AcceptThenLoseResponse = true; // entegratör belgeyi ALDI ama yanıt kayboldu
        var id = await InvoiceAsync(c, await CorporateAsync(c, "9876543210"));

        var t0 = DateTimeOffset.UtcNow;
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 1, 0, 0), await DispatchAsync(c, t0));
        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderilecek, doc.Durum);
        Assert.Equal(1, doc.DenemeSayisi);
        Assert.Equal(t0.AddMinutes(5), doc.SonrakiDenemeUtc!.Value, TimeSpan.FromSeconds(1));
        Assert.StartsWith("Geçici hata, yeniden denenecek: Yanıt alınamadı", doc.SonMesaj);

        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(c, t0.AddMinutes(1))); // vadesi gelmedi
        Assert.Equal(new EDocumentDispatcher.Sonuc(1, 0, 0, 0), await DispatchAsync(c, t0.AddMinutes(6)));
        Assert.Single(c.Fake.SendCalls);            // aynı ETTN ikinci kez GÖNDERİLMEDİ
        Assert.Single(c.Fake.StatusCalls);          // önce soruldu
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
    }

    [Fact]
    public async Task Transient_failures_back_off_then_fail_after_max_attempts_and_requeue_gives_a_new_budget()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.OnSend = _ => new EInvoiceSendResult(EInvoiceOutcome.GeciciHata, null, null, "Servis geçici olarak kapalı");
        var id = await InvoiceAsync(c, await CorporateAsync(c, "9876543210"));

        var t = DateTimeOffset.UtcNow;
        for (var i = 1; i <= EDocumentPolicy.MaxAttempts; i++)
        {
            await DispatchAsync(c, t);
            t = t.AddHours(5); // en uzun geri çekilmeden (4 sa) sonra
        }
        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Hata, doc.Durum);
        Assert.Equal("Deneme sınırı (5) doldu: Servis geçici olarak kapalı", doc.SonMesaj);
        Assert.Equal(5, c.Fake.SendCalls.Count);
        Assert.Equal(4, c.Fake.StatusCalls.Count); // 2.–5. denemeden önce "ulaştı mı?" soruldu
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(c, t)); // Hata kuyrukta değil

        c.Fake.OnSend = null;
        using (var s = c.Scope())
            Assert.Equal(EDocumentStatus.Gonderilecek, (await s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(id)).Durum);
        var requeued = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.Gonderilecek, 0), (requeued.Durum, requeued.DenemeSayisi));
        Assert.Equal(new EDocumentDispatcher.Sonuc(1, 0, 0, 0), await DispatchAsync(c, t));
        Assert.Equal(6, c.Fake.SendCalls.Count);
        Assert.Equal(1, (await DocAsync(c, id)).DenemeSayisi);
    }

    [Fact]
    public async Task Permanent_rejection_marks_error_without_retry()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.OnSend = _ => new EInvoiceSendResult(EInvoiceOutcome.KaliciHata, null, null, "Şema doğrulama hatası");
        var id = await InvoiceAsync(c, await CorporateAsync(c, "9876543210"));
        var t = DateTimeOffset.UtcNow;
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 1, 0), await DispatchAsync(c, t));
        Assert.Equal("Entegratör belgeyi reddetti: Şema doğrulama hatası", (await DocAsync(c, id)).SonMesaj);
        await DispatchAsync(c, t.AddDays(1));
        Assert.Single(c.Fake.SendCalls);
    }

    // ------------------------------------------------------------------ KVKK: TCKN yalnız XML'de

    [Fact]
    public async Task Individual_buyer_tckn_is_decrypted_into_xml_only_never_into_records_or_logs()
    {
        using var c = New();
        await ConnectAsync(c);
        var tckn = TestNationalId.New();
        var customer = await IndividualAsync(c, tckn);
        var stored = await DbAsync(c, db => db.Customers.AsNoTracking().SingleAsync(x => x.Id == customer));
        Assert.Null(stored.TcKimlik);                    // düz kolon yazılmaz
        Assert.DoesNotContain(tckn, stored.TcKimlikEnc); // at-rest şifreli

        // Entegratör hata metninde alıcının TCKN'sini geri yansıtıyor (en kötü durum).
        c.Fake.OnSend = s => new EInvoiceSendResult(EInvoiceOutcome.GeciciHata, null, null, $"Alıcı {s.AliciVknTckn} için yanıt yok");
        var id = await InvoiceAsync(c, customer);
        var log = new CapturingLogger();
        await DispatchAsync(c, DateTimeOffset.UtcNow, log);

        var sent = Assert.Single(c.Fake.SendCalls);
        Assert.Equal((EDocumentKind.EArsiv, "EARSIVFATURA", tckn), (sent.Tur, sent.Profil, sent.AliciVknTckn)); // kayıtlı değil → e-Arşiv
        Assert.Contains(tckn, Encoding.UTF8.GetString(sent.Xml), StringComparison.Ordinal); // çözülüp XML'e girdi
        AssertUbl(sent.Xml, "earchive");

        var doc = await DocAsync(c, id);
        Assert.Equal("Geçici hata, yeniden denenecek: Alıcı *********** için yanıt yok", doc.SonMesaj);
        foreach (var value in new[] { doc.SonMesaj, doc.EntegratorDurum, doc.EntegratorRef, doc.Profil, doc.XmlOzet })
            Assert.DoesNotContain(tckn, value ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(tckn, log.All, StringComparison.Ordinal);
        Assert.NotEmpty(log.Lines);
        var audit = await DbAsync(c, db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == doc.Id.ToString()).ToListAsync());
        Assert.All(audit, a => Assert.DoesNotContain(tckn, (a.OldValues ?? "") + (a.NewValues ?? ""), StringComparison.Ordinal));

        // Alıcı sorgusu hatası da maskelenir.
        c.Fake.OnSend = null;
        c.Fake.RecipientError = $"Kullanıcı listesi sorgusu başarısız: {tckn}";
        await DispatchAsync(c, DateTimeOffset.UtcNow.AddHours(1), log);
        doc = await DocAsync(c, id);
        Assert.Equal("Alıcı e-Fatura sorgusu yapılamadı: Kullanıcı listesi sorgusu başarısız: ***********", doc.SonMesaj);
        Assert.DoesNotContain(tckn, log.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Individual_without_tckn_goes_to_e_archive_with_consumer_placeholder_without_recipient_query()
    {
        using var c = New();
        await ConnectAsync(c);
        var id = await InvoiceAsync(c, await IndividualAsync(c, null));
        Assert.Equal(new EDocumentDispatcher.Sonuc(1, 0, 0, 0), await DispatchAsync(c, DateTimeOffset.UtcNow));
        var sent = Assert.Single(c.Fake.SendCalls);
        Assert.Equal((EDocumentKind.EArsiv, "11111111111"), (sent.Tur, sent.AliciVknTckn));
        Assert.Empty(c.Fake.RecipientCalls);
        AssertUbl(sent.Xml, "earchive");
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
    }

    [Fact]
    public async Task Customer_without_city_is_a_permanent_data_error_until_fixed_and_requeued()
    {
        using var c = New();
        await ConnectAsync(c);
        Guid customer;
        using (var s = c.Scope())
            customer = await s.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
            { Tip = CustomerType.Kurumsal, Unvan = "İlsiz Ltd.", VergiNo = "1112223334" });
        var id = await InvoiceAsync(c, customer);
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 1, 0), await DispatchAsync(c, DateTimeOffset.UtcNow));
        Assert.Equal("Alıcı: adreste il ve ilçe zorunludur.", (await DocAsync(c, id)).SonMesaj);
        Assert.Empty(c.Fake.SendCalls);
    }

    // ------------------------------------------------------------------ tutar kilidi

    [Fact]
    public async Task Invoice_whose_lines_do_not_add_up_is_never_sent()
    {
        using var c = New();
        await ConnectAsync(c);
        var customer = await CorporateAsync(c, "9876543210");
        // Kayıtlı toplam 1.200 ama satırlar 1.100 — e-belge defterden sapmamalı (elle kurulmuş bozuk kayıt).
        var invoice = new Invoice
        {
            No = "TST" + DateTime.UtcNow.Year + "999999999", CariId = customer, Tarih = DateTimeOffset.UtcNow.AddMinutes(-1),
            NetTutar = 1000m, KdvTutar = 200m, GenelToplam = 1200m, Currency = "TRY", Kur = 1m,
        };
        invoice.Lines.Add(new InvoiceLine { InvoiceId = invoice.Id, Aciklama = "Kira", Miktar = 1m, BirimNetFiyat = 900m, KdvOrani = 0.20m, SatirNet = 900m, SatirKdv = 180m, SatirToplam = 1080m });
        await DbAsync(c, async db =>
        {
            db.Invoices.Add(invoice);
            db.EDocuments.Add(new EDocument { InvoiceId = invoice.Id, Durum = EDocumentStatus.Gonderilecek });
            return await db.SaveChangesAsync();
        });
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 1, 0), await DispatchAsync(c, DateTimeOffset.UtcNow));
        Assert.Equal("Fatura satırlarının toplamı fatura toplamıyla tutmuyor; e-belge üretilmedi (tutarlar değiştirilmez).",
            (await DocAsync(c, invoice.Id)).SonMesaj);
        Assert.Empty(c.Fake.SendCalls);
    }

    [Fact]
    public async Task Withholding_invoice_is_not_sent_because_ledger_does_not_reflect_it()
    {
        using var c = New();
        await ConnectAsync(c);
        Guid id;
        using (var s = c.Scope())
            id = await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
            {
                CariId = await CorporateAsync(c, "9876543210"), NetTutar = 1000m, KdvOrani = 0.20m,
                Vergi = new InvoiceTaxInfo(null, 20m, 40m, null, false, false),
            });
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 1, 0), await DispatchAsync(c, DateTimeOffset.UtcNow));
        Assert.StartsWith("Tevkifatlı fatura için e-belge henüz otomatik üretilmiyor", (await DocAsync(c, id)).SonMesaj);
        Assert.Empty(c.Fake.SendCalls);
    }

    // ------------------------------------------------------------------ iade + iptal

    [Fact]
    public async Task Refund_is_never_auto_sent_and_source_e_archive_can_be_cancelled_at_integrator()
    {
        using var c = New();
        await ConnectAsync(c);
        var source = await InvoiceAsync(c, await IndividualAsync(c, TestNationalId.New()));
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        var sourceDoc = await DocAsync(c, source);
        Assert.Equal((EDocumentStatus.Gonderildi, EDocumentKind.EArsiv), (sourceDoc.Durum, sourceDoc.Tur));

        using var s = c.Scope();
        var refund = await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateRefundAsync(source);
        var refundDoc = await DocAsync(c, refund);
        Assert.Equal((EDocumentStatus.Taslak, EDocumentPolicy.RefundNotSent), (refundDoc.Durum, refundDoc.SonMesaj));
        var edocs = s.ServiceProvider.GetRequiredService<EDocumentService>();
        await Assert.ThrowsAsync<ValidationException>(() => edocs.QueueAsync(refund));
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(c, DateTimeOffset.UtcNow.AddHours(1)));

        var missing = await Assert.ThrowsAsync<ValidationException>(() => edocs.CancelAsync(source, "  "));
        Assert.Equal("gerekce", missing.Alan);
        var result = await edocs.CancelAsync(source, "Müşteri hizmeti iade etti");
        Assert.Equal(EDocumentStatus.Iptal, result.Durum);
        Assert.Equal((sourceDoc.Ettn, "Müşteri hizmeti iade etti"), Assert.Single(c.Fake.Cancelled));
        Assert.Equal(EDocumentStatus.Iptal, (await DocAsync(c, source)).Durum);
        Assert.Equal(EDocumentStatus.Iptal, (await edocs.CancelAsync(source, "tekrar")).Durum); // idempotent
        Assert.Single(c.Fake.Cancelled);
    }

    [Fact]
    public async Task Sent_e_invoice_cannot_be_cancelled_by_sender_while_unsent_one_is_cancelled_locally_and_never_sent()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.RegisteredRecipients.Add("9876543210");
        var customer = await CorporateAsync(c, "9876543210");
        var sentId = await InvoiceAsync(c, customer);
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        var unsentId = await InvoiceAsync(c, customer, 50m);

        using var s = c.Scope();
        var edocs = s.ServiceProvider.GetRequiredService<EDocumentService>();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => edocs.CancelAsync(sentId, "Hatalı kesildi"));
        Assert.StartsWith("Gönderilmiş e-Fatura entegratörden iptal edilemez", ex.Message, StringComparison.Ordinal);
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, sentId)).Durum);
        Assert.Empty(c.Fake.Cancelled);

        Assert.Equal(EDocumentStatus.Iptal, (await edocs.CancelAsync(unsentId, "Kağıt fatura kesildi")).Durum);
        Assert.Equal("e-Belge gönderilmeden iptal edildi: Kağıt fatura kesildi", (await DocAsync(c, unsentId)).SonMesaj);
        await DispatchAsync(c, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Single(c.Fake.SendCalls); // iptal edilen hiç gönderilmedi
        await Assert.ThrowsAsync<ValidationException>(() => edocs.QueueAsync(unsentId));

        // FinanceReverse olmadan iptal yok; FinanceWrite olmadan kuyruğa alma yok.
        using var operatorScope = c.Scope(UserRole.Operator);
        var opEdocs = operatorScope.ServiceProvider.GetRequiredService<EDocumentService>();
        await Assert.ThrowsAsync<NoPermissionException>(() => opEdocs.CancelAsync(sentId, "x"));
        await Assert.ThrowsAsync<NoPermissionException>(() => opEdocs.QueueAsync(sentId));
    }

    [Fact]
    public async Task Cancel_after_a_lost_response_asks_the_integrator_first_and_never_hides_a_delivered_document()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.RegisteredRecipients.Add("9876543210");
        var customer = await CorporateAsync(c, "9876543210");
        var archive = await InvoiceAsync(c, await IndividualAsync(c, TestNationalId.New()));

        // e-Arşiv: entegratör aldı, yanıt kayboldu → kayıt "kuyrukta" görünüyor ama belge GİB tarafında var.
        c.Fake.AcceptThenLoseResponse = true;
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, archive)).Durum);

        using var s = c.Scope();
        var edocs = s.ServiceProvider.GetRequiredService<EDocumentService>();
        // Gönderilmiş e-Arşiv ancak iadeden sonra iptal edilir (M3). İade, sonucu belirsiz (denenmiş) belgeye DOKUNMAZ:
        // belge entegratöre ulaşmış olabilir — kayıt "kuyrukta" kalır, iptal entegratöre sorarak karar verir.
        await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateRefundAsync(archive);
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, archive)).Durum);
        var result = await edocs.CancelAsync(archive, "Müşteri vazgeçti");
        Assert.Equal(EDocumentStatus.Iptal, result.Durum);
        var archiveDoc = await DocAsync(c, archive);
        Assert.Equal((archiveDoc.Ettn, "Müşteri vazgeçti"), Assert.Single(c.Fake.Cancelled)); // entegratörde iptal
        Assert.NotNull(archiveDoc.GonderimUtc);
        Assert.Equal("e-Arşiv belgesi entegratörde iptal edildi: Müşteri vazgeçti", archiveDoc.SonMesaj);

        // e-Fatura: aynı durum → "gönderilmeden iptal" YAPILMAZ; kayıt gönderildi olarak düzeltilir, iptal reddedilir.
        var einvoice = await InvoiceAsync(c, customer);
        c.Fake.AcceptThenLoseResponse = true;
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => edocs.CancelAsync(einvoice, "Hatalı kesildi"));
        Assert.StartsWith("Gönderilmiş e-Fatura entegratörden iptal edilemez", ex.Message, StringComparison.Ordinal);
        var corrected = await DocAsync(c, einvoice);
        Assert.Equal(EDocumentStatus.Gonderildi, corrected.Durum);
        Assert.Equal("Önceki gönderim denemesi entegratöre ulaşmış (durum sorgusuyla doğrulandı).", corrected.SonMesaj);
        Assert.Equal(2, c.Fake.SendCalls.Count); // hiçbir belge ikinci kez gönderilmedi
    }

    // ------------------------------------------------------------------ kiracı izolasyonu

    [Fact]
    public async Task Other_tenant_cannot_see_queue_or_dispatch_documents()
    {
        using var a = New();
        await ConnectAsync(a);
        var id = await InvoiceAsync(a, await CorporateAsync(a, "9876543210"));
        var doc = await DocAsync(a, id);

        using var b = new Ctx(new TestHost(fx.AppConnectionString, s => s.AddSingleton<IEInvoiceIntegrator>(a.Fake)), a.Fake, Guid.NewGuid());
        await ConnectAsync(b);
        using (var s = b.Scope())
        {
            var edocs = s.ServiceProvider.GetRequiredService<EDocumentService>();
            Assert.Null(await edocs.GetByInvoiceAsync(id));
            Assert.Empty(await edocs.ListByInvoicesAsync([id]));
            var ex = await Assert.ThrowsAsync<ValidationException>(() => edocs.QueueAsync(id));
            Assert.Equal("Fatura bulunamadı.", ex.Message);
            await Assert.ThrowsAsync<ValidationException>(() => edocs.CancelAsync(id, "başka kiracı"));
        }
        Assert.Equal(new EDocumentDispatcher.Sonuc(0, 0, 0, 0), await DispatchAsync(b, DateTimeOffset.UtcNow));
        Assert.Empty(a.Fake.SendCalls);

        // RLS (EF filtresinden bağımsız): racar_app + B'nin GUC'u satırı görmez; A'nınki görür.
        Assert.Equal(0L, await CountAsRacarAppAsync(b.Tenant, doc.Id));
        Assert.Equal(1L, await CountAsRacarAppAsync(a.Tenant, doc.Id));
        // B'nin GUC'u ile A'nın satırını güncellemek de etkisiz.
        await using (var conn = new NpgsqlConnection(fx.AppConnectionString))
        {
            await conn.OpenAsync();
            await using var guc = new NpgsqlCommand($"SELECT set_config('app.tenant_id', '{b.Tenant}', false)", conn);
            await guc.ExecuteNonQueryAsync();
            await using var upd = new NpgsqlCommand("UPDATE \"EBelgeler\" SET \"Durum\" = 4 WHERE \"Id\" = @id", conn);
            upd.Parameters.AddWithValue("id", doc.Id);
            Assert.Equal(0, await upd.ExecuteNonQueryAsync());
        }
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(a, id)).Durum);
    }

    private async Task<long> CountAsRacarAppAsync(Guid tenant, Guid docId)
    {
        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using (var guc = new NpgsqlCommand($"SELECT set_config('app.tenant_id', '{tenant}', false)", conn))
            await guc.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM \"EBelgeler\" WHERE \"Id\" = @id", conn);
        cmd.Parameters.AddWithValue("id", docId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    // ------------------------------------------------------------------ dönem job'ı da aynı kuyruğa girer

    [Fact]
    public async Task Period_job_invoices_get_an_edocument_record_in_the_same_transaction()
    {
        using var c = New(adapter: false);
        using var s = c.Scope();
        var sp = s.ServiceProvider;
        await sp.GetRequiredService<ITenantSettingsRepository>().UpsertAsync(x => x.DonemselFaturalamaJob = true);
        var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-65);
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 EB " + Random.Shared.Next(1000, 9999) });
        var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Job", Soyad = "M" });
        await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(90), GunlukUcret = 100m, DonemselFaturalama = true,
        });
        PeriodInvoiceGenerator.Sonuc result;
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            result = await PeriodInvoiceGenerator.RunAsync(db, c.Tenant, DateTimeOffset.UtcNow);
        Assert.True(result.Kesilen > 0);

        var invoices = await DbAsync(c, db => db.Invoices.AsNoTracking().Select(i => i.Id).ToListAsync());
        var docs = await DbAsync(c, db => db.EDocuments.AsNoTracking().ToListAsync());
        Assert.Equal(invoices.OrderBy(x => x), docs.Select(d => d.InvoiceId).OrderBy(x => x));
        Assert.All(docs, d =>
        {
            Assert.Equal(c.Tenant, d.TenantId);
            Assert.Equal(EDocumentStatus.Taslak, d.Durum);
            Assert.Equal("e-Fatura gönderilmedi: entegratör bağlı değil (entegratör seçilmedi).", d.SonMesaj);
        });
    }
}
