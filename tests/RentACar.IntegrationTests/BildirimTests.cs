using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Crm;
using RentACar.Application.Notifications;
using RentACar.Application.Periods;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap G6 — bildirim merkezi agrega (salt-okur). BAĞIMSIZ ORACLE: 2 şikayetten (1 açık + 1 kapalı) yalnız
/// AÇIK sayılır; dönem kilidi tarihi DonemKapanis'e yansır; vade kurulmadığından vade sayıları 0.
/// </summary>
[Collection("postgres")]
public sealed class BildirimTests(PostgresFixture fx)
{
    [Fact]
    public async Task Aggregates_open_complaints_and_period_status()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var sikayet = sp.GetRequiredService<SikayetService>();

        await sikayet.CreateAsync(new SikayetInput { Konu = "Araç kirli", Durum = SikayetDurum.Acik });
        await sikayet.CreateAsync(new SikayetInput { Konu = "Çözüldü", Durum = SikayetDurum.Kapali });
        var kapanis = new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero);
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(kapanis);

        var d = await sp.GetRequiredService<BildirimService>().GetAsync();

        Assert.Equal(1, d.AcikSikayet);              // yalnız açık (elle oracle)
        Assert.Single(d.Sikayetler);
        Assert.Equal("Araç kirli", d.Sikayetler[0].Konu);
        Assert.Equal(kapanis.Date, d.DonemKapanis!.Value.Date);
        Assert.Equal(0, d.VadeGecmis);               // vade kurulmadı
        Assert.Equal(0, d.VadeYakin);
    }
}
