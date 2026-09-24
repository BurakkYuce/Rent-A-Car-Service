using RentACar.Application.Common;

namespace RentACar.Application.Periods;

/// <summary>
/// Dönem kilidi kontrol yardımcısı (roadmap D2). Kural: bir defter girişi kapanış GÜNÜ ve ÖNCESİNE postlanamaz.
/// Tek doğruluk kaynağı — tüm postlama yolları bunu kullanır.
/// <para><b>Gün İSTANBUL takvim günüdür (F8.1a adversarial M2):</b> önce karşılaştırma UTC günüyle yapılıyordu.
/// İstanbul'da 00:00–03:00 arasında UTC hâlâ önceki gündedir; "dünü kilitle" sonrası bu saatlerde girilen BUGÜN
/// tarihli her kayıt "dün ve öncesi KAPALI" diye reddediliyordu. Hem giriş anı hem kapanış kaydı İstanbul gününe
/// çevrilir. Kapanış kaydı hangi temsil ile yazılmış olursa olsun (UTC gece yarısı ya da İstanbul gece yarısının
/// UTC karşılığı) aynı güne düşer.</para>
/// </summary>
public static class PeriodLock
{
    /// <summary>Uygulama geneli tenant saat dilimi (Infrastructure <c>TenantGun.Dilim</c> ile aynı çözüm).</summary>
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { /* diğerini dene */ }
            catch (InvalidTimeZoneException) { /* diğerini dene */ }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>Anın İstanbul takvim günü.</summary>
    public static DateOnly LocalDay(DateTimeOffset instant)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>Kapanış gününün SON anı (İstanbul gün sonu − 1 tick), UTC — kapanış fişi bu ana kadarki bakiyeyi kapatır.</summary>
    public static DateTimeOffset DayEndUtc(DateOnly day)
    {
        var nextMidnight = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(nextMidnight, Zone.GetUtcOffset(nextMidnight)).ToUniversalTime().AddTicks(-1);
    }

    public static bool IsClosed(DateTimeOffset entryDateUtc, DateTimeOffset? closing)
        => closing is { } c && LocalDay(entryDateUtc) <= LocalDay(c);

    public static void ThrowIfClosed(DateTimeOffset entryDateUtc, DateTimeOffset? closing, string? label = null)
    {
        if (IsClosed(entryDateUtc, closing))
            throw new ValidationException(
                $"{(label is null ? "" : label + ": ")}{LocalDay(closing!.Value):yyyy-MM-dd} ve öncesi dönem KAPALI — bu tarihe kayıt yapılamaz.");
    }
}

/// <summary>Postlama yollarının çağırdığı dönem-kilidi guard'ı. Tüm para servisleri buna bağımlıdır.</summary>
public interface IPeriodLockGuard
{
    /// <summary>Tenant'ın kapanış tarihi (yoksa null). Batch'te bir kez okuyup yerelde karşılaştırmak için.</summary>
    Task<DateTimeOffset?> GetClosingDateAsync(CancellationToken ct = default);

    /// <summary>Verilen giriş tarihi kapalı dönemdeyse ValidationException atar.</summary>
    Task EnsureOpenAsync(DateTimeOffset entryDateUtc, CancellationToken ct = default);
}
