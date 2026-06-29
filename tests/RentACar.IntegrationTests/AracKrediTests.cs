using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap L4 — araç kredisi. BAĞIMSIZ ORACLE: 120000/12 taksit, faiz 0 → aylık 10000; 3 ödeme → kalan 90000;
/// 12 ödeme → Kapandi/kalan 0. Faiz %20 → toplam faiz 20000. No "KR-". Tenant izolasyonu (RLS).
/// </summary>
[Collection("postgres")]
public sealed class AracKrediTests(PostgresFixture fx)
{
    [Fact]
    public async Task Kalan_bakiye_taksit_ode()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracKrediService>();

        var id = await svc.CreateAsync(new AracKrediInput
        { BankaAdi = "X Bank", KrediTutari = 120_000m, FaizOran = 0m, TaksitSayisi = 12 });

        var k0 = await svc.GetAsync(id);
        Assert.StartsWith("KR-", k0!.No);
        var o0 = AracKrediService.Hesapla(k0);
        Assert.Equal(10_000m, o0.AylikTaksit);        // 120000/12
        Assert.Equal(120_000m, o0.ToplamGeriOdeme);
        Assert.Equal(120_000m, o0.KalanBakiye);       // hiç ödeme yok

        for (var i = 0; i < 3; i++) Assert.True(await svc.TaksitOdeAsync(id));
        var o3 = AracKrediService.Hesapla((await svc.GetAsync(id))!);
        Assert.Equal(90_000m, o3.KalanBakiye);        // 120000 − 3×10000 (elle oracle)

        for (var i = 0; i < 9; i++) await svc.TaksitOdeAsync(id);  // toplam 12
        var k12 = await svc.GetAsync(id);
        Assert.Equal(KrediDurum.Kapandi, k12!.Durum); // tüm taksitler ödendi
        Assert.Equal(0m, AracKrediService.Hesapla(k12).KalanBakiye);
        Assert.False(await svc.TaksitOdeAsync(id));    // fazladan ödeme reddedilir
    }

    [Fact]
    public async Task Faiz_toplam_geri_odeme()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<AracKrediService>();
        var id = await svc.CreateAsync(new AracKrediInput
        { BankaAdi = "Y Bank", KrediTutari = 100_000m, FaizOran = 0.20m, TaksitSayisi = 12 });
        var o = AracKrediService.Hesapla((await svc.GetAsync(id))!);
        Assert.Equal(20_000m, o.ToplamFaiz);          // 100000×0.20×12/12
        Assert.Equal(120_000m, o.ToplamGeriOdeme);
        Assert.Equal(10_000m, o.AylikTaksit);
    }

    [Fact]
    public async Task Tenant_isolation()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var a = host.ScopeFor(Guid.NewGuid()))
        {
            await a.ServiceProvider.GetRequiredService<AracKrediService>()
                .CreateAsync(new AracKrediInput { BankaAdi = "Gizli Bank", KrediTutari = 50_000m, TaksitSayisi = 6 });
        }
        using var b = host.ScopeFor(Guid.NewGuid());
        Assert.Empty(await b.ServiceProvider.GetRequiredService<AracKrediService>().ListAsync());
    }
}
