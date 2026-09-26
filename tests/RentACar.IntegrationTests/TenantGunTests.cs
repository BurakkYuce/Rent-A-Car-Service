using RentACar.Infrastructure.Persistence;

namespace RentACar.IntegrationTests;

/// <summary>
/// Belge numarasındaki "gün" TENANT'ın yerel günüdür (İstanbul), UTC değil.
///
/// <para><b>Neden önemli:</b> saat 02:00'de kesilen bir sözleşme UTC'ye göre hâlâ bir önceki
/// gündedir. Numaraya o gün yazılsaydı belge "dünkü" görünür ve o günün sayacı ertesi gün ikinci
/// kez 001'den başlardı.</para>
///
/// <para><b>tz AÇIKÇA geçilir:</b> CI UTC'de koşar; varsayılana güvenen bir test orada kendiliğinden
/// geçer ve hiçbir şey doğrulamaz (<c>ExportTarih</c>'te öğrenilen ders).</para>
/// </summary>
public sealed class TenantGunTests
{
    private static readonly TimeZoneInfo Istanbul = TenantDay.Slice;

    [Fact]
    public void Gece_yarisindan_sonra_YEREL_gune_gecer()
    {
        // 26 Ağu 21:30 UTC = 27 Ağu 00:30 İstanbul (UTC+3) → belge 27 Ağustos'a yazılmalı.
        Assert.Equal(new DateOnly(2026, 8, 27),
            TenantDay.Day(new DateTimeOffset(2026, 8, 26, 21, 30, 0, TimeSpan.Zero), Istanbul));
    }

    [Fact]
    public void Gece_yarisindan_once_ayni_gunde_kalir()
    {
        // 26 Ağu 20:30 UTC = 26 Ağu 23:30 İstanbul → hâlâ 26 Ağustos.
        Assert.Equal(new DateOnly(2026, 8, 26),
            TenantDay.Day(new DateTimeOffset(2026, 8, 26, 20, 30, 0, TimeSpan.Zero), Istanbul));
    }

    [Fact]
    public void Ayin_ve_yilin_son_gunu_dogru_devreder()
    {
        // 31 Ara 21:30 UTC = 1 Oca 00:30 İstanbul → yıl da devreder (fatura sayacı için kritik).
        Assert.Equal(new DateOnly(2027, 1, 1),
            TenantDay.Day(new DateTimeOffset(2026, 12, 31, 21, 30, 0, TimeSpan.Zero), Istanbul));
    }

    [Fact]
    public void Offsetli_girdi_de_dogru_cevrilir()
    {
        // Çağıran +03:00 offsetli bir an verirse de sonuç aynı olmalı (mutlak an aynı).
        var utc = new DateTimeOffset(2026, 8, 26, 21, 30, 0, TimeSpan.Zero);
        var yerel = new DateTimeOffset(2026, 8, 27, 0, 30, 0, TimeSpan.FromHours(3));
        Assert.Equal(TenantDay.Day(utc, Istanbul), TenantDay.Day(yerel, Istanbul));
    }

    [Fact]
    public void Saat_dilimi_cozulebiliyor()
        // Platform (Mac/Linux/CI) hangisi olursa olsun İstanbul bulunmalı; bulunamazsa UTC'ye
        // düşer ve gece yarısı testleri kırmızıya döner — sessizce yanlış güne yazmaktan iyidir.
        => Assert.Equal(TimeSpan.FromHours(3), Istanbul.GetUtcOffset(new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc)));
}
