using RentACar.Application.Bookings;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR4c — ComputeGun canlı TürevRent kalibrasyonu: 24h tam blok + ~3sa kısmi eşiği (eski ceil DEĞİL).
/// BAĞIMSIZ ORACLE: beklenen günler TürevRent gün-hesabı tablosundan (elle), eşik-uzağı senaryolarla.
/// Birim test (DB yok).
/// </summary>
public sealed class GunKalibrasyonTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
    private static int Gun(double saat) => BookingMath.ComputeGun(T0, T0.AddHours(saat));

    [Theory]
    [InlineData(24, 1)]    // tam 1 gün
    [InlineData(26, 1)]    // 1 gün + 2sa kısmi (< eşik) → 1 (eski ceil: 2 — DÜZELDİ)
    [InlineData(28, 2)]    // 1 gün + 4sa kısmi (> eşik) → 2
    [InlineData(48, 2)]    // tam 2 gün
    [InlineData(50, 2)]    // 2 gün + 2sa (< eşik) → 2 (eski ceil: 3)
    [InlineData(52, 3)]    // 2 gün + 4sa (> eşik) → 3
    [InlineData(72, 3)]    // tam 3 gün
    [InlineData(1, 1)]     // < 1 gün → min 1
    [InlineData(3, 1)]     // 3sa → min 1 (tam gün 0, kısmi 3 > eşik ama Max(1,1)=1)
    public void ComputeGun_kismi_esik_kurali(double saat, int beklenen)
        => Assert.Equal(beklenen, Gun(saat));

    [Fact]
    public void Tam_gun_araliklari_ceil_ile_ayni_regresyon()
    {
        // Tüm mevcut testler tam-gün aralığı kullanır → floor+eşik ceil ile AYNI (regresyon güvencesi).
        for (var g = 1; g <= 30; g++)
            Assert.Equal(g, BookingMath.ComputeGun(T0, T0.AddDays(g)));
    }
}
