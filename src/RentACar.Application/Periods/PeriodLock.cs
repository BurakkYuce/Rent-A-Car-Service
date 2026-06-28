using RentACar.Application.Common;

namespace RentACar.Application.Periods;

/// <summary>
/// Dönem kilidi kontrol yardımcısı (roadmap D2). Kural: bir defter girişi <see cref="KapanisTarihi"/> ve
/// ÖNCESİNE (gün bazında) postlanamaz. Tek doğruluk kaynağı — tüm postlama yolları bunu kullanır.
/// </summary>
public static class PeriodLock
{
    public static bool IsClosed(DateTimeOffset entryDateUtc, DateTimeOffset? closing)
        => closing is { } c && entryDateUtc.Date <= c.Date;

    public static void ThrowIfClosed(DateTimeOffset entryDateUtc, DateTimeOffset? closing, string? label = null)
    {
        if (IsClosed(entryDateUtc, closing))
            throw new ValidationException(
                $"{(label is null ? "" : label + ": ")}{closing!.Value:yyyy-MM-dd} ve öncesi dönem KAPALI — bu tarihe kayıt yapılamaz.");
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
