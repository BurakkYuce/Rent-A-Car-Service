using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Integrations;
using RentACar.Application.Kabis;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.Infrastructure.Persistence.Repositories;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>Gönderim yapmadan sonucu kontrol edilebilir kılan sahte KABİS adaptörü (gerçek sisteme çıkılmaz).</summary>
public sealed class FakeKabis : IKabisService
{
    private int _n;
    public List<(KabisKimlik Kimlik, KabisKiralamaBildirimi Bildirim)> Rentals { get; } = [];
    public List<KabisIadeBildirimi> Returns { get; } = [];
    public List<(string Referans, string? DisReferans)> Cancels { get; } = [];
    /// <summary>Sıradaki sonuçlar; boşsa başarı + yeni dış referans.</summary>
    public Queue<KabisSonuc> Next { get; } = new();
    public Exception? Throw { get; set; }
    public int Calls => Rentals.Count + Returns.Count + Cancels.Count;

    private KabisSonuc Result()
    {
        if (Throw is { } e) throw e;
        return Next.Count > 0 ? Next.Dequeue() : new KabisSonuc(KabisSonucTuru.Basarili, "KBS-" + ++_n, null);
    }

    public Task<bool> NotifyAsync(KabisBildirim notification, CancellationToken ct = default) => Task.FromResult(false);

    public Task<KabisSonuc> NotifyRentalAsync(KabisKimlik credentials, KabisKiralamaBildirimi notice, CancellationToken ct = default)
    {
        Rentals.Add((credentials, notice));
        return Task.FromResult(Result());
    }

    public Task<KabisSonuc> NotifyReturnAsync(KabisKimlik credentials, KabisIadeBildirimi notice, CancellationToken ct = default)
    {
        Returns.Add(notice);
        return Task.FromResult(Result());
    }

    public Task<KabisSonuc> CancelAsync(KabisKimlik credentials, string reference, string? externalReference, string reason,
        CancellationToken ct = default)
    {
        Cancels.Add((reference, externalReference));
        return Task.FromResult(Result());
    }

    public Task<KabisSorguSonucu> QueryAsync(KabisKimlik credentials, string reference, CancellationToken ct = default)
        => Task.FromResult(new KabisSorguSonucu(KabisSonucTuru.Basarili, false, null, null));
}

/// <summary>Log satırlarını yakalayan kaydedici (PII taraması için).</summary>
public sealed class ListLogger : ILogger
{
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Lines.Add(formatter(state, exception) + " " + exception);
}

/// <summary>
/// KABİS iç akışı — kuyruk kaydı, gönderim, yeniden deneme, idempotency, dürüst stub, PII, kiracı izolasyonu ve şube
/// kapsamı. GERÇEK PostgreSQL (racar_app, RLS).
///
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen değerler elle kurulan senaryodan gelir ("teslimde TEK Kiralama kaydı",
/// "geçici hatada 1. deneme sonrası Bekliyor + deneme 1", "8. geçici hatadan sonra Hata", "bağlı değilken deneme 0",
/// "hata metninde TC yok"); servis/politika kodundan türetilmez. Dış referanslar sahte adaptörün ürettiği
/// <c>KBS-n</c> değerleridir.</para>
/// </summary>
[Collection("postgres")]
public sealed class KabisBildirimTests(PostgresFixture fx)
{
    // Geçerli (algoritmik) test TC'si — gerçek kişiye ait değil, depodaki diğer testlerle aynı numara.
    private const string Tc = "10000000146";
    private const string Passport = "U98765432";
    private const string License = "B7654321";
    private static readonly string Password = "P" + Guid.NewGuid().ToString("N"); // sabit kimlik bilgisi YOK

    private static DateTimeOffset Start => new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-1).AddHours(9);

    private (TestHost Host, FakeKabis Fake) Setup(bool stub = false)
    {
        var fake = new FakeKabis();
        var host = stub
            ? new TestHost(fx.AppConnectionString)
            : new TestHost(fx.AppConnectionString, s => s.AddSingleton<IKabisService>(fake));
        return (host, fake);
    }

    private static async Task EnableAsync(TestHost host, Guid tenant, bool block = false, bool credentials = true)
    {
        using var s = host.ScopeFor(tenant);
        var svc = s.ServiceProvider.GetRequiredService<KabisSettingsService>();
        var (_, version) = await svc.GetAsync();
        await svc.SaveAsync(new KabisSettingsInput
        {
            Etkin = true, Kurum = KabisAuthority.Emniyet,
            Kullanici = credentials ? "firma01" : null, Sifre = credentials ? Password : null, TeslimiEngelle = block,
        }, version);
    }

    private static async Task<Guid> RentalAsync(TestHost host, Guid tenant, string? office = null, string? tc = Tc)
    {
        using var s = host.ScopeFor(tenant);
        var sp = s.ServiceProvider;
        var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
        {
            Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Demir", TcKimlik = tc, PasaportNo = Passport, EhliyetNo = License,
        });
        var vehicle = await TestVehicle.NewAsync(sp);
        return await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = Start, BitTar = Start.AddDays(3), GunlukUcret = 100m,
            CikisOfisi = office,
        });
    }

    private static async Task DeliverAsync(TestHost host, Guid tenant, Guid rental)
    {
        using var s = host.ScopeFor(tenant);
        Assert.True(await s.ServiceProvider.GetRequiredService<RentalService>().DeliverAsync(rental, 1000, 8));
    }

    private static async Task ReturnAsync(TestHost host, Guid tenant, Guid rental)
    {
        using var s = host.ScopeFor(tenant);
        Assert.True(await s.ServiceProvider.GetRequiredService<RentalService>()
            .ReturnAsync(rental, 1200, 8, TestZaman.Now()));
    }

    private static async Task<List<KabisNotification>> RowsAsync(TestHost host, Guid tenant, Guid? rental = null)
    {
        using var s = host.ScopeFor(tenant);
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await db.KabisBildirimleri.AsNoTracking().Where(x => rental == null || x.KiraId == rental).ToListAsync();
    }

    private static DbContextOptions<AppDbContext> RawOptions(string cs) => new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options;

    /// <summary>İşin GERÇEK yolu: ön kontrol + yalıtılmış adım + koşu günlüğü (KabisRetryJob.RunTenantAsync).</summary>
    private async Task<int> RunJobAsync(TestHost host, Guid tenant, DateTimeOffset now, IKabisService port, ILogger? log = null)
    {
        using var s = host.ScopeFor(tenant);
        return await RentACar.Web.Jobs.KabisRetryJob.RunTenantAsync(RawOptions(fx.AppConnectionString), tenant, now, port,
            s.ServiceProvider.GetRequiredService<ISecretProtector>(), log ?? new ListLogger(), CancellationToken.None);
    }

    // ------------------------------------------------------------------ kuyruk + başarı

    [Fact]
    public async Task Kabis_kapaliyken_teslimde_kuyruk_kaydi_olusmaz_ve_adaptor_cagrilmaz()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        var rental = await RentalAsync(host, tenant);

        await DeliverAsync(host, tenant, rental);
        await ReturnAsync(host, tenant, rental);

        Assert.Empty(await RowsAsync(host, tenant));
        Assert.Equal(0, fake.Calls);
        using var s = host.ScopeFor(tenant);
        Assert.Null(await s.ServiceProvider.GetRequiredService<KabisNotificationService>().ProblemCountAsync());
    }

    [Fact]
    public async Task Teslimde_tek_kiralama_kaydi_olusur_ve_sahte_adaptorle_bildirilir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);

        await DeliverAsync(host, tenant, rental);

        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeType.Kiralama, row.Tur);
        Assert.Equal(KabisNoticeStatus.Bildirildi, row.Durum);
        Assert.Equal("KBS-1", row.DisReferans);
        Assert.Equal(1, row.DenemeSayisi);
        Assert.Null(row.SonHata);
        Assert.Equal(KabisAuthority.Emniyet, row.Kurum);

        // Yük bellekte çözülmüş kimlikle gitti (şifreli cari alanından), kimlik bilgisi ayardan çözüldü.
        var (credentials, notice) = Assert.Single(fake.Rentals);
        Assert.Equal("firma01", credentials.Kullanici);
        Assert.Equal(Password, credentials.Sifre);
        Assert.Equal(Tc, notice.Kiralayan!.TcKimlik);
        Assert.Equal(License, notice.Kiralayan.EhliyetNo);
        Assert.False(notice.KurumMu);

        // Kuyruk satırında kimlik verisi YOK (tüm kolonlar taranır).
        var json = JsonSerializer.Serialize(row);
        foreach (var pii in new[] { Tc, Passport, License, Password })
            Assert.DoesNotContain(pii, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Donuste_iade_kaydi_kuyruga_girer_ve_kiralama_referansiyla_bildirilir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        await DeliverAsync(host, tenant, rental);

        await ReturnAsync(host, tenant, rental);

        var rows = await RowsAsync(host, tenant, rental);
        Assert.Equal(2, rows.Count);
        var iade = Assert.Single(rows, r => r.Tur == KabisNoticeType.Iade);
        Assert.Equal(KabisNoticeStatus.Bildirildi, iade.Durum);
        Assert.Equal("KBS-2", iade.DisReferans);
        var sent = Assert.Single(fake.Returns);
        Assert.Equal("KBS-1", sent.DisReferans); // kiralama bildiriminin referansıyla
        Assert.Equal(1200, sent.DonusKm);
    }

    // ------------------------------------------------------------------ hata + yeniden deneme

    [Fact]
    public async Task Gecici_hatada_bekliyor_kalir_deneme_sayilir_ve_is_vadesinde_bildirir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null, "Sunucu meşgul"));

        await DeliverAsync(host, tenant, rental);

        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Bekliyor, row.Durum);
        Assert.Equal(1, row.DenemeSayisi);
        Assert.Equal("Sunucu meşgul", row.SonHata);
        Assert.NotNull(row.SonrakiDenemeUtc);

        // Vadesi gelmeden iş DOKUNMAZ (geri çekilme: ilk bekleme 1 dakika).
        Assert.Equal(0, await RunJobAsync(host, tenant, row.SonDenemeUtc!.Value.AddSeconds(30), fake));
        Assert.Single(fake.Rentals);

        // Vadesi gelince iş gönderir → Bildirildi.
        Assert.Equal(1, await RunJobAsync(host, tenant, row.SonDenemeUtc!.Value.AddMinutes(2), fake));
        row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Bildirildi, row.Durum);
        Assert.Equal(2, row.DenemeSayisi);
        Assert.Equal("KBS-1", row.DisReferans);
        Assert.Null(row.SonHata);
        Assert.Equal(2, fake.Rentals.Count);
    }

    [Fact]
    public async Task Deneme_hakki_bitince_Hata_olur_ve_artik_denenmez()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        for (var i = 0; i < 20; i++) fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null, "zaman aşımı"));

        await DeliverAsync(host, tenant, rental); // 1. deneme
        var clock = DateTimeOffset.UtcNow;
        for (var i = 0; i < 12; i++) // 8. denemeden sonra iş artık bir şey bulmaz
        {
            clock = clock.AddHours(5); // en uzun geri çekilme (4 saat) de geçsin
            await RunJobAsync(host, tenant, clock, fake);
        }

        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Hata, row.Durum);
        Assert.Equal(8, row.DenemeSayisi);
        Assert.Equal(8, fake.Rentals.Count);
        Assert.Null(row.SonrakiDenemeUtc);
    }

    [Fact]
    public async Task Kalici_hatada_Hata_olur_operator_yeniden_deneyince_bildirilir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.KaliciHata, null, "Kullanıcı yetkisiz"));

        await DeliverAsync(host, tenant, rental);
        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Hata, row.Durum);
        Assert.Equal("Kullanıcı yetkisiz", row.SonHata);

        // İş kalıcı hatayı tekrar DENEMEZ.
        Assert.Equal(0, await RunJobAsync(host, tenant, DateTimeOffset.UtcNow.AddDays(1), fake));
        Assert.Single(fake.Rentals);

        // Panel + ekran uyarısı.
        using (var s = host.ScopeFor(tenant))
        {
            var svc = s.ServiceProvider.GetRequiredService<KabisNotificationService>();
            Assert.Equal(1, await svc.ProblemCountAsync());
            var status = await svc.GetForRentalAsync(rental);
            Assert.Contains("yapılamadı", status.Uyari);
        }

        // Operatör sorunu giderip yeniden dener → hemen gönderilir.
        using (var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator))
        {
            var status = await op.ServiceProvider.GetRequiredService<KabisNotificationService>()
                .RetryAsync(rental, KabisNoticeType.Kiralama);
            var n = Assert.Single(status.Bildirimler);
            Assert.Equal(KabisNoticeStatus.Bildirildi, n.Durum);
            Assert.Null(status.Uyari);
            Assert.Equal(0, await op.ServiceProvider.GetRequiredService<KabisNotificationService>().ProblemCountAsync());
        }
    }

    [Fact]
    public async Task Iade_kiralama_bildirilmeden_gonderilmez_beklemede_kalir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null, "ağ"));
        await DeliverAsync(host, tenant, rental);

        await ReturnAsync(host, tenant, rental); // dönüş kiralamayı yeniden denemez; iade kaydı kiralamayı bekler

        var iade = Assert.Single(await RowsAsync(host, tenant, rental), r => r.Tur == KabisNoticeType.Iade);
        Assert.Equal(KabisNoticeStatus.Bekliyor, iade.Durum);
        Assert.Equal(0, iade.DenemeSayisi);
        Assert.Equal(KabisNotificationService.WaitingForRental, iade.SonHata);
        Assert.Empty(fake.Returns);
    }

    // ------------------------------------------------------------------ idempotency

    [Fact]
    public async Task Idempotent_ayni_kira_ve_tur_icin_tek_kayit_tek_gonderim()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        await DeliverAsync(host, tenant, rental);

        using (var s = host.ScopeFor(tenant))
        {
            var repo = s.ServiceProvider.GetRequiredService<IKabisRepository>();
            // İkinci "aynı olay" kaydı ŞEMADA reddedilir (benzersiz index) → no-op.
            Assert.False(await repo.AddAsync(KabisNotificationService.NewRecord(
                rental, KabisNoticeType.Kiralama, KabisAuthority.Emniyet, DateTimeOffset.UtcNow)));
            // İkinci teslim imkânsız (araç zaten teslim edildi).
            await Assert.ThrowsAsync<ValidationException>(() =>
                s.ServiceProvider.GetRequiredService<RentalService>().DeliverAsync(rental, 1000, 8));
        }

        // İş iki kez koşsa da bildirilmiş kayıt yeniden gönderilmez.
        await RunJobAsync(host, tenant, DateTimeOffset.UtcNow.AddHours(1), fake);
        await RunJobAsync(host, tenant, DateTimeOffset.UtcNow.AddHours(2), fake);
        Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Single(fake.Rentals);
    }

    [Fact]
    public async Task Sahiplenme_atomiktir_ayni_satiri_iki_gonderici_alamaz()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null, "ağ"));
        await DeliverAsync(host, tenant, rental);
        var row = Assert.Single(await RowsAsync(host, tenant, rental));

        using var s = host.ScopeFor(tenant);
        var repo = s.ServiceProvider.GetRequiredService<IKabisRepository>();
        var due = row.SonrakiDenemeUtc!.Value.AddMinutes(1);
        var claims = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => repo.TryClaimAsync(row.Id, due, KabisRetryPolicy.Lease, force: false)));
        Assert.Equal(1, claims.Count(c => c));
        // Sahiplenme sürerken zorla deneme de alamaz (sürmekte olan gönderim).
        Assert.False(await repo.TryClaimAsync(row.Id, due, KabisRetryPolicy.Lease, force: true));
    }

    // ------------------------------------------------------------------ dürüst stub

    [Fact]
    public async Task Stub_yapilandirma_yokken_bagli_degil_kalir_sahte_basari_ve_deneme_yok()
    {
        var (host, _) = Setup(stub: true); // GERÇEK DI kaydı: StubKabisService
        using var __ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);

        await DeliverAsync(host, tenant, rental);
        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Bekliyor, row.Durum);
        Assert.Contains("bağlı değil", row.SonHata);
        Assert.Null(row.DisReferans);
        Assert.Null(row.BildirimUtc);
        Assert.Equal(0, row.DenemeSayisi); // deneme hakkı yanmaz

        // İş saatlerce dönse de hiçbir zaman "Bildirildi" olmaz, Hata'ya da düşmez.
        using (var s = host.ScopeFor(tenant))
        {
            var port = s.ServiceProvider.GetRequiredService<IKabisService>();
            Assert.IsType<RentACar.Infrastructure.Integrations.StubKabisService>(port);
            var clock = DateTimeOffset.UtcNow;
            for (var i = 0; i < 5; i++) await RunJobAsync(host, tenant, clock = clock.AddHours(2), port);
        }
        row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Bekliyor, row.Durum);
        Assert.Equal(0, row.DenemeSayisi);
        Assert.Null(row.DisReferans);
        using (var s = host.ScopeFor(tenant))
            Assert.Equal(1, await s.ServiceProvider.GetRequiredService<KabisNotificationService>().ProblemCountAsync());
    }

    [Fact]
    public async Task Kimlik_bilgisi_yokken_adaptor_cagrilmaz_bagli_degil_notu_yazilir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant, credentials: false);
        var rental = await RentalAsync(host, tenant);

        await DeliverAsync(host, tenant, rental);

        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Bekliyor, row.Durum);
        Assert.Equal(KabisCredentials.Missing, row.SonHata);
        Assert.Equal(0, row.DenemeSayisi);
        Assert.Equal(0, fake.Calls);
    }

    // ------------------------------------------------------------------ teslimi engelleme

    [Fact]
    public async Task Engelleyici_modda_bildirim_yapilamazsa_teslim_reddedilir_basarida_teslim_edilir()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant, block: true);
        var rental = await RentalAsync(host, tenant);
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null, "servis kapalı"));

        using (var s = host.ScopeFor(tenant))
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                s.ServiceProvider.GetRequiredService<RentalService>().DeliverAsync(rental, 1000, 8));
            Assert.Contains("teslim engellendi", ex.Message);
            Assert.Contains("servis kapalı", ex.Message);
            Assert.Equal("kabis", ex.Alan);
            Assert.Null((await s.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rental))!.CikisKm);
        }

        // Operatör tezgâhta tekrar dener — geri çekilme beklenmez, karşı sistem kabul eder → teslim yapılır.
        await DeliverAsync(host, tenant, rental);
        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Bildirildi, row.Durum);
        Assert.Equal(2, fake.Rentals.Count);
        using (var s = host.ScopeFor(tenant))
            Assert.Equal(1000, (await s.ServiceProvider.GetRequiredService<RentalService>().GetAsync(rental))!.CikisKm);
    }

    [Fact]
    public async Task Engelleyici_modda_gecersiz_teslim_icin_bildirim_gitmez()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant, block: true);
        var rental = await RentalAsync(host, tenant);

        using var s = host.ScopeFor(tenant);
        await Assert.ThrowsAsync<ValidationException>(() =>
            s.ServiceProvider.GetRequiredService<RentalService>().DeliverAsync(rental, -5, 8)); // negatif km
        Assert.Equal(0, fake.Calls);
        Assert.Empty(await RowsAsync(host, tenant, rental));
    }

    // ------------------------------------------------------------------ iptal + operatör kararları

    [Fact]
    public async Task Bildirilmis_kira_iptal_edilince_iptal_bildirimi_gider_bildirilmemiste_gerekli_degil_olur()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);

        var reported = await RentalAsync(host, tenant);
        await DeliverAsync(host, tenant, reported);
        var pending = await RentalAsync(host, tenant, tc: null); // ikinci cari: yabancı uyruklu (pasaportla) — TC tenant'ta benzersiz
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null, "ağ"));
        await DeliverAsync(host, tenant, pending);

        using (var s = host.ScopeFor(tenant))
        {
            var rentals = s.ServiceProvider.GetRequiredService<RentalService>();
            Assert.True(await rentals.CancelAsync(reported));
            Assert.True(await rentals.CancelAsync(pending));
        }

        var cancel = Assert.Single(await RowsAsync(host, tenant, reported), r => r.Tur == KabisNoticeType.Iptal);
        Assert.Equal(KabisNoticeStatus.Bildirildi, cancel.Durum);
        Assert.Equal("KBS-1", Assert.Single(fake.Cancels).DisReferans);

        var closed = Assert.Single(await RowsAsync(host, tenant, pending));
        Assert.Equal(KabisNoticeStatus.GerekliDegil, closed.Durum);
        Assert.Equal(KabisNotificationService.CancelledNote, closed.Gerekce);
    }

    [Fact]
    public async Task Operator_gerekli_degil_ve_elle_bildirildi_isaretleri_gerekceyle_kaydolur()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var a = await RentalAsync(host, tenant);
        var b = await RentalAsync(host, tenant, tc: null); // ikinci cari pasaportla (TC tenant'ta benzersiz)
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.KaliciHata, null, "red"));
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.KaliciHata, null, "red"));
        await DeliverAsync(host, tenant, a);
        await DeliverAsync(host, tenant, b);

        var user = Guid.NewGuid();
        using var s = host.ScopeFor(tenant, user, "op", UserRole.Operator);
        var svc = s.ServiceProvider.GetRequiredService<KabisNotificationService>();
        var empty = await Assert.ThrowsAsync<ValidationException>(() => svc.MarkNotRequiredAsync(a, KabisNoticeType.Kiralama, "  "));
        Assert.Equal("gerekce", empty.Alan);

        var st = await svc.MarkNotRequiredAsync(a, KabisNoticeType.Kiralama, "Kamu kurumu sözleşmesi, portaldan girildi");
        Assert.Equal(KabisNoticeStatus.GerekliDegil, Assert.Single(st.Bildirimler).Durum);

        st = await svc.MarkManuallyReportedAsync(b, KabisNoticeType.Kiralama, "PORTAL-778");
        var n = Assert.Single(st.Bildirimler);
        Assert.Equal(KabisNoticeStatus.Bildirildi, n.Durum);
        Assert.True(n.Elle);
        Assert.Equal("PORTAL-778", n.DisReferans);

        var row = Assert.Single(await RowsAsync(host, tenant, b));
        Assert.Equal(user, row.IsaretleyenKullaniciId);
        Assert.Equal(0, await svc.ProblemCountAsync());
        // Kapanmış kayıt tekrar işaretlenemez.
        await Assert.ThrowsAsync<ValidationException>(() => svc.MarkNotRequiredAsync(b, KabisNoticeType.Kiralama, "x"));
    }

    [Fact]
    public async Task Eksik_kimlikte_kalici_hata_ve_acik_mesaj_adaptor_cagrilmaz()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        Guid rental;
        using (var s = host.ScopeFor(tenant))
        {
            var sp = s.ServiceProvider;
            var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
            { Tip = CustomerType.Bireysel, Ad = "Kimliksiz", Soyad = "Müşteri" });
            rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            { MusteriId = customer, VehicleId = await TestVehicle.NewAsync(sp), BasTar = Start, BitTar = Start.AddDays(2), GunlukUcret = 100m });
        }

        await DeliverAsync(host, tenant, rental);

        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Equal(KabisNoticeStatus.Hata, row.Durum);
        Assert.Contains("T.C. kimlik ya da pasaport", row.SonHata);
        Assert.Equal(0, row.DenemeSayisi); // karşı sisteme gidilmedi
        Assert.Equal(0, fake.Calls);
    }

    // ------------------------------------------------------------------ PII

    [Fact]
    public async Task Kimlik_ve_sifre_hata_metninde_koşu_gunlugunde_logda_ve_denetimde_yok()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant);
        // Karşı sistem gönderilen değerleri hata cümlesinde YANKILIYOR (en kötü durum).
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.GeciciHata, null,
            $"Kişi {Tc} / pasaport {Passport} / ehliyet {License} doğrulanamadı; oturum {Password} reddedildi"));

        await DeliverAsync(host, tenant, rental);

        var row = Assert.Single(await RowsAsync(host, tenant, rental));
        foreach (var pii in new[] { Tc, Passport, License, Password })
            Assert.DoesNotContain(pii, row.SonHata!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", row.SonHata);
        Assert.Contains("doğrulanamadı", row.SonHata); // cümlenin anlamı korunur

        // İstisna yolu (URL/gövde yankısı) — işin GERÇEK yolu, yakalanan log satırları ve koşu günlüğü.
        fake.Throw = new HttpRequestException($"POST https://kabis.ornek/bildir?tc={Tc}&pasaport={Passport} başarısız");
        var log = new ListLogger();
        Assert.Equal(1, await RunJobAsync(host, tenant, row.SonrakiDenemeUtc!.Value.AddMinutes(1), fake, log));
        row = Assert.Single(await RowsAsync(host, tenant, rental));
        Assert.Contains("HttpRequestException", row.SonHata);
        foreach (var pii in new[] { Tc, Passport, License, Password })
        {
            Assert.DoesNotContain(pii, row.SonHata!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(log.Lines, l => l.Contains(pii, StringComparison.OrdinalIgnoreCase));
        }

        using var s = host.ScopeFor(tenant);
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var jobLogs = await db.JobCalismaLoglari.AsNoTracking()
            .Where(x => x.JobAdi == JobRunRecorder.KabisNotification).ToListAsync();
        Assert.NotEmpty(jobLogs); // iş varken günlüğe yazıldı
        var audits = await db.AuditLogs.AsNoTracking().ToListAsync();
        foreach (var pii in new[] { Tc, Passport, License, Password })
        {
            Assert.DoesNotContain(jobLogs, l => (l.Detay ?? "").Contains(pii, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(audits, a => (a.OldValues + a.NewValues).Contains(pii, StringComparison.OrdinalIgnoreCase));
        }
        // KABİS şifre cipher'ı da denetime düz yazılmaz — maskeli.
        Assert.Contains(audits, a => a.EntityName == "Ayarlar" && a.NewValues is { } v
            && JsonDocument.Parse(v).RootElement.TryGetProperty("KabisSifreEnc", out var p) && p.GetString() == "***");
    }

    // ------------------------------------------------------------------ kiracı izolasyonu + şube kapsamı

    [Fact]
    public async Task Kiraci_izolasyonu_baska_kiracinin_kaydi_gorunmez_ve_islenemez()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await EnableAsync(host, a);
        await EnableAsync(host, b);
        var rental = await RentalAsync(host, a);
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.KaliciHata, null, "red"));
        await DeliverAsync(host, a, rental);

        Assert.Single(await RowsAsync(host, a));
        Assert.Empty(await RowsAsync(host, b)); // racar_app + RLS: B'nin GUC'u A'nın satırını görmez

        using (var s = host.ScopeFor(b))
        {
            var svc = s.ServiceProvider.GetRequiredService<KabisNotificationService>();
            Assert.Equal(0, await svc.ProblemCountAsync());
            Assert.Empty(await svc.ListProblemsAsync());
            await Assert.ThrowsAsync<ValidationException>(() => svc.GetForRentalAsync(rental));
            await Assert.ThrowsAsync<ValidationException>(() => svc.RetryAsync(rental, KabisNoticeType.Kiralama));
            Assert.Null(await s.ServiceProvider.GetRequiredService<IKabisRepository>().FindAsync(rental, KabisNoticeType.Kiralama));
        }

        // B'nin işi A'nın satırına DOKUNMAZ.
        Assert.Equal(0, await RunJobAsync(host, b, DateTimeOffset.UtcNow.AddDays(1), fake));
        Assert.Single(fake.Rentals);

        // Doğrudan racar_app + B GUC'uyla ham sorgu da 0 satır görür (RLS, query filter'dan bağımsız).
        var sys = new SystemTenantContext { TenantId = b };
        await using var raw = new AppDbContext(RawOptions(fx.AppConnectionString), sys, sys);
        await TenantGuc.OpenAsync(raw, b);
        Assert.Equal(0, await raw.KabisBildirimleri.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Sube_kapsami_operator_baska_subenin_kabis_kaydini_goremez_ve_isleyemez()
    {
        var (host, fake) = Setup();
        using var _ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);
        var rental = await RentalAsync(host, tenant, office: "Ankara");
        fake.Next.Enqueue(new KabisSonuc(KabisSonucTuru.KaliciHata, null, "red"));
        await DeliverAsync(host, tenant, rental);

        using (var other = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez"))
        {
            var svc = other.ServiceProvider.GetRequiredService<KabisNotificationService>();
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.GetForRentalAsync(rental));
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.RetryAsync(rental, KabisNoticeType.Kiralama));
            await Assert.ThrowsAsync<NoPermissionException>(() =>
                svc.MarkNotRequiredAsync(rental, KabisNoticeType.Kiralama, "deneme"));
            Assert.Equal(0, await svc.ProblemCountAsync());
            Assert.Empty(await svc.ListProblemsAsync());
        }

        using (var own = host.ScopeFor(tenant, Guid.NewGuid(), "op2", UserRole.Operator, assignedBranch: "Ankara"))
        {
            var svc = own.ServiceProvider.GetRequiredService<KabisNotificationService>();
            Assert.Equal(1, await svc.ProblemCountAsync());
            Assert.Equal(rental, Assert.Single(await svc.ListProblemsAsync()).KiraId);
            Assert.Single((await svc.GetForRentalAsync(rental)).Bildirimler);
        }

        // Muhasebe (FinanceWrite) okur ama işlem yapamaz.
        using (var acc = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe))
        {
            var svc = acc.ServiceProvider.GetRequiredService<KabisNotificationService>();
            Assert.Single((await svc.GetForRentalAsync(rental)).Bildirimler);
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.RetryAsync(rental, KabisNoticeType.Kiralama));
        }
    }

    // ------------------------------------------------------------------ ayarlar

    [Fact]
    public async Task Ayarlar_sifre_sifreli_saklanir_gorunumde_donmez_hedef_degisince_yeniden_istenir()
    {
        var (host, _) = Setup();
        using var __ = host;
        var tenant = Guid.NewGuid();
        await EnableAsync(host, tenant);

        using var s = host.ScopeFor(tenant);
        var svc = s.ServiceProvider.GetRequiredService<KabisSettingsService>();
        var (view, version) = await svc.GetAsync();
        Assert.True(view.Etkin);
        Assert.True(view.SifreTanimli);
        Assert.Equal("firma01", view.Kullanici);
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(view), StringComparison.Ordinal);

        await using (var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var cipher = await db.TenantSettings.AsNoTracking().Select(x => x.KabisSifreEnc).SingleAsync();
            Assert.False(string.IsNullOrEmpty(cipher));
            Assert.NotEqual(Password, cipher);
        }

        // Kullanıcı değişip şifre boş bırakılırsa kayıtlı şifre yeni hedefe TAŞINMAZ (M3 deseni).
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(new KabisSettingsInput
        { Etkin = true, Kurum = KabisAuthority.Jandarma, Kullanici = "baska", TeslimiEngelle = false }, version));
        Assert.Equal("sifre", ex.Alan);

        // Bayat sürüm → çakışma.
        await svc.SaveAsync(new KabisSettingsInput { Etkin = true, Kurum = KabisAuthority.Emniyet, Kullanici = "firma01" }, version);
        await Assert.ThrowsAsync<ConcurrentModificationException>(() => svc.SaveAsync(new KabisSettingsInput
        { Etkin = false, Kurum = KabisAuthority.Emniyet, Kullanici = "firma01" }, version));

        // Operatör ayarı okuyamaz.
        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        await Assert.ThrowsAsync<NoPermissionException>(() => op.ServiceProvider.GetRequiredService<KabisSettingsService>().GetAsync());
    }

    [Fact]
    public async Task Baglanti_testi_stubta_basari_dondurmez()
    {
        var (host, _) = Setup(stub: true);
        using var __ = host;
        var tenant = Guid.NewGuid();
        using (var s = host.ScopeFor(tenant))
        {
            var none = await s.ServiceProvider.GetRequiredService<KabisSettingsService>().TestConnectionAsync();
            Assert.False(none.Ok);
            Assert.Equal(KabisCredentials.Missing, none.Mesaj);
        }
        await EnableAsync(host, tenant);
        using (var s = host.ScopeFor(tenant))
        {
            var r = await s.ServiceProvider.GetRequiredService<KabisSettingsService>().TestConnectionAsync();
            Assert.False(r.Ok);
            Assert.Contains("bağlı değil", r.Mesaj);
        }
    }
}
