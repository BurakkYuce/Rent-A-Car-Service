namespace RentACar.Web.Reports;

/// <summary>
/// Export tarih biçimleyicisi — TEK kural, TEK yer.
///
/// <para><b>Kural: YEREL TAKVİM GÜNÜ.</b> Tarih alanları forma yerel gün olarak girilir ve
/// <c>FormParse.Date</c> bunu UTC'ye çevirir (01.03 00:00 +03 → 28.02 21:00 UTC). Ham UTC ile
/// yazılınca export "28.02", ekran "01.03" gösteriyordu — canlı duman testinde yakalanan
/// <b>bir gün geri</b> hatası. Ekranlar <c>.LocalDateTime</c> kullandığı için export de öyle
/// yazar: "gördüğün = indirdiğin".</para>
///
/// <para><b>Neden ayrı bir tip:</b> kural bir dönem iki biçimleyiciye (ham-UTC <c>D</c> ve yerel
/// <c>DG</c>) bölünmüştü ve ham olan tek tek export'larda gün kaydırıyordu. Kuralı tipe taşımak
/// hem tek kaynak yapar hem de <paramref name="tz"/> sayesinde SAAT DİLİMİNDEN BAĞIMSIZ test
/// edilebilir kılar: CI UTC'de koştuğu için <c>.LocalDateTime</c>'a doğrudan bakan bir test
/// kuralı doğrulayamaz (UTC'de yerel gün = UTC gün, iddia kendiliğinden geçer).</para>
/// </summary>
public static class ExportDate
{
    /// <summary>
    /// Tarihi yerel takvim günü olarak <c>yyyy-MM-dd</c> yazar; <c>null</c> → <c>null</c>.
    /// </summary>
    /// <param name="tz">
    /// Yalnız TEST için: dönüşümün yapılacağı saat dilimi. Üretim çağrıları bu parametreyi
    /// vermez ve <see cref="TimeZoneInfo.Local"/> kullanılır.
    /// </param>
    public static string? Day(DateTimeOffset? d, TimeZoneInfo? tz = null)
        => d is { } v ? TimeZoneInfo.ConvertTime(v, tz ?? TimeZoneInfo.Local).ToString("yyyy-MM-dd") : null;

    /// <summary>Excel hücresi için yerel gün-saat (metin değil, gerçek tarih hücresi).</summary>
    public static DateTime Cell(DateTimeOffset d, TimeZoneInfo? tz = null)
        => TimeZoneInfo.ConvertTime(d, tz ?? TimeZoneInfo.Local).DateTime;
}
