using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Legal;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap C2 — Hukuk dosyası master. BAĞIMSIZ ORACLE: CRUD; DosyaNo benzersizliği; tenant izolasyon;
/// yetki (OperationsWrite olmayan rol reddedilir).
/// </summary>
[Collection("postgres")]
public sealed class HukukTests(PostgresFixture fx)
{
    private static HukukDosyaInput Input(string no) => new()
    {
        DosyaNo = no, Tur = HukukTuru.Icra, Avukat = "Av. Demir", Tutar = 15000m,
        Durum = HukukDurum.Acik, Tarih = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), Aciklama = "İcra takibi"
    };

    [Fact]
    public async Task Crud_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<HukukDosyaService>();

        var id = await svc.CreateAsync(Input("2026/123"));
        var h = await svc.GetAsync(id);
        Assert.NotNull(h);
        Assert.Equal("2026/123", h!.DosyaNo);
        Assert.Equal(HukukTuru.Icra, h.Tur);
        Assert.Equal(15000m, h.Tutar);
        Assert.Equal(HukukDurum.Acik, h.Durum);

        Assert.True(await svc.UpdateAsync(id, new HukukDosyaInput
        { DosyaNo = "2026/123", Tur = HukukTuru.Icra, Tutar = 15000m, Durum = HukukDurum.Kapali }));
        Assert.Equal(HukukDurum.Kapali, (await svc.GetAsync(id))!.Durum);

        Assert.Single(await svc.ListAsync());
        Assert.True(await svc.DeleteAsync(id));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Duplicate_dosyano_rejected()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<HukukDosyaService>();

        await svc.CreateAsync(Input("2026/999"));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(Input("2026/999")));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<HukukDosyaService>().CreateAsync(Input("2026/123"));

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<HukukDosyaService>().ListAsync());
    }

    [Fact]
    public async Task Non_operations_role_denied()
    {
        using var host = new TestHost(fx.AppConnectionString);
        // Muhasebe: FinanceWrite/ViewReports var, OperationsWrite YOK → yazma reddedilir.
        using var scope = host.ScopeFor(Guid.NewGuid(), role: UserRole.Muhasebe);
        await Assert.ThrowsAsync<ValidationException>(
            () => scope.ServiceProvider.GetRequiredService<HukukDosyaService>().CreateAsync(Input("2026/1")));
    }
}
