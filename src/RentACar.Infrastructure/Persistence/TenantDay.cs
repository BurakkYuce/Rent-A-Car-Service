namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// "Bugün" — TENANT'ın yerel günü (İstanbul), UTC değil.
///
/// <para><b>Neden kritik:</b> belge numarası günü içeriyor. Saat 02:00'de kesilen bir sözleşme
/// UTC'ye göre HÂLÂ bir önceki gündedir; numaraya o gün yazılsaydı belge "dünkü" görünür ve o günün
/// sayacı ertesi gün ikinci kez 001'den başlardı.</para>
///
/// <para><b>Neden statik + opsiyonel <c>tz</c> parametresi (DI değil):</b> numara üretimi repository
/// katmanında, açık bir transaction içinde olur; repository'ler yalnız
/// <c>IDbContextFactory&lt;AppDbContext&gt;</c> alıyor ve DI eklemek 20+ ctor'u kırardı. Repoda emsal
/// var: <c>ExportTarih.Gun(d, tz = null)</c> (test-only parametre) ve job'lardaki statik
/// <c>ResolveTz()</c>. Testler <c>tz</c>'yi AÇIKÇA geçmeli — CI UTC'de koştuğu için varsayılana
/// güvenen bir test kendiliğinden geçer ve hiçbir şey doğrulamaz.</para>
///
/// <para>Tenant-bazlı saat dilimi YOK (TenantSettings'te böyle bir alan yok): uygulama geneli tek
/// dilim. Yurt dışı tenant gerekirse burası ayrılacak tek nokta.</para>
/// </summary>
public static class TenantDay
{
    /// <summary>
    /// Uygulamanın çalıştığı tenant saat dilimi. <c>public</c>: testler bunu AÇIKÇA geçebilsin diye
    /// (repoda <c>InternalsVisibleTo</c> kullanılmıyor) ve iş kodu aynı dilimi tek kaynaktan alsın diye.
    /// </summary>
    public static TimeZoneInfo Slice { get; } = Resolve();

    /// <summary>Windows/Linux id farkını tolere eder; ikisi de yoksa UTC'ye düşer (asla patlamaz).</summary>
    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { /* diğerini dene */ }
            catch (InvalidTimeZoneException) { /* diğerini dene */ }
        }
        return TimeZoneInfo.Utc;
    }

    public static DateOnly Day(DateTimeOffset an, TimeZoneInfo? tz = null)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(an, tz ?? Slice).DateTime);
}
