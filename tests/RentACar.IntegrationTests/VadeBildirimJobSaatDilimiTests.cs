using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.Jobs;
using RentACar.Application.Notifications;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// VadeBildirimJob zinciri — saat dilimi regresyonu.
///
/// <para><b>Hata:</b> <c>MusteriBildirimUretici</c> İstanbul gün sınırlarını +03:00 ofsetli
/// <c>DateTimeOffset</c> olarak kurup SORGU PARAMETRESİ yapıyordu. Npgsql 6+ <c>timestamptz</c>'ye
/// yalnız Offset=0 yazar → her koşuda <c>ArgumentException</c> → teslim/iade hatırlatmaları hiç
/// üretilmedi; istisna job'ın tenant bloğundan çıktığı için aynı tenant'ın WhatsApp günlük özeti de
/// atlandı. Log satırı "Vade bildirim: tenant … taraması atlandı" dediği için vade bildirimi sanıldı.</para>
///
/// <para><b>Eski testler neden yakalamadı:</b> <c>MusteriBildirimUretici.RunAsync</c>'i çağıran HİÇBİR
/// test yoktu — <c>MusteriBildirimTests</c> yalnız servisi, <c>JobCalismaLogTests</c> zincirin ilk iki
/// halkasını (Vade + Filo) koşuyor. Vade/Filo testleri ise UTC <c>now</c> veriyor.</para>
///
/// <para>Testler job'ın GERÇEK yolunu kurar: interceptor'sız options + <c>SystemTenantContext</c> +
/// <c>TenantGuc</c> + <c>JobCalismaKaydedici</c> sarmalayıcısı; parametreler üretimdekiyle aynı biçimde
/// (now UTC, tz İstanbul).</para>
///
/// <para><b>BAĞIMSIZ ORACLE:</b> İstanbul = sabit UTC+3 (2016'dan beri yaz saati yok). Gün sınırları,
/// beklenen alıcılar ve idempotency anahtarları elle yazılmış sabitlerdir; üretim kodundan türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class VadeBildirimJobSaatDilimiTests(PostgresFixture fx)
{
    // Sabit +3 dilim: CI'da tzdata olmasa bile deterministik (WhatsAppOzetTests emsali).
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.CreateCustomTimeZone("ist3-vade-job", TimeSpan.FromHours(3), "İstanbul", "İstanbul");

    // 06.07.2026 22:30Z = İstanbul 07.07.2026 01:30 → İstanbul "bugün" 07.07, "yarın" 08.07.
    // UTC günü hâlâ 06.07: gün hesabı UTC'ye kaysa beklenen kayıtlar değişir (sınır da sınanır).
    private static readonly DateTimeOffset NowUtc = new(2026, 7, 6, 22, 30, 0, TimeSpan.Zero);
    private const string GunEtiketi = "2026-07-07";

    /// <summary>İstanbul duvar saati → DB'ye yazılabilir UTC an (elle: yerel − 3 saat).</summary>
    private static DateTimeOffset Ist3(int ay, int gun, int saat, int dk = 0)
        => new DateTimeOffset(2026, ay, gun, saat, dk, 0, TimeSpan.Zero).AddHours(-3);

    private DbContextOptions<AppDbContext> RawOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;

    private static (TestHost Host, SahteEposta Posta) Kur(string cs)
    {
        var posta = new SahteEposta();
        return (new TestHost(cs, s => s.AddSingleton<IEmailSender>(posta)), posta);
    }

    /// <summary>SMTP ayarı + iki hatırlatma şablonu (e-posta) — mesaj gerçekten "gönderilsin".</summary>
    private static async Task KanalVeSablonAsync(TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<TenantSettingsService>().SaveAsync(new TenantSettingsModel
        {
            FirmaUnvan = "Yüce Rent A.Ş.", SmtpHost = "smtp.test.local", SmtpPort = 587,
            SmtpGonderenAdres = "rezervasyon@yucerent.com",
        });
        var svc = sp.GetRequiredService<CustomerNotificationService>();
        await svc.SaveTemplateAsync(new MesajSablonInput
        {
            Tur = MessageType.TeslimHatirlatma, Kanal = MessageChannel.Eposta,
            Konu = "Yarın teslim", Govde = "<p>{MusteriAd}, {No} yarın.</p>", Aktif = true,
        });
        await svc.SaveTemplateAsync(new MesajSablonInput
        {
            Tur = MessageType.IadeHatirlatma, Kanal = MessageChannel.Eposta,
            Konu = "Bugün iade", Govde = "<p>{MusteriAd}, {No} bugün.</p>", Aktif = true,
        });
    }

    /// <summary>Müşteri + araç (kayıt başına ayrı) — "kime mesaj gitti" doğrudan hangi kaydın seçildiğini söyler.</summary>
    private static (Guid MusteriId, Guid AracId) MusteriArac(AppDbContext db, string email, string no)
    {
        var musteri = new Customer { Ad = email, Email = email };
        var arac = new Vehicle { Plaka = "34TZ" + no, Durum = VehicleStatus.Musait };
        db.Customers.Add(musteri);
        db.Vehicles.Add(arac);
        return (musteri.Id, arac.Id);
    }

    /// <summary>Rezerv durumda rezervasyon (teslim hatırlatması adayı: BasTar'a bakılır).</summary>
    private static async Task<Guid> RezAsync(AppDbContext db, string email, string no, DateTimeOffset bas)
    {
        var (m, v) = MusteriArac(db, email, no);
        var r = new Reservation
        {
            ReservationNo = no, Durum = ReservationStatus.Rezerv, MusteriId = m, VehicleId = v,
            BasTar = bas, BitTar = bas.AddDays(3),
        };
        db.Reservations.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    /// <summary>Kirada durumda sözleşme (iade hatırlatması adayı: BitTar'a bakılır).</summary>
    private static async Task<Guid> KiraAsync(AppDbContext db, string email, string no, DateTimeOffset bit)
    {
        var (m, v) = MusteriArac(db, email, no);
        var k = new RentalContract
        {
            SozlesmeNo = no, Durum = RentalStatus.Kirada, MusteriId = m, VehicleId = v,
            BasTar = bit.AddDays(-3), BitTar = bit,
        };
        db.Rentals.Add(k);
        await db.SaveChangesAsync();
        return k.Id;
    }

    /// <summary>Job'ın birebir bağlamı: raw options + SystemTenantContext + GUC.</summary>
    private async Task<AppDbContext> JobContextAsync(Guid tenant)
    {
        var sys = new SystemTenantContext { TenantId = tenant };
        var db = new AppDbContext(RawOptions(), sys, sys);
        await TenantGuc.OpenAsync(db, tenant);
        return db;
    }

    private static Task<int> MusteriKosAsync(AppDbContext db, Guid tenant, DateTimeOffset now,
        IServiceProvider sp, IEmailSender posta)
        => JobRunRecorder.RunAsync(db, tenant, JobRunRecorder.CustomerNotification,
            () => CustomerNotificationGenerator.RunAsync(db, tenant, now, Ist,
                sp.GetRequiredService<ISecretProtector>(), posta, sp.GetRequiredService<ISmsService>()),
            n => n);

    [Fact]
    public async Task Musteri_hatirlatmalari_uretim_parametreleriyle_Istanbul_gununden_uretilir()
    {
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await KanalVeSablonAsync(host, tenant);

        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        Guid rYarin, rYarinGece, kBugun;
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            // Teslim hatırlatması = İstanbul YARINI [08.07 00:00, 09.07 00:00) içinde başlayan Rezerv.
            rYarin = await RezAsync(db, "a@ornek.com", "RZA", Ist3(7, 8, 10));
            rYarinGece = await RezAsync(db, "b@ornek.com", "RZB", Ist3(7, 8, 0, 30)); // = 07.07 21:30Z (UTC'de "bugün")
            await RezAsync(db, "c@ornek.com", "RZC", Ist3(7, 7, 12));                 // İstanbul bugünü → YOK
            await RezAsync(db, "d@ornek.com", "RZD", Ist3(7, 9, 0));                  // üst sınır açık → YOK
            // İade hatırlatması = İstanbul BUGÜNÜ [07.07 00:00, 08.07 00:00) içinde biten Kirada.
            kBugun = await KiraAsync(db, "e@ornek.com", "KRE", Ist3(7, 7, 18));
            await KiraAsync(db, "f@ornek.com", "KRF", Ist3(7, 6, 23, 59));            // İstanbul dünü → YOK
            await KiraAsync(db, "g@ornek.com", "KRG", Ist3(7, 8, 9));                 // İstanbul yarını → YOK
        }

        int sonuc;
        await using (var db = await JobContextAsync(tenant))
            sonuc = await MusteriKosAsync(db, tenant, NowUtc, sp, posta); // üretimdeki gibi: now UTC, tz İstanbul

        Assert.Equal(3, sonuc);
        Assert.Equal(
            new[] { "a@ornek.com", "b@ornek.com", "e@ornek.com" },
            posta.Gonderilenler.Select(g => g.Mesaj.Alici).Order().ToArray());

        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var kayitlar = await db.GidenMesajlar.AsNoTracking().ToListAsync();
            Assert.Equal(
                new[]
                {
                    $"iade-hatirlatma:{kBugun:N}:{GunEtiketi}",
                    $"teslim-hatirlatma:{rYarin:N}:{GunEtiketi}",
                    $"teslim-hatirlatma:{rYarinGece:N}:{GunEtiketi}",
                }.Order().ToArray(),
                kayitlar.Select(k => k.Anahtar).Order().ToArray());
            Assert.All(kayitlar, k => Assert.Equal(OutgoingMessageStatus.Gonderildi, k.Durum));
        }

        // Koşu günlüğü: başarı satırı yazıldı (eski kodda bu iş için HİÇ satır oluşmuyordu).
        var log = Assert.Single(await sp.GetRequiredService<JobRunLogService>().ListAsync(),
            l => l.JobAdi == JobRunRecorder.CustomerNotification);
        Assert.True(log.Basarili);
        Assert.Equal(3, log.SonucSayisi);
    }

    [Fact]
    public async Task Zincir_03_00_ofsetli_now_ile_de_calisir_ve_DBye_UTC_yazar()
    {
        // Aynı an, İstanbul ofsetiyle ifade edilmiş: 07.07.2026 01:30+03:00 == 06.07.2026 22:30Z.
        var nowIst = NowUtc.ToOffset(TimeSpan.FromHours(3));
        var (host, posta) = Kur(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await KanalVeSablonAsync(host, tenant);

        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;

        // Vade: Kasko 5 gün sonra biter → 1 bildirim (7 gün kovası).
        var aracV = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TZ 901", Durum = VehicleStatus.Musait });
        await sp.GetRequiredService<RegulationService>().AddInsuranceAsync(aracV, InsuranceType.Kasko,
            NowUtc.AddDays(-360), NowUtc.AddDays(5), 1000m, "P-TZ", "Sig", null);

        // Filo: Km 9.500, bakım hedefi 10.000 → kalan 500 ≤ 1000 → 1 "Bakım-Km" bildirimi.
        var aracF = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TZ 902", Km = 9_500 });
        var servis = sp.GetRequiredService<ServiceRecordService>();
        var sid = await servis.CreateAsync(new ServiceRecordInput { VehicleId = aracF, GirisKm = 9_000 });
        Assert.True(await servis.StartAsync(sid));
        Assert.True(await servis.CompleteAsync(sid, 9_500, nextMaintenanceKm: 10_000));

        // Müşteri: İstanbul yarını başlayan 1 rezervasyon → 1 teslim hatırlatması.
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await RezAsync(db, "z@ornek.com", "RZZ", Ist3(7, 8, 10));

        int vade, filo, musteri;
        await using (var db = await JobContextAsync(tenant))
        {
            // VadeBildirimJob.RunOnceAsync'in tenant bloğu — aynı sıra, aynı db.
            vade = await JobRunRecorder.RunAsync(db, tenant, JobRunRecorder.DueNotification,
                () => DueNotificationGenerator.RunAsync(db, tenant, nowIst), n => n);
            filo = await JobRunRecorder.RunAsync(db, tenant, JobRunRecorder.FleetNotification,
                () => FleetNotificationGenerator.RunAsync(db, tenant, nowIst, TutSatEsikleri.Default), n => n);
            musteri = await MusteriKosAsync(db, tenant, nowIst, sp, posta);
        }

        Assert.Equal(1, vade);
        Assert.Equal(1, filo);
        Assert.Equal(1, musteri);
        Assert.Equal("z@ornek.com", Assert.Single(posta.Gonderilenler).Mesaj.Alici);

        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var bildirimler = await db.Bildirimler.AsNoTracking().ToListAsync();
            Assert.Equal(new[] { "Bakım-Km", "Kasko" }, bildirimler.Select(b => b.Tur).Order().ToArray());
            // Oluşturma anı = job'ın anı (ofset ne olursa olsun aynı UTC an).
            Assert.All(bildirimler, b => Assert.Equal(NowUtc.UtcDateTime, b.OlusturmaTarihi.UtcDateTime));
            var anahtar = Assert.Single(await db.GidenMesajlar.AsNoTracking().Select(m => m.Anahtar).ToListAsync());
            Assert.EndsWith(":" + GunEtiketi, anahtar); // gün etiketi İstanbul günü (07.07), UTC günü (06.07) değil
        }

        var loglar = await sp.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(3, loglar.Count);
        Assert.All(loglar, l => Assert.True(l.Basarili, $"{l.JobAdi}: {l.Detay}"));
    }
}
