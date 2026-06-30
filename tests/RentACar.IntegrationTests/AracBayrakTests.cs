using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap K2 — Vehicle operasyon bayrakları + bakım/lastik (additive). BAĞIMSIZ ORACLE: alanlar
/// Create/Update üzerinden kalıcılaşıp Get ile geri okunur (ApplyExtended eşlemesi).
/// </summary>
[Collection("postgres")]
public sealed class AracBayrakTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bakim = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Bayraklar_ve_bakim_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var id = await svc.CreateAsync(new VehicleInput
        {
            Plaka = "34 BK 01", Durum = VehicleStatus.Musait,
            WebRezKapat = true, Rehin = true, KarLastigi = true,
            SonBakimTarih = Bakim, SonBakimKm = 15000, LastikDurumu = "Kışlık"
        });

        var v = await svc.GetAsync(id);
        Assert.True(v!.WebRezKapat);
        Assert.True(v.Rehin);
        Assert.True(v.KarLastigi);
        Assert.False(v.Utts);            // set edilmedi → false
        Assert.Equal(Bakim, v.SonBakimTarih);
        Assert.Equal(15000, v.SonBakimKm);
        Assert.Equal("Kışlık", v.LastikDurumu);

        // Update: bayrak kapat + lastik değiştir
        await svc.UpdateAsync(id, new VehicleInput
        {
            Plaka = "34 BK 01", Durum = VehicleStatus.Musait,
            WebRezKapat = false, Utts = true, LastikDurumu = "Yazlık", SonBakimKm = 16000
        });
        var v2 = await svc.GetAsync(id);
        Assert.False(v2!.WebRezKapat);   // kapatıldı
        Assert.True(v2.Utts);            // açıldı
        Assert.Equal("Yazlık", v2.LastikDurumu);
        Assert.Equal(16000, v2.SonBakimKm);
    }
}
