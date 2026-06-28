using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap C3 — CRM (Anket + Şikayet). BAĞIMSIZ ORACLE: CRUD; Puan 0-10 doğrulama; Şikayet durum/çözüm;
/// tenant izolasyon; yetki (OperationsWrite olmayan reddedilir).
/// </summary>
[Collection("postgres")]
public sealed class CrmTests(PostgresFixture fx)
{
    [Fact]
    public async Task Anket_crud_and_puan_validation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AnketService>();

        var id = await svc.CreateAsync(new AnketInput { Puan = 9, Yorum = "Memnun", Kaynak = "Web" });
        var a = await svc.GetAsync(id);
        Assert.Equal(9, a!.Puan);
        Assert.Equal("Memnun", a.Yorum);
        Assert.Single(await svc.ListAsync());

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AnketInput { Puan = 11 }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new AnketInput { Puan = -1 }));

        Assert.True(await svc.DeleteAsync(id));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Sikayet_crud_with_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<SikayetService>();

        var id = await svc.CreateAsync(new SikayetInput { Konu = "Geç teslim", Detay = "Araç geç geldi" });
        var s = await svc.GetAsync(id);
        Assert.Equal("Geç teslim", s!.Konu);
        Assert.Equal(SikayetDurum.Acik, s.Durum);

        Assert.True(await svc.UpdateAsync(id, new SikayetInput { Konu = "Geç teslim", Durum = SikayetDurum.Cozuldu, Cozum = "Özür + indirim" }));
        var s2 = await svc.GetAsync(id);
        Assert.Equal(SikayetDurum.Cozuldu, s2!.Durum);
        Assert.Equal("Özür + indirim", s2.Cozum);

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new SikayetInput { Konu = "  " }));
    }

    [Fact]
    public async Task Tenant_isolation_both()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
        {
            await s1.ServiceProvider.GetRequiredService<AnketService>().CreateAsync(new AnketInput { Puan = 5 });
            await s1.ServiceProvider.GetRequiredService<SikayetService>().CreateAsync(new SikayetInput { Konu = "X" });
        }

        using var s2 = host.ScopeFor(t2);
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<AnketService>().ListAsync());
        Assert.Empty(await s2.ServiceProvider.GetRequiredService<SikayetService>().ListAsync());
    }

    [Fact]
    public async Task Non_operations_role_denied()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), role: UserRole.Muhasebe); // OperationsWrite yok
        await Assert.ThrowsAsync<ValidationException>(
            () => scope.ServiceProvider.GetRequiredService<AnketService>().CreateAsync(new AnketInput { Puan = 5 }));
        await Assert.ThrowsAsync<ValidationException>(
            () => scope.ServiceProvider.GetRequiredService<SikayetService>().CreateAsync(new SikayetInput { Konu = "X" }));
    }
}
