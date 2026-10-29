using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.EInvoicing;
using RentACar.Application.Finance;
using RentACar.Application.Integrations;
using RentACar.Application.TenantSettings;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR #394 adversarial incelemesinin 5 Low bulgusunun KALICI testleri (doğrulayıcı probe'larından çevrildi):
/// N1 istemci zaman aşımı + sunucuda geç kabul, N2 kayıtsız (özellik öncesi) faturada iade ↔ kuyruk yarışı, N3 canlıdan
/// teste geçiş, N4 maskeleme ayraçları, E5 e-Fatura / e-Arşiv ayrı seri (GİB e-Arşiv kılavuzu v1.18 §6).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen numaralar elle ("EAR" + yıl + "000000001"); bekleme süresi sınırı sabit
/// 30 dk (politika sabitinden okunmaz); "nerede kabul edildi" entegratör çiftinin kendi kaydından; mesajlar sabit cümle.</para>
/// </summary>
[Collection("postgres")]
public sealed class EDocumentLowFindingsTests(PostgresFixture fx)
{
    private const string RefundedSourceCancelled = "Fatura iade edildi; e-belge gönderilmeden iptal edildi.";
    private const string RefundedSourceNotSent = "Bu fatura iade edildi; e-belgesi gönderilmez (defterde satış geri alındı).";
    private const string RefundedAwaitingSettle = "Fatura iade edildi; önceki gönderim denemesinin sonucu kesinleşince e-belge iptal edilecek.";
    private const string NotSettledPrefix = "Önceki gönderim denemesinin sonucu henüz kesinleşmedi";
    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(30);

    private sealed class Ctx(TestHost host, FakeEInvoiceIntegrator fake, Guid tenant) : IDisposable
    {
        public TestHost Host { get; } = host;
        public FakeEInvoiceIntegrator Fake { get; } = fake;
        public Guid Tenant { get; } = tenant;
        public IServiceScope Scope(UserRole role = UserRole.Admin) => Host.ScopeFor(Tenant, Guid.NewGuid(), role: role);
        public void Dispose() => Host.Dispose();
    }

    private Ctx New()
    {
        var fake = new FakeEInvoiceIntegrator();
        var host = new TestHost(fx.AppConnectionString, s => s.AddSingleton<IEInvoiceIntegrator>(fake));
        return new Ctx(host, fake, Guid.NewGuid());
    }

    private static TenantSettingsModel Settings(string? secret, bool live, string? eFaturaSeries = null, string? eArsivSeries = null) => new()
    {
        FirmaUnvan = "Deneme Oto Kiralama A.Ş.", FirmaVergiNo = "1234567890", FirmaVergiDairesi = "Kadıköy",
        FirmaAdres = "Moda Cad. No:1", FirmaIl = "İstanbul", FirmaIlce = "Kadıköy",
        EFaturaEntegrator = FakeEInvoiceIntegrator.TestKey, EFaturaKullanici = "entegrator-kullanici", EFaturaSifre = secret,
        EFaturaCanliOrtam = live, EFaturaSeriKodu = eFaturaSeries, EArsivSeriKodu = eArsivSeries,
    };

    private static async Task ConnectAsync(Ctx c, bool live = true)
    {
        using var s = c.Scope();
        await s.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(Settings("S" + Guid.NewGuid().ToString("N"), live));
    }

    private static async Task SwitchAsync(Ctx c, bool live)
    {
        using var s = c.Scope(); // sır boş = mevcut korunur
        await s.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(Settings(null, live));
    }

    private static async Task<Guid> IndividualAsync(Ctx c)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Yılmaz", TcKimlik = TestNationalId.New(), Adres = "Gül Sk. 3", Il = "İzmir", Ilce = "Konak",
        });
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

    private static async Task<Guid> InvoiceAsync(Ctx c, Guid customer, decimal net = 1000m)
    {
        using var s = c.Scope();
        return await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateManualAsync(new ManualInvoiceInput
        {
            CariId = customer, NetTutar = net, KdvOrani = 0.20m, Aciklama = "Araç kiralama hizmeti",
        });
    }

    private static async Task RefundAsync(Ctx c, Guid source)
    {
        using var s = c.Scope();
        await s.ServiceProvider.GetRequiredService<InvoiceService>().CreateRefundAsync(source);
    }

    private static async Task<string> CancelAsync(Ctx c, Guid id, string reason)
    {
        using var s = c.Scope();
        try { return (await s.ServiceProvider.GetRequiredService<EDocumentService>().CancelAsync(id, reason)).Durum.ToString(); }
        catch (ValidationException ex) { return "red: " + ex.Message; }
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

    /// <summary>Son denemeyi geçmişe çeker (süre geçişi benzetimi; saat beklenmez).</summary>
    private static Task<int> AgeLastAttemptAsync(Ctx c, Guid invoice, TimeSpan by)
        => DbAsync(c, db => db.EDocuments.Where(d => d.InvoiceId == invoice)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.SonDenemeUtc, d => d.SonDenemeUtc - by)));

    /// <summary>Fatura tarihinin İstanbul yılı (numaranın yıl kısmı).</summary>
    private static async Task<int> YearAsync(Ctx c, Guid invoice)
    {
        var tarih = await DbAsync(c, db => db.Invoices.AsNoTracking().Where(i => i.Id == invoice).Select(i => i.Tarih).SingleAsync());
        return TimeZoneInfo.ConvertTime(tarih, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).Year;
    }

    /// <summary>İstemci zaman aşımı: istek entegratöre ulaştı, sunucu belgeyi 1,5 sn sonra KABUL ediyor; istemci
    /// TaskCanceledException (HttpClient.Timeout) görüyor.</summary>
    private static Func<Task?> ArmClientTimeoutWithLateAccept(Ctx c)
    {
        Task? late = null;
        c.Fake.OnSend = sub =>
        {
            var creds = c.Fake.LastCredentials!;
            late = Task.Run(async () =>
            {
                await Task.Delay(1500);
                c.Fake.OnSend = null;
                await c.Fake.SendAsync(creds, sub); // aynı ETTN, sunucu tarafı kabul
            });
            throw new TaskCanceledException("İstemci zaman aşımı (HttpClient.Timeout).");
        };
        return () => late;
    }

    // ===================================================================== N1 — istemci zaman aşımı + geç kabul

    [Fact]
    public async Task N1_local_cancel_does_not_trust_a_single_not_found_while_the_live_attempt_may_still_land()
    {
        using var c = New();
        await ConnectAsync(c);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        var late = ArmClientTimeoutWithLateAccept(c);
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0);
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, id)).Durum);

        var cancel = await CancelAsync(c, id, "Müşteri vazgeçti"); // entegratör henüz "bulunamadı" diyor
        await late()!;
        Assert.StartsWith("red: " + NotSettledPrefix, cancel, StringComparison.Ordinal);
        var doc = await DocAsync(c, id);
        Assert.NotEqual(EDocumentStatus.Iptal, doc.Durum);
        Assert.True(c.Fake.AcceptedLive(doc.Ettn)); // sunucu kabul etti

        // Sonraki koşu önce sorar → kabul görülür → gönderildi; ikinci gönderim yok.
        await DispatchAsync(c, t0.AddMinutes(6));
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
        Assert.Equal(2, c.Fake.SendCalls.Count); // istemci denemesi + sunucu tarafı benzetimi; yeniden gönderim yok
    }

    [Fact]
    public async Task N1_after_the_settle_period_a_not_found_attempt_can_be_cancelled_locally()
    {
        using var c = New();
        await ConnectAsync(c);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        c.Fake.OnSend = _ => throw new TaskCanceledException("İstemci zaman aşımı."); // sunucuya ulaşmadı
        await DispatchAsync(c, DateTimeOffset.UtcNow);

        Assert.StartsWith("red: " + NotSettledPrefix, await CancelAsync(c, id, "Vazgeçildi"), StringComparison.Ordinal);
        await AgeLastAttemptAsync(c, id, Settle - TimeSpan.FromMinutes(1)); // 29 dk: hâlâ bekler
        Assert.StartsWith("red: " + NotSettledPrefix, await CancelAsync(c, id, "Vazgeçildi"), StringComparison.Ordinal);
        await AgeLastAttemptAsync(c, id, TimeSpan.FromMinutes(2));           // 31 dk
        Assert.Equal("Iptal", await CancelAsync(c, id, "Vazgeçildi"));
        Assert.Empty(c.Fake.Accepted);
    }

    [Fact]
    public async Task N1_refunded_invoice_with_an_unsettled_live_attempt_is_deferred_then_cancelled_never_resent()
    {
        using var c = New();
        await ConnectAsync(c);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        c.Fake.OnSend = _ => throw new TaskCanceledException("İstemci zaman aşımı.");
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0);
        c.Fake.OnSend = null;
        await RefundAsync(c, id); // denenmiş belgeye iade dokunmaz
        Assert.Equal(EDocumentStatus.Gonderilecek, (await DocAsync(c, id)).Durum);

        await DispatchAsync(c, t0.AddMinutes(6)); // 6 dk: bulunamadı ama kesin değil → ertele
        var deferred = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.Gonderilecek, RefundedAwaitingSettle), (deferred.Durum, deferred.SonMesaj));

        await DispatchAsync(c, t0.AddMinutes(31)); // 31 dk: kesinleşti → iptal
        var doc = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.Iptal, RefundedSourceCancelled), (doc.Durum, doc.SonMesaj));
        Assert.Single(c.Fake.SendCalls); // yalnız ilk (zaman aşımına uğrayan) deneme
        Assert.Empty(c.Fake.Accepted);
    }

    // ===================================================================== N2 — kayıtsız fatura: iade ↔ kuyruk

    private async Task DeleteEDocumentRowAsync(Ctx c, Guid invoice) // özellik öncesi fatura benzetimi
    {
        await using var owner = new NpgsqlConnection(fx.OwnerConnectionString);
        await owner.OpenAsync();
        // Sahip rolü de RLS'e tabi (FORCE): kiracı bağlamı kurulur.
        await using var cmd = new NpgsqlCommand(
            $"SELECT set_config('app.tenant_id', '{c.Tenant}', false); DELETE FROM \"EBelgeler\" WHERE \"InvoiceId\" = '{invoice}'", owner);
        await cmd.ExecuteNonQueryAsync();
        Assert.Equal(0, await DbAsync(c, db => db.EDocuments.CountAsync(d => d.InvoiceId == invoice)));
    }

    [Fact]
    public async Task N2_refund_of_a_pre_feature_invoice_writes_a_cancelled_record_and_queueing_is_refused()
    {
        using var c = New();
        await ConnectAsync(c);
        var source = await InvoiceAsync(c, await IndividualAsync(c));
        await DeleteEDocumentRowAsync(c, source);

        await RefundAsync(c, source);
        var doc = await DocAsync(c, source);
        Assert.Equal((EDocumentStatus.Iptal, RefundedSourceCancelled, c.Tenant), (doc.Durum, doc.SonMesaj, doc.TenantId));

        using (var s = c.Scope())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() => s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(source));
            Assert.Equal(RefundedSourceNotSent, ex.Message);
        }
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        Assert.Empty(c.Fake.SendCalls);
    }

    /// <summary>
    /// Yarış: iade TX'i kaynağı sonuçlandırdı ama commit etmedi (geçici trigger: iade INSERT'inde 3 sn uyku). Bu sırada
    /// "Gönderime al" ve gönderim işi koşar. Beklenen: kuyruğa alma iadeyi bekler ve reddeder; iade edilmiş fatura gönderilmez.
    /// </summary>
    [Fact]
    public async Task N2_queue_waits_for_an_uncommitted_refund_of_a_pre_feature_invoice_and_nothing_is_sent()
    {
        using var c = New();
        await ConnectAsync(c);
        var source = await InvoiceAsync(c, await IndividualAsync(c));
        await DeleteEDocumentRowAsync(c, source);
        var trig = "test_iade_sleep_" + c.Tenant.ToString("N")[..8];
        await using (var owner = new NpgsqlConnection(fx.OwnerConnectionString))
        {
            await owner.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                $"CREATE OR REPLACE FUNCTION {trig}() RETURNS trigger AS $$ BEGIN IF NEW.\"TenantId\" = '{c.Tenant}' AND NEW.\"IadeMi\" THEN PERFORM pg_sleep(3); END IF; RETURN NEW; END; $$ LANGUAGE plpgsql; " +
                $"CREATE TRIGGER {trig} BEFORE INSERT ON \"Invoices\" FOR EACH ROW EXECUTE FUNCTION {trig}();", owner);
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            var refund = Task.Run(() => RefundAsync(c, source));
            await Task.Delay(800); // iade TX'i trigger uykusunda (kilit alındı, commit yok)
            string queued;
            using (var s = c.Scope())
            {
                try { queued = (await s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(source)).Durum.ToString(); }
                catch (ValidationException ex) { queued = "red: " + ex.Message; }
            }
            await DispatchAsync(c, DateTimeOffset.UtcNow);
            await refund;

            Assert.Equal("red: " + RefundedSourceNotSent, queued);
            Assert.True(await DbAsync(c, db => db.Invoices.AsNoTracking().AnyAsync(i => i.KaynakFaturaId == source)));
            Assert.Equal(EDocumentStatus.Iptal, (await DocAsync(c, source)).Durum);
            Assert.Empty(c.Fake.SendCalls);
        }
        finally
        {
            await using var owner = new NpgsqlConnection(fx.OwnerConnectionString);
            await owner.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {trig} ON \"Invoices\"; DROP FUNCTION IF EXISTS {trig}();", owner);
            await drop.ExecuteNonQueryAsync();
        }
    }

    // ===================================================================== N3 — canlıdan teste geçiş

    [Fact]
    public async Task N3_live_lost_response_then_switch_to_test_is_found_in_live_and_never_hidden_by_a_test_send()
    {
        using var c = New();
        await ConnectAsync(c, live: true);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        c.Fake.AcceptThenLoseResponse = true;
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0); // canlıda kabul, yanıt kayboldu
        Assert.Equal((bool?)true, (await DocAsync(c, id)).SonDenemeCanli);

        await SwitchAsync(c, live: false);
        await DispatchAsync(c, t0.AddHours(1)); // canlıda da sorar → bulundu → gönderildi (teste gönderilmez)
        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderildi, doc.Durum);
        Assert.True(c.Fake.AcceptedLive(doc.Ettn));
        Assert.Single(c.Fake.SendCalls);

        await RefundAsync(c, id); // gönderilmiş belgeye iade dokunmaz
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
    }

    [Fact]
    public async Task N3_unsettled_live_attempt_blocks_switching_to_test_until_settled_then_live_is_asked_first()
    {
        using var c = New();
        await ConnectAsync(c, live: true);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        c.Fake.OnSend = _ => throw new TaskCanceledException("İstemci zaman aşımı."); // canlıya ulaşmadı
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0);
        c.Fake.OnSend = null;
        await SwitchAsync(c, live: false);

        await DispatchAsync(c, t0.AddMinutes(6)); // canlı deneme kesinleşmedi → teste gönderilmez
        var waiting = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.Gonderilecek, (bool?)true), (waiting.Durum, waiting.SonDenemeCanli));
        Assert.StartsWith(NotSettledPrefix, waiting.SonMesaj, StringComparison.Ordinal);
        Assert.Single(c.Fake.SendCalls);

        await DispatchAsync(c, t0.AddMinutes(31)); // kesinleşti: canlıda yok → test ortamına gider
        var doc = await DocAsync(c, id);
        Assert.Equal((EDocumentStatus.TestGonderildi, (bool?)false), (doc.Durum, doc.SonDenemeCanli));
        Assert.False(c.Fake.AcceptedLive(doc.Ettn));
        Assert.Equal(2, c.Fake.SendCalls.Count);
    }

    [Fact]
    public async Task N3_local_cancel_after_switching_to_test_asks_the_live_environment_too()
    {
        using var c = New();
        await ConnectAsync(c, live: true);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        c.Fake.AcceptThenLoseResponse = true;
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        await SwitchAsync(c, live: false);

        // e-Arşiv canlıda kabul edilmiş: yerel iptal YOK; kayıt gönderildi olarak düzeltilir, iadesiz iptal reddedilir.
        Assert.StartsWith("red: Gönderilmiş e-Arşiv yalnız fatura iade edildikten sonra iptal edilebilir",
            await CancelAsync(c, id, "Vazgeçildi"), StringComparison.Ordinal);
        Assert.Equal(EDocumentStatus.Gonderildi, (await DocAsync(c, id)).Durum);
        Assert.Empty(c.Fake.Cancelled);
    }

    /// <summary>
    /// #410 adversarial M1 (P1): test'te kabul → canlıya geç, yeniden kuyruk → canlı deneme istemci zaman aşımı → 30 dk
    /// dolmadan teste geri dön. Canlının "bulunamadı"sından sonra test ortamına SORULMAZ (aynı ETTN'in eski test kabulü
    /// bulunup belge TestGonderildi sayılırdı); belge kuyrukta bekler. Sunucu canlıda geç kabul edince iade belgeyi yerelde
    /// iptal ETMEZ; sonraki koşu canlıda bulur → Gonderildi.
    /// </summary>
    private static async Task<(Guid Id, Func<Task> LateLiveAccept, DateTimeOffset T0)> TestLiveTestFlapAsync(Ctx c)
    {
        await ConnectAsync(c, live: false);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0);
        Assert.Equal(EDocumentStatus.TestGonderildi, (await DocAsync(c, id)).Durum);

        await SwitchAsync(c, live: true);
        using (var s = c.Scope()) await s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(id);
        EInvoiceSubmission? pending = null;
        EInvoiceCredentials? creds = null;
        c.Fake.OnSend = sub => { pending = sub; creds = c.Fake.LastCredentials; throw new TaskCanceledException("İstemci zaman aşımı."); };
        await DispatchAsync(c, t0.AddMinutes(1));
        c.Fake.OnSend = null;
        Assert.Equal((bool?)true, (await DocAsync(c, id)).SonDenemeCanli);
        await SwitchAsync(c, live: false);
        return (id, () => c.Fake.SendAsync(creds! with { CanliOrtam = true }, pending!), t0);
    }

    [Fact]
    public async Task M1_test_live_test_flap_does_not_count_the_old_test_acceptance_and_refund_never_orphans_the_live_document()
    {
        using var c = New();
        var (id, lateLiveAccept, t0) = await TestLiveTestFlapAsync(c);

        await DispatchAsync(c, t0.AddMinutes(10)); // canlı deneme 9 dk önce: kesinleşmedi
        var waiting = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderilecek, waiting.Durum);
        Assert.StartsWith(NotSettledPrefix, waiting.SonMesaj, StringComparison.Ordinal);

        await lateLiveAccept(); // sunucu canlıda geç kabul
        await RefundAsync(c, id);
        Assert.NotEqual(EDocumentStatus.Iptal, (await DocAsync(c, id)).Durum);

        await DispatchAsync(c, t0.AddMinutes(20)); // canlıda bulunur
        var doc = await DocAsync(c, id);
        Assert.Equal(EDocumentStatus.Gonderildi, doc.Durum);
        Assert.True(c.Fake.AcceptedLive(doc.Ettn));
        Assert.Empty(c.Fake.Cancelled);
    }

    [Fact]
    public async Task M1_same_flap_local_cancel_is_refused_until_the_live_attempt_settles()
    {
        using var c = New();
        var (id, lateLiveAccept, _) = await TestLiveTestFlapAsync(c);

        Assert.StartsWith("red: " + NotSettledPrefix, await CancelAsync(c, id, "Vazgeçildi"), StringComparison.Ordinal);
        await lateLiveAccept();
        Assert.NotEqual(EDocumentStatus.Iptal, (await DocAsync(c, id)).Durum);
    }

    [Fact]
    public async Task M1_test_sent_document_with_an_unsettled_live_attempt_is_not_cancelled_locally_or_by_refund()
    {
        using var c = New();
        await ConnectAsync(c, live: false);
        var id = await InvoiceAsync(c, await IndividualAsync(c));
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        // Savunma katmanı: TestGonderildi ama son deneme kesinleşmemiş canlı deneme (durum elle kurulur).
        await DbAsync(c, db => db.EDocuments.Where(d => d.InvoiceId == id)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.SonDenemeCanli, true).SetProperty(d => d.SonDenemeUtc, DateTimeOffset.UtcNow)));

        Assert.StartsWith("red: " + NotSettledPrefix, await CancelAsync(c, id, "Vazgeçildi"), StringComparison.Ordinal);
        await RefundAsync(c, id);
        Assert.Equal(EDocumentStatus.TestGonderildi, (await DocAsync(c, id)).Durum); // iade dokunmadı

        await AgeLastAttemptAsync(c, id, Settle + TimeSpan.FromMinutes(1));
        Assert.Equal("Iptal", await CancelAsync(c, id, "Vazgeçildi"));
    }

    // ===================================================================== N4 — maskeleme ayraçları

    [Theory]
    [InlineData(" - ")]
    [InlineData("/")]
    [InlineData(" ")]          // NBSP
    [InlineData(" ")]          // dar NBSP
    [InlineData(" / ")]
    [InlineData(". ")]
    [InlineData("–")]          // en-dash (#410 L3)
    [InlineData(" – ")]
    [InlineData("_")]
    [InlineData(",")]
    [InlineData(" , ")]
    [InlineData("​")]          // sıfır genişlikli boşluk
    public void N4_tckn_with_unusual_separators_is_masked(string sep)
    {
        var t = TestNationalId.New();
        var grouped = string.Join(sep, t[..3], t[3..6], t[6..9], t[9..]);
        var masked = EDocumentPolicy.Sanitize($"Alıcı {grouped} bulunamadı")!;
        Assert.Equal("Alıcı *********** bulunamadı", masked);
        Assert.Equal("Alıcı *********** bulunamadı", EDocumentPolicy.Sanitize($"Alıcı {grouped} bulunamadı", t)); // bilinen sır yolu
    }

    [Theory]
    [InlineData("Fatura RNT2026000000001 reddedildi")]
    [InlineData("Tarih 15.10.2026 08:30:15 geçersiz")]
    [InlineData("Tutar 1.250,50 TL tutmuyor")]
    [InlineData("ETTN 3F2504E0-4F89-11D3-9A0C-0305E82C3301 bulunamadı")]
    [InlineData("VKN 1234567890 kayıtlı değil")]
    [InlineData("VKN 123 456 78 90, tarih 2026-10-17 kalır")]
    public void N4_non_personal_numbers_are_kept(string text) => Assert.Equal(text, EDocumentPolicy.Sanitize(text));

    // ===================================================================== E5 — e-Fatura / e-Arşiv ayrı seri

    [Fact]
    public async Task E5_e_invoice_and_e_archive_get_numbers_from_separate_series_independent_of_the_invoice_number()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.RegisteredRecipients.Add("9876543210");
        var corporate = await CorporateAsync(c, "9876543210");
        var individual = await IndividualAsync(c);
        var e1 = await InvoiceAsync(c, corporate);
        var a1 = await InvoiceAsync(c, individual);
        var a2 = await InvoiceAsync(c, individual, 500m);
        var refunded = await InvoiceAsync(c, individual, 300m);
        await RefundAsync(c, refunded); // iç numara tüketir, GİB serisinde numara tüketmez
        await DispatchAsync(c, DateTimeOffset.UtcNow);

        var year = await YearAsync(c, e1);
        var sent = c.Fake.Accepted.Values.ToDictionary(x => x.Ettn, x => (x.Tur, x.FaturaNo));
        async Task<(EDocumentKind, string)> Sent(Guid id) => sent[(await DocAsync(c, id)).Ettn];
        Assert.Equal((EDocumentKind.EFatura, $"EFA{year}000000001"), await Sent(e1));
        Assert.Equal((EDocumentKind.EArsiv, $"EAR{year}000000001"), await Sent(a1));
        Assert.Equal((EDocumentKind.EArsiv, $"EAR{year}000000002"), await Sent(a2)); // boşluksuz
        Assert.Equal(3, sent.Count);

        // XML'deki numara = gönderilen numara = kayıttaki numara; iç fatura numarası (RNT…) ayrı kalır.
        foreach (var id in new[] { e1, a1, a2 })
        {
            var doc = await DocAsync(c, id);
            var sub = c.Fake.Accepted[doc.Ettn];
            Assert.Equal(sub.FaturaNo, XDocument.Parse(Encoding.UTF8.GetString(sub.Xml)).Root!.Element(UblInvoiceBuilder.Cbc + "ID")!.Value);
            Assert.Equal(sub.FaturaNo, doc.BelgeNo);
            Assert.StartsWith("RNT", await DbAsync(c, db => db.Invoices.AsNoTracking().Where(i => i.Id == id).Select(i => i.No).SingleAsync()), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task E5_retry_reuses_the_number_and_test_environment_has_its_own_counter()
    {
        using var c = New();
        await ConnectAsync(c, live: false);
        var individual = await IndividualAsync(c);
        var a = await InvoiceAsync(c, individual);
        c.Fake.OnSend = _ => new EInvoiceSendResult(EInvoiceOutcome.GeciciHata, null, null, "Servis geçici olarak kapalı");
        var t0 = DateTimeOffset.UtcNow;
        await DispatchAsync(c, t0);
        c.Fake.OnSend = null;
        await DispatchAsync(c, t0.AddMinutes(6)); // yeniden deneme: aynı numara
        var year = await YearAsync(c, a);
        Assert.Equal([$"EAR{year}000000001", $"EAR{year}000000001"], c.Fake.SendCalls.Select(x => x.FaturaNo).ToArray());
        Assert.Equal(EDocumentStatus.TestGonderildi, (await DocAsync(c, a)).Durum);

        // Canlıya geç: test gönderimi canlı serisinde numara tüketmemiş → canlıdaki ilk numara 1.
        await SwitchAsync(c, live: true);
        using (var s = c.Scope()) await s.ServiceProvider.GetRequiredService<EDocumentService>().QueueAsync(a);
        var b = await InvoiceAsync(c, individual, 500m);
        await DispatchAsync(c, t0.AddMinutes(7));
        var docA = await DocAsync(c, a);
        var docB = await DocAsync(c, b);
        Assert.Equal((EDocumentStatus.Gonderildi, $"EAR{year}000000001", (bool?)true), (docA.Durum, docA.BelgeNo, docA.SonDenemeCanli));
        Assert.Equal((EDocumentStatus.Gonderildi, $"EAR{year}000000002", (bool?)true), (docB.Durum, docB.BelgeNo, docB.SonDenemeCanli));
    }

    /// <summary>#410 adversarial L1 (P5): e-Arşiv numarası vermiş seri sonradan e-Fatura serisi yapılamaz (ve tersi);
    /// üç kod farklı olsa da aynı sayaç iki türü karıştırırdı. Alan hatası, ayar değişmez.</summary>
    [Fact]
    public async Task E5_a_series_that_issued_numbers_for_one_kind_cannot_be_swapped_to_the_other_kind()
    {
        using var c = New();
        await ConnectAsync(c);
        c.Fake.RegisteredRecipients.Add("9876543210");
        var a1 = await InvoiceAsync(c, await IndividualAsync(c));
        var e1 = await InvoiceAsync(c, await CorporateAsync(c, "9876543210"));
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        var year = await YearAsync(c, a1);
        Assert.Equal($"EAR{year}000000001", (await DocAsync(c, a1)).BelgeNo);
        Assert.Equal($"EFA{year}000000001", (await DocAsync(c, e1)).BelgeNo);

        using var s = c.Scope();
        var settings = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
        var toEInvoice = await Assert.ThrowsAsync<ValidationException>(() => settings.SaveAsync(Settings(null, true, "EAR", "XAR")));
        Assert.Equal("eFaturaSeriKodu", toEInvoice.Alan);
        Assert.StartsWith("e-Fatura seri kodu EAR daha önce e-Arşiv belgelerinde kullanıldı", toEInvoice.Message, StringComparison.Ordinal);
        var toArchive = await Assert.ThrowsAsync<ValidationException>(() => settings.SaveAsync(Settings(null, true, "XFA", "EFA")));
        Assert.Equal("eArsivSeriKodu", toArchive.Alan);
        var saved = await settings.GetAsync();
        Assert.Equal(((string?)null, (string?)null), (saved.EFaturaSeriKodu, saved.EArsivSeriKodu)); // değişmedi

        await settings.SaveAsync(Settings(null, true, "XFA", "XAR")); // kullanılmamış yeni seriler serbest
    }

    [Fact]
    public async Task E5_series_codes_are_configurable_and_must_differ_from_each_other_and_from_the_invoice_series()
    {
        using var c = New();
        using (var s = c.Scope())
        {
            var settings = s.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var secret = "S" + Guid.NewGuid().ToString("N");
            var same = await Assert.ThrowsAsync<ValidationException>(() => settings.SaveAsync(Settings(secret, true, "ABC", "ABC")));
            Assert.Equal("eArsivSeriKodu", same.Alan);
            var internalSeries = await Assert.ThrowsAsync<ValidationException>(() => settings.SaveAsync(Settings(secret, true, "RNT", null)));
            Assert.Equal("eFaturaSeriKodu", internalSeries.Alan); // iç fatura serisi (varsayılan RNT) ile çakışma
            var bad = await Assert.ThrowsAsync<ValidationException>(() => settings.SaveAsync(Settings(secret, true, null, "Ç12")));
            Assert.Equal("eArsivSeriKodu", bad.Alan);
            await settings.SaveAsync(Settings(secret, true, "kfa", "KAR")); // küçük harf büyütülür
            var saved = await settings.GetAsync();
            Assert.Equal(("KFA", "KAR"), (saved.EFaturaSeriKodu, saved.EArsivSeriKodu));
        }
        c.Fake.RegisteredRecipients.Add("9876543210");
        var e1 = await InvoiceAsync(c, await CorporateAsync(c, "9876543210"));
        var a1 = await InvoiceAsync(c, await IndividualAsync(c));
        await DispatchAsync(c, DateTimeOffset.UtcNow);
        var year = await YearAsync(c, e1);
        Assert.Equal($"KFA{year}000000001", (await DocAsync(c, e1)).BelgeNo);
        Assert.Equal($"KAR{year}000000001", (await DocAsync(c, a1)).BelgeNo);
    }
}
