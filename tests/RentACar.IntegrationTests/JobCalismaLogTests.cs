using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Jobs;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-26 — Otomatik servis koşu günlüğü.
///
/// <para>Testler <b>job'ın GERÇEK yolunu</b> kurar (interceptor'sız options + <c>SystemTenantContext</c>
/// + elle <c>set_config</c>), çünkü kaydedici o bağlamda çalışıyor ve RLS'i o GUC'a dayanıyor —
/// factory-context ile test etmek yolu doğrulamazdı (<c>BildirimTests.Job_ham_context_yolu…</c>
/// aynı dersin ürünü).</para>
///
/// <para>Bağımsız oracle: "2 üretici koştu → 2 log satırı beklerim", "hata fırlatan iş 1 başarısız
/// satır bırakır ve hata çağırana AYNEN geçer" gibi beklentiler senaryodan; sayılar testte sabit.</para>
/// </summary>
[Collection("postgres")]
public sealed class JobCalismaLogTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Job'ın gerçek bağlamı: interceptor YOK, GUC elle açılır.</summary>
    private async Task<AppDbContext> JobContextAsync(Guid tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var sys = new SystemTenantContext { TenantId = tenant };
        var db = new AppDbContext(options, sys, sys);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenant.ToString()}, false)");
        return db;
    }

    private static async Task SeedDuesAsync(IServiceProvider sp)
    {
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 JL 01", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();
        await reg.AddInsuranceAsync(v, InsuranceType.Kasko, Now.AddDays(-365), Now.AddDays(5), 1000m, "P1", "Sig", null);
        await reg.AddMtvAsync(v, "2026/1", 800m, Now.AddDays(20));
    }

    [Fact]
    public async Task Iki_uretici_kosusu_IKI_log_satiri_birakir_ve_sonuc_sayisi_uretici_donusuyle_ayni()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        await SeedDuesAsync(scope.ServiceProvider);

        int dueResult;
        await using (var db = await JobContextAsync(tenant))
        {
            // Job'ın yaptığının aynısı: üreticiyi kaydedici üzerinden çağır.
            dueResult = await JobRunRecorder.RunAsync(db, tenant,
                JobRunRecorder.DueNotification,
                () => DueNotificationGenerator.RunAsync(db, tenant, Now), n => n);
            await JobRunRecorder.RunAsync(db, tenant,
                JobRunRecorder.FleetNotification,
                () => FleetNotificationGenerator.RunAsync(db, tenant, Now, TutSatEsikleri.Default), n => n);
        }

        // Elle beklenen: 2 üretici çağrıldı → 2 satır.
        var logs = await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, l => Assert.True(l.Basarili));
        Assert.All(logs, l => Assert.Null(l.Detay));
        Assert.Contains(logs, l => l.JobAdi == JobRunRecorder.DueNotification);
        Assert.Contains(logs, l => l.JobAdi == JobRunRecorder.FleetNotification);

        // SonucSayisi üreticinin GERÇEK dönüşüyle aynı olmalı (kayıt uydurmuyor).
        // 2 vade tohumlandı (Kasko 5g + MTV 20g) → üretici 2 bildirim üretir.
        Assert.Equal(2, dueResult);
        Assert.Equal(dueResult, logs.Single(l => l.JobAdi == JobRunRecorder.DueNotification).SonucSayisi);

        Assert.All(logs, l => Assert.True(l.BitisUtc >= l.BaslangicUtc));
        Assert.All(logs, l => Assert.True(l.SureMs >= 0));
    }

    [Fact]
    public async Task Hata_yolu_BASARISIZ_satir_yazar_ve_istisnayi_AYNEN_yeniden_firlatir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);

        await using (var db = await JobContextAsync(tenant))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                JobRunRecorder.RunAsync<int>(db, tenant, "test-hatali",
                    () => throw new InvalidOperationException("kasten patlatildi")));
            Assert.Equal("kasten patlatildi", ex.Message);   // hata yutulmadı, çağırana geçti
        }

        var log = Assert.Single(await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
        Assert.False(log.Basarili);
        Assert.Equal("test-hatali", log.JobAdi);
        Assert.Equal("kasten patlatildi", log.Detay);
        Assert.Null(log.SonucSayisi);
    }

    [Fact]
    public async Task Uzun_hata_mesaji_512_karaktere_KIRPILIR_yazim_dusmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);

        var longText = new string('x', 5000);
        await using (var db = await JobContextAsync(tenant))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                JobRunRecorder.RunAsync<int>(db, tenant, "uzun-hata",
                    () => throw new InvalidOperationException(longText)));

        var log = Assert.Single(await scope.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
        Assert.False(log.Basarili);
        Assert.Equal(512, log.Detay!.Length);
        Assert.EndsWith("...", log.Detay);
    }

    [Fact]
    public async Task Filtreler_ELLE_KURULAN_sayilari_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);

        // Senaryo elle: "a" işi 2 başarılı, "b" işi 1 başarısız.
        await using (var db = await JobContextAsync(tenant))
        {
            await JobRunRecorder.WriteAsync(db, tenant, "a", Now.AddDays(-5), true, 3, null);
            await JobRunRecorder.WriteAsync(db, tenant, "a", Now.AddDays(-1), true, 0, null);
            await JobRunRecorder.WriteAsync(db, tenant, "b", Now.AddDays(-1), false, null, "patladi");
        }

        var svc = scope.ServiceProvider.GetRequiredService<JobRunLogService>();
        Assert.Equal(3, (await svc.ListAsync()).Count);
        Assert.Equal(2, (await svc.ListAsync(new JobCalismaLogFilter { JobAdi = "a" })).Count);
        Assert.Single(await svc.ListAsync(new JobCalismaLogFilter { YalnizHatali = true }));
        // Tarih aralığı: -5 günlük kayıt dışarıda kalır.
        Assert.Equal(2, (await svc.ListAsync(new JobCalismaLogFilter { Bas = Now.AddDays(-3) })).Count);
        // Üst sınır: -1 günlük kayıtlar dışarıda kalır.
        Assert.Single(await svc.ListAsync(new JobCalismaLogFilter { Bit = Now.AddDays(-3) }));

        // Son koşular: iş adı başına EN YENİ satır → 2 iş = 2 satır, "a" için -1 günlük olan.
        var last = await svc.LastRunsAsync();
        Assert.Equal(2, last.Count);
        Assert.Equal(Now.AddDays(-1), last.Single(x => x.JobAdi == "a").BaslangicUtc);
        Assert.Equal(0, last.Single(x => x.JobAdi == "a").SonucSayisi);
    }

    [Fact]
    public async Task Loglar_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        await using (var db = await JobContextAsync(t1))
            await JobRunRecorder.WriteAsync(db, t1, "gizli", Now, true, 1, null);

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());

        using var s1 = host.ScopeFor(t1);
        Assert.Single(await s1.ServiceProvider.GetRequiredService<JobRunLogService>().ListAsync());
    }

    [Fact]
    public async Task Operator_gunlugu_GOREMEZ_ViewReports_gerekir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        await using (var db = await JobContextAsync(tenant))
            await JobRunRecorder.WriteAsync(db, tenant, "a", Now, true, 1, null);

        using var s = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator);
        var svc = s.ServiceProvider.GetRequiredService<JobRunLogService>();
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.ListAsync());
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.LastRunsAsync());
    }
}
