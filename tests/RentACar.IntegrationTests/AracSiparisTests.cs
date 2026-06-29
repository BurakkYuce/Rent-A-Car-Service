using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracSiparisleri;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap L3 — araç sipariş/tedarik. BAĞIMSIZ ORACLE: No "SP-" boşluksuz; durum Bekliyor→Onaylandi→TeslimAlindi;
/// tedarikçi zorunlu; tenant izolasyonu (racar_app, RLS).
/// </summary>
[Collection("postgres")]
public sealed class AracSiparisTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_no_and_durum_gecisi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracSiparisService>();

        var id = await svc.CreateAsync(new AracSiparisInput
        { Tedarikci = "ABC Otomotiv", Marka = "Toyota", Adet = 2, BirimFiyat = 500_000m });

        var s = await svc.GetAsync(id);
        Assert.StartsWith("SP-", s!.No);
        Assert.Equal(SiparisDurum.Bekliyor, s.Durum);
        Assert.Equal(2, s.Adet);

        Assert.True(await svc.OnaylaAsync(id));
        Assert.Equal(SiparisDurum.Onaylandi, (await svc.GetAsync(id))!.Durum);
        Assert.True(await svc.TeslimAlAsync(id));
        Assert.Equal(SiparisDurum.TeslimAlindi, (await svc.GetAsync(id))!.Durum);
    }

    [Fact]
    public async Task Tedarikci_zorunlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(() =>
            scope.ServiceProvider.GetRequiredService<AracSiparisService>()
                .CreateAsync(new AracSiparisInput { Tedarikci = "  ", Adet = 1, BirimFiyat = 100m }));
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<AracSiparisService>()
                .CreateAsync(new AracSiparisInput { Tedarikci = "Gizli Tedarikçi", Adet = 1, BirimFiyat = 100m });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<AracSiparisService>().ListAsync());
    }
}
