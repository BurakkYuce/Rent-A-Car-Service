using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Regulation;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ2-2.1 — Vade panosunun ARAÇ boyutu: GetForVehicleAsync (karne "Yaklaşan Vadeler" bloğu) +
/// GetWarningCountsByVehicleAsync (filo panosu "Vade" kolonu). Union'a DOKUNULMADI (bildirim
/// üreticiyle paylaşımlı — O12a); yalnız saf filtre/gruplama. BAĞIMSIZ ORACLE (elle):
/// V1: kasko bitiş +5g (YediGun) + MTV vade −3g (Gecmis, ödenmemiş) → uyarı 2;
/// V2: muayene bitiş +200g (Ileri) → uyarı 0 (sözlükte yok).
/// </summary>
[Collection("postgres")]
public sealed class VadeAracBoyutuTests(PostgresFixture fx)
{
    [Fact]
    public async Task Arac_filtresi_ve_uyari_sayilari()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var reg = sp.GetRequiredService<RegulationService>();
        var now = DateTimeOffset.UtcNow;

        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 VD 01" });
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 VD 02" });

        await reg.AddInsuranceAsync(v1, InsuranceType.Kasko, now.AddYears(-1), now.AddDays(5),
            100m, "K1", "X", null, "TRY");                                   // ≤7 gün
        await reg.AddMtvAsync(v1, "2026-1", 50m, now.AddDays(-3));           // GEÇMİŞ (ödenmemiş)
        await reg.AddInspectionAsync(v2, now.AddDays(-165), now.AddDays(200), 20m); // İleri

        var due = sp.GetRequiredService<DueService>();

        // Araç filtresi: V1'in 2 kalemi (kasko + MTV), V2'nin 1 kalemi (muayene).
        var v1Items = await due.GetForVehicleAsync(v1);
        Assert.Equal(2, v1Items.Count);
        Assert.Contains(v1Items, i => i.Tur == "Kasko" && i.Bucket == DueBucket.YediGun);
        Assert.Contains(v1Items, i => i.Tur == "MTV" && i.Bucket == DueBucket.Gecmis);
        var v2Items = await due.GetForVehicleAsync(v2);
        var inspection = Assert.Single(v2Items);
        Assert.Equal(DueBucket.Ileri, inspection.Bucket);

        // Uyarı sayıları (geçmiş + ≤30): V1=2; V2 sözlükte YOK (uyarısı olmayan araç 0 sayılır).
        var numbers = await due.GetWarningCountsByVehicleAsync();
        Assert.Equal(2, numbers[v1]);
        Assert.False(numbers.ContainsKey(v2));

        // Ödenen MTV vade panosundan düşer → V1 uyarısı 1'e iner (mevcut union davranışı korunuyor).
        var mtvId = (await reg.ListMtvAsync()).Single(m => m.VehicleId == v1).Id;
        await reg.PayMtvAsync(mtvId, LedgerAccountType.Kasa);
        var after = await due.GetWarningCountsByVehicleAsync();
        Assert.Equal(1, after[v1]);
    }
}
