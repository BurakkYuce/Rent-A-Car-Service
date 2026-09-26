using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F6.2a — #278 Low bulguları SERVİS düzeyinde (Blazor yolu da <see cref="VehicleService"/>/<see cref="VehiclePhotoService"/>
/// kullanır). Beklenen değerler elle seçilmiş uç değerlerden; servis kodundan türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class VehicleLowFixesTests(PostgresFixture fx)
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    private static string NewPlate() => "34LF" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();

    [Fact]
    public async Task Service_rejects_unreasonable_dates_and_negative_costs()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using var scope = host.ScopeFor(tenantId);
        var vehicles = scope.ServiceProvider.GetRequiredService<VehicleService>();

        var old = new DateTimeOffset(1949, 12, 31, 0, 0, 0, TimeSpan.Zero);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => vehicles.CreateAsync(new VehicleInput
        { Plaka = NewPlate(), Durum = VehicleStatus.Musait, TescilTarihi = old }));
        Assert.StartsWith("Tescil tarihi", ex.Message);

        ex = await Assert.ThrowsAsync<ValidationException>(() => vehicles.CreateAsync(new VehicleInput
        { Plaka = NewPlate(), Durum = VehicleStatus.Musait, KiraBitTar = DateTimeOffset.UtcNow.AddYears(31) }));
        Assert.StartsWith("Kira bitiş tarihi", ex.Message);

        ex = await Assert.ThrowsAsync<ValidationException>(() => vehicles.CreateAsync(new VehicleInput
        { Plaka = NewPlate(), Durum = VehicleStatus.Musait, AlisKdv = -5m }));
        Assert.StartsWith("Alış KDV", ex.Message);

        // Güncelleme yolu da aynı kuraldan geçer (Blazor düzenleme formu).
        var id = await vehicles.CreateAsync(new VehicleInput
        { Plaka = NewPlate(), Durum = VehicleStatus.Musait, FiloGirisTarih = TestZaman.DaysLater(-30) });
        var version = await vehicles.VersionAsync(id);
        ex = await Assert.ThrowsAsync<ValidationException>(() => vehicles.UpdateAsync(id, new VehicleInput
        { Plaka = NewPlate(), Durum = VehicleStatus.Musait, FiloYonetimMaliyeti = -1m }, version));
        Assert.StartsWith("Filo yönetim maliyeti", ex.Message);

        ex = await Assert.ThrowsAsync<ValidationException>(() => vehicles.EnterManualKmAsync(id, 100, old));
        Assert.StartsWith("KM tarihi", ex.Message);
    }

    [Fact]
    public async Task Parallel_photo_uploads_via_service_cap_at_twenty_with_unique_order()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        Guid vehicleId;
        using (var s = host.ScopeFor(tenantId))
            vehicleId = await s.ServiceProvider.GetRequiredService<VehicleService>()
                .CreateAsync(new VehicleInput { Plaka = NewPlate(), Durum = VehicleStatus.Musait });

        async Task<bool> UploadAsync()
        {
            using var s = host.ScopeFor(tenantId);
            try
            {
                await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().AddAsync(vehicleId, TinyPng);
                return true;
            }
            catch (ValidationException) { return false; }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(UploadAsync)));
        Assert.Equal(20, results.Count(r => r));

        using var scope = host.ScopeFor(tenantId);
        var photos = scope.ServiceProvider.GetRequiredService<VehiclePhotoService>();
        var list = await photos.ListMetaAsync(vehicleId);
        Assert.Equal(Enumerable.Range(0, 20).ToList(), list.Select(p => p.Sira).OrderBy(x => x).ToList());

        async Task MoveAsync(Guid photoId, int direction)
        {
            using var s = host.ScopeFor(tenantId);
            await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().MoveAsync(vehicleId, photoId, direction);
        }

        await Task.WhenAll(list.Select((p, i) => Task.Run(() => MoveAsync(p.Id, i % 2 == 0 ? 1 : -1))));
        var after = await photos.ListMetaAsync(vehicleId);
        Assert.Equal(Enumerable.Range(0, 20).ToList(), after.Select(p => p.Sira).OrderBy(x => x).ToList());
    }
}
