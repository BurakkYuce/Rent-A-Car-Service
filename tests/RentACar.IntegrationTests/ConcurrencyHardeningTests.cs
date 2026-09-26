using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Application.ServiceRecords;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Eşzamanlılık sağlamlaştırması (küçük borç — deadlock-retry'ın atlanan tek-satır metodları):
/// AddLineAsync (ToplamIscilik agregatı → rücu/yansıtma ile deftere gider = PARA) ve TaksitOdeAsync
/// (sayaç) artık FOR UPDATE satır-kilidi + PgRetry ile KAYIP-GÜNCELLEMEye kapalı. BAĞIMSIZ ORACLE:
/// 5 paralel × 10 = 50; 3 paralel taksit → OdenenTaksit 3 (kilitsiz eski hâlde < bu değerler olurdu).
/// </summary>
[Collection("postgres")]
public sealed class ConcurrencyHardeningTests(PostgresFixture fx)
{
    [Fact]
    public async Task Eszamanli_kalem_eklemede_toplam_iscilik_kaybolmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid recId;
        using (var scope = host.ScopeFor(tenant))
        {
            var sp = scope.ServiceProvider;
            var v = await sp.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = "34 SV 01", Durum = VehicleStatus.Musait });
            recId = await sp.GetRequiredService<ServiceRecordService>()
                .CreateAsync(new ServiceRecordInput { VehicleId = v, GirisKm = 1000 });
        }

        // 5 paralel kalem (her biri 10) — FOR UPDATE serileştirir → toplam 50 (hiçbiri kaybolmaz).
        var tasks = Enumerable.Range(0, 5).Select(i => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            await s.ServiceProvider.GetRequiredService<ServiceRecordService>()
                .AddItemAsync(recId, $"kalem-{i}", 10m);
        }));
        await Task.WhenAll(tasks);

        using var check = host.ScopeFor(tenant);
        var factory = check.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rec = await db.ServiceRecords.AsNoTracking().SingleAsync(r => r.Id == recId);
        var rowCount = await db.Set<ServiceLine>().CountAsync(l => l.ServiceRecordId == recId);
        Assert.Equal(5, rowCount);
        Assert.Equal(50m, rec.ToplamIscilik); // 5×10 — hiçbir eklemenin toplamı kaybolmadı (oracle)
    }

    [Fact]
    public async Task Eszamanli_taksit_odemede_sayac_kaybolmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid loanId;
        using (var scope = host.ScopeFor(tenant))
        {
            loanId = await scope.ServiceProvider.GetRequiredService<VehicleLoanService>()
                .CreateAsync(new AracKrediInput { BankaAdi = "Test Bank", KrediTutari = 100000m, FaizOran = 0.1m, TaksitSayisi = 12 });
        }

        // 3 paralel taksit ödemesi → OdenenTaksit tam 3 (kilitsiz sayaç yarışında < 3 olurdu).
        var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            using var s = host.ScopeFor(tenant);
            await s.ServiceProvider.GetRequiredService<VehicleLoanService>().PayInstallmentAsync(loanId);
        }));
        await Task.WhenAll(tasks);

        using var check = host.ScopeFor(tenant);
        var k = await check.ServiceProvider.GetRequiredService<VehicleLoanService>().GetAsync(loanId);
        Assert.Equal(3, k!.OdenenTaksit); // hiçbir artırım kaybolmadı (oracle)
    }
}
