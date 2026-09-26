using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Crm;
using RentACar.Application.Notifications;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G6 — bildirim merkezi agrega (salt-okur) + scheduler kalıcı vade bildirimi. BAĞIMSIZ ORACLE:
/// agrega yalnız açık şikayeti sayar; 4 vade kaynağından 3'ü uyarı (Kasko 5g/MTV 20g/Muayene geçmiş),
/// Trafik 100g İLERİ → bildirim yok; üretim idempotent; okundu unread'i düşürür; cross-tenant izole.
/// </summary>
[Collection("postgres")]
public sealed class BildirimTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset D(int day) => Now.AddDays(day);

    private static async Task<Guid> SeedDuesAsync(IServiceProvider sp)
    {
        var v = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 BD 01", Durum = VehicleStatus.Musait });
        var reg = sp.GetRequiredService<RegulationService>();
        await reg.AddInsuranceAsync(v, InsuranceType.Kasko, D(-365), D(5), 1000m, "P1", "Sig", null);   // YediGun
        await reg.AddMtvAsync(v, "2026/1", 800m, D(20));                                                 // OtuzGun
        await reg.AddInspectionAsync(v, D(-365), D(-3), 500m);                                           // Gecmis
        await reg.AddInsuranceAsync(v, InsuranceType.Trafik, D(-365), D(100), 900m, "P2", "Sig", null);  // Ileri → YOK
        return v;
    }

    private static async Task<int> GenerateAsync(IServiceProvider sp, Guid tenant)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        return await DueNotificationGenerator.RunAsync(db, tenant, Now);
    }

    [Fact]
    public async Task Vade_tarayip_uyarilari_bildirime_yazar_ileri_haric()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SeedDuesAsync(sp);

        Assert.Equal(3, await GenerateAsync(sp, tenant)); // Kasko + MTV + Muayene (Trafik İLERİ değil)

        var svc = sp.GetRequiredService<InAppNotificationService>();
        var notifications = await svc.ListPersistedAsync();
        Assert.Equal(3, notifications.Count);
        Assert.Contains(notifications, b => b.Tur == "Kasko");
        Assert.Contains(notifications, b => b.Tur == "MTV");
        Assert.Contains(notifications, b => b.Tur == "Muayene");
        Assert.DoesNotContain(notifications, b => b.Tur == "Trafik"); // İLERİ vade bildirim üretmez
        Assert.All(notifications, b => Assert.False(b.Okundu));
        Assert.Equal(3, await svc.UnreadCountAsync());
    }

    [Fact]
    public async Task Uretim_idempotent()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SeedDuesAsync(sp);

        Assert.Equal(3, await GenerateAsync(sp, tenant)); // ilk tarama
        Assert.Equal(0, await GenerateAsync(sp, tenant)); // ikinci tarama: çift-yazma YOK
        Assert.Equal(3, (await sp.GetRequiredService<InAppNotificationService>().ListPersistedAsync()).Count);
    }

    [Fact]
    public async Task Okundu_isaretleme_unread_dusurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        var sp = scope.ServiceProvider;
        await SeedDuesAsync(sp);
        await GenerateAsync(sp, tenant);
        var svc = sp.GetRequiredService<InAppNotificationService>();

        var first = (await svc.ListPersistedAsync()).First();
        Assert.True(await svc.MarkReadAsync(first.Id));
        Assert.Equal(2, await svc.UnreadCountAsync());
        Assert.Equal(2, await svc.MarkAllReadAsync()); // kalan 2
        Assert.Equal(0, await svc.UnreadCountAsync());
    }

    [Fact]
    public async Task Cross_tenant_izole()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tA = Guid.NewGuid();
        using var sA = host.ScopeFor(tA);
        await SeedDuesAsync(sA.ServiceProvider);
        await GenerateAsync(sA.ServiceProvider, tA);

        using var sB = host.ScopeFor(Guid.NewGuid()); // farklı tenant → A'nınkiler sızmaz
        Assert.Equal(0, await sB.ServiceProvider.GetRequiredService<InAppNotificationService>().UnreadCountAsync());
    }

    [Fact]
    public async Task Job_ham_context_yolu_dogru_tenanta_yazar()
    {
        // Adversarial testing-gap: BildirimTests VadeBildirimUretici'yi factory-context (interceptor'lı)
        // ile çağırıyordu; job'ın GERÇEK yolu interceptor'SIZ options + SystemTenantContext + manual
        // set_config + açık TenantId damga. Bu yolu birebir test et.
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        using var scope = host.ScopeFor(tenant);
        await SeedDuesAsync(scope.ServiceProvider);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.AppConnectionString).Options;
        var sys = new SystemTenantContext { TenantId = tenant };
        await using (var db = new AppDbContext(options, sys, sys)) // interceptor YOK (job deseni)
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.tenant_id', {tenant.ToString()}, false)");
            Assert.Equal(3, await DueNotificationGenerator.RunAsync(db, tenant, Now));
        }
        // Açık damga + RLS ile doğru tenant'ta görünür.
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<InAppNotificationService>().UnreadCountAsync());
    }

    [Fact]
    public async Task Aggregates_open_complaints_and_period_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var complaint = sp.GetRequiredService<ComplaintService>();

        await complaint.CreateAsync(new SikayetInput { Konu = "Araç kirli", Durum = ComplaintStatus.Acik });
        await complaint.CreateAsync(new SikayetInput { Konu = "Çözüldü", Durum = ComplaintStatus.Kapali });
        var closing = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);
        await sp.GetRequiredService<PeriodLockService>().LockAsync(closing);

        var d = await sp.GetRequiredService<InAppNotificationService>().GetAsync();

        Assert.Equal(1, d.AcikSikayet);              // yalnız açık (elle oracle)
        Assert.Single(d.Sikayetler);
        Assert.Equal("Araç kirli", d.Sikayetler[0].Konu);
        Assert.Equal(closing.Date, d.DonemKapanis!.Value.Date);
        Assert.Equal(0, d.VadeGecmis);               // vade kurulmadı
        Assert.Equal(0, d.VadeYakin);
    }
}
