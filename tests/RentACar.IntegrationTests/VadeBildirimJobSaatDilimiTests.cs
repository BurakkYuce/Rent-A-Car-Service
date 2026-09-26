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
    private const string DayLabel = "2026-07-07";

    /// <summary>İstanbul duvar saati → DB'ye yazılabilir UTC an (elle: yerel − 3 saat).</summary>
    private static DateTimeOffset Ist3(int month, int day, int hour, int dk = 0)
        => new DateTimeOffset(2026, month, day, hour, dk, 0, TimeSpan.Zero).AddHours(-3);

    private DbContextOptions<AppDbContext> RawOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;

    private static (TestHost Host, FakeEmail Posta) Setup(string cs)
    {
        var mail = new FakeEmail();
        return (new TestHost(cs, s => s.AddSingleton<IEmailSender>(mail)), mail);
    }

    /// <summary>SMTP ayarı + iki hatırlatma şablonu (e-posta) — mesaj gerçekten "gönderilsin".</summary>
    private static async Task ChannelAndTemplateAsync(TestHost host, Guid tenant)
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
    private static (Guid MusteriId, Guid AracId) CustomerVehicle(AppDbContext db, string email, string no)
    {
        var customer = new Customer { Ad = email, Email = email };
        var vehicle = new Vehicle { Plaka = "34TZ" + no, Durum = VehicleStatus.Musait };
        db.Customers.Add(customer);
        db.Vehicles.Add(vehicle);
        return (customer.Id, vehicle.Id);
    }

    /// <summary>Rezerv durumda rezervasyon (teslim hatırlatması adayı: BasTar'a bakılır).</summary>
    private static async Task<Guid> ReservationAsync(AppDbContext db, string email, string no, DateTimeOffset start)
    {
        var (m, v) = CustomerVehicle(db, email, no);
        var r = new Reservation
        {
            ReservationNo = no, Durum = ReservationStatus.Rezerv, MusteriId = m, VehicleId = v,
            BasTar = start, BitTar = start.AddDays(3),
        };
        db.Reservations.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    /// <summary>Kirada durumda sözleşme (iade hatırlatması adayı: BitTar'a bakılır).</summary>
    private static async Task<Guid> RentalAsync(AppDbContext db, string email, string no, DateTimeOffset bit)
    {
        var (m, v) = CustomerVehicle(db, email, no);
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

    private static Task<int> RunCustomerAsync(AppDbContext db, Guid tenant, DateTimeOffset now,
        IServiceProvider sp, IEmailSender mail)
        => JobRunRecorder.RunAsync(db, tenant, JobRunRecorder.CustomerNotification,
            () => CustomerNotificationGenerator.RunAsync(db, tenant, now, Ist,
                sp.GetRequiredService<ISecretProtector>(), mail, sp.GetRequiredService<ISmsService>()),
            n => n);

    [Fact]
    public async Task Musteri_hatirlatmalari_uretim_parametreleriyle_Istanbul_gununden_uretilir()
    {
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await ChannelAndTemplateAsync(host, tenant);

        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        Guid rTomorrow, rTomorrowNight, kToday;
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            // Teslim hatırlatması = İstanbul YARINI [08.07 00:00, 09.07 00:00) içinde başlayan Rezerv.
            rTomorrow = await ReservationAsync(db, "a@ornek.com", "RZA", Ist3(7, 8, 10));
            rTomorrowNight = await ReservationAsync(db, "b@ornek.com", "RZB", Ist3(7, 8, 0, 30)); // = 07.07 21:30Z (UTC'de "bugün")
            await ReservationAsync(db, "c@ornek.com", "RZC", Ist3(7, 7, 12));                 // İstanbul bugünü → YOK
            await ReservationAsync(db, "d@ornek.com", "RZD", Ist3(7, 9, 0));                  // üst sınır açık → YOK
            // İade hatırlatması = İstanbul BUGÜNÜ [07.07 00:00, 08.07 00:00) içinde biten Kirada.
            kToday = await RentalAsync(db, "e@ornek.com", "KRE", Ist3(7, 7, 18));
            await RentalAsync(db, "f@ornek.com", "KRF", Ist3(7, 6, 23, 59));            // İstanbul dünü → YOK
            await RentalAsync(db, "g@ornek.com", "KRG", Ist3(7, 8, 9));                 // İstanbul yarını → YOK
        }

        int result;
        await using (var db = await JobContextAsync(tenant))
            result = await RunCustomerAsync(db, tenant, NowUtc, sp, mail); // üretimdeki gibi: now UTC, tz İstanbul

        Assert.Equal(3, result);
        Assert.Equal(
            new[] { "a@ornek.com", "b@ornek.com", "e@ornek.com" },
            mail.Sent.Select(g => g.Mesaj.Alici).Order().ToArray());

        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var records = await db.GidenMesajlar.AsNoTracking().ToListAsync();
            Assert.Equal(
                new[]
                {
                    $"iade-hatirlatma:{kToday:N}:{DayLabel}",
                    $"teslim-hatirlatma:{rTomorrow:N}:{DayLabel}",
                    $"teslim-hatirlatma:{rTomorrowNight:N}:{DayLabel}",
                }.Order().ToArray(),
                records.Select(k => k.Anahtar).Order().ToArray());
            Assert.All(records, k => Assert.Equal(OutgoingMessageStatus.Gonderildi, k.Durum));
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
        var (host, mail) = Setup(fx.AppConnectionString);
        using var _ = host;
        var tenant = Guid.NewGuid();
        await ChannelAndTemplateAsync(host, tenant);

        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;

        // Vade: Kasko 5 gün sonra biter → 1 bildirim (7 gün kovası).
        var vehicleV = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TZ 901", Durum = VehicleStatus.Musait });
        await sp.GetRequiredService<RegulationService>().AddInsuranceAsync(vehicleV, InsuranceType.Kasko,
            NowUtc.AddDays(-360), NowUtc.AddDays(5), 1000m, "P-TZ", "Sig", null);

        // Filo: Km 9.500, bakım hedefi 10.000 → kalan 500 ≤ 1000 → 1 "Bakım-Km" bildirimi.
        var vehicleF = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 TZ 902", Km = 9_500 });
        var service = sp.GetRequiredService<ServiceRecordService>();
        var sid = await service.CreateAsync(new ServiceRecordInput { VehicleId = vehicleF, GirisKm = 9_000 });
        Assert.True(await service.StartAsync(sid));
        Assert.True(await service.CompleteAsync(sid, 9_500, nextMaintenanceKm: 10_000));

        // Müşteri: İstanbul yarını başlayan 1 rezervasyon → 1 teslim hatırlatması.
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
            await ReservationAsync(db, "z@ornek.com", "RZZ", Ist3(7, 8, 10));

        int due, fleet, customer;
        await using (var db = await JobContextAsync(tenant))
        {
            // VadeBildirimJob.RunOnceAsync'in tenant bloğu — aynı sıra, aynı db.
            due = await JobRunRecorder.RunAsync(db, tenant, JobRunRecorder.DueNotification,
                () => DueNotificationGenerator.RunAsync(db, tenant, nowIst), n => n);
            fleet = await JobRunRecorder.RunAsync(db, tenant, JobRunRecorder.FleetNotification,
                () => FleetNotificationGenerator.RunAsync(db, tenant, nowIst, TutSatEsikleri.Default), n => n);
            customer = await RunCustomerAsync(db, tenant, nowIst, sp, mail);
        }

        Assert.Equal(1, due);
        Assert.Equal(1, fleet);
        Assert.Equal(1, customer);
        Assert.Equal("z@ornek.com", Assert.Single(mail.Sent).Mesaj.Alici);

        await using (var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var notifications = await db.Bildirimler.AsNoTracking().ToListAsync();
            Assert.Equal(new[] { "Bakım-Km", "Kasko" }, notifications.Select(b => b.Tur).Order().ToArray());
            // Oluşturma anı = job'ın anı (ofset ne olursa olsun aynı UTC an).
            Assert.All(notifications, b => Assert.Equal(NowUtc.UtcDateTime, b.OlusturmaTarihi.UtcDateTime));
            var key = Assert.Single(await db.GidenMesajlar.AsNoTracking().Select(m => m.Anahtar).ToListAsync());
            Assert.EndsWith(":" + DayLabel, key); // gün etiketi İstanbul günü (07.07), UTC günü (06.07) değil
        }

        var logs = await sp.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, l => Assert.True(l.Basarili, $"{l.JobAdi}: {l.Detay}"));
    }
}
