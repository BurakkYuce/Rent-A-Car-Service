using RentACar.Web.Api.Rapor;

namespace RentACar.Web.Reports;

/// <summary>
/// Rapor export uçlarının tarih parametreleri — EKRANLA AYNI kural (<see cref="ReportPeriod"/>). Ekrandaki export
/// bağlantıları günü <c>yyyy-MM-dd</c> İSTANBUL günü olarak taşır (<see cref="ReportExport.Day"/>):
/// <list type="bullet">
/// <item><see cref="From"/>/<see cref="To"/> = günün gerçek İstanbul başlangıcı / bitiş gününün SONU
/// (<see cref="ReportPeriod.FromUtc"/>/<see cref="ReportPeriod.ToUtc"/>; ekran uçlarının çoğu bunu kullanır).</item>
/// <item><see cref="FromAnchor"/>/<see cref="ToAnchor"/> = günün UTC gece yarısı çıpası (servis girdiyi takvim gününe
/// indiren raporlar: doluluk, araç durum takip — ekranları da çıpa kullanır).</item>
/// </list>
/// Gün değil tam zaman damgası verilirse (eski el yapımı bağlantılar) değer olduğu gibi UTC'ye çevrilir.
/// <para>Eski davranış: <c>FormParse.Date</c> çıplak günü SUNUCUNUN yerel gece yarısına çeviriyordu — +03 sunucuda
/// gün bir geri kayıyordu ve <c>to</c> bitiş gününün BAŞI olduğu için bitiş günü dosyaya hiç girmiyordu.</para>
/// <para><c>public</c>: saat diliminden bağımsız saf testler doğrudan çağırabilsin diye (repoda InternalsVisibleTo yok).</para>
/// </summary>
public sealed record ExportPeriod(
    DateTimeOffset? From, DateTimeOffset? To, DateTimeOffset? FromAnchor, DateTimeOffset? ToAnchor)
{
    public static ExportPeriod Parse(string? from, string? to)
    {
        var fromDay = Day(from, "from");
        var toDay = Day(to, "to");
        var p = new ReportPeriod(fromDay, toDay);
        return new ExportPeriod(
            fromDay is null ? FormParse.Date(from) : p.FromUtc,
            toDay is null ? FormParse.Date(to) : p.ToUtc,
            fromDay is { } f ? ReportPeriod.Anchor(f) : FormParse.Date(from),
            toDay is { } t ? ReportPeriod.Anchor(t) : FormParse.Date(to));
    }

    /// <summary>Tek gün (<c>gun</c>): günün UTC çıpası; boşsa BUGÜN (İstanbul) — ekran varsayılanıyla aynı.</summary>
    public static DateTimeOffset DayAnchor(string? value)
        => Day(value, "gun") is { } d ? ReportPeriod.Anchor(d)
            : FormParse.Date(value) ?? ReportPeriod.Anchor(ReportPeriod.Today);

    /// <summary>"Bu güne kadar" (<c>asOf</c>): günün UTC çıpasının sonu (yaşlandırma ekranıyla aynı); boşsa null.</summary>
    public static DateTimeOffset? DayEnd(string? value)
        => Day(value, "asOf") is { } d ? ReportPeriod.Anchor(d).AddDays(1).AddMicroseconds(-1) : FormParse.Date(value);

    /// <summary>Yalnız ÇIPLAK gün (yyyy-MM-dd) gün sayılır; yıl sınırı ekranla aynı.</summary>
    private static DateOnly? Day(string? value, string field)
    {
        var s = (value ?? string.Empty).Trim();
        return s.Length == 10 && FormParse.Day(s) is { } d ? ReportPeriod.ValidateDay(d, field) : null;
    }
}
