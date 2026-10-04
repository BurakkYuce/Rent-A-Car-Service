namespace RentACar.Web.Reports;

/// <summary>
/// Excel sayfa (worksheet) adı kuralları — TEK yer. Excel (ve ClosedXML) şu adları reddeder ve export 500 verir:
/// <c>\ / ? * [ ] :</c> karakterlerinden birini içeren, 31 karakterden uzun, boş, kesme işaretiyle (<c>'</c>) başlayan
/// ya da biten ad ve ayrılmış "History" adı. Kabul bulgusu d-rapor-karsilastirmali-04: "Karşılaştırmalı Analiz
/// (Kira / Adet)" sayfa adındaki "/" Excel export'unu 500'e düşürüyordu.
/// </summary>
public static class ExcelSheetName
{
    public const int MaxLength = 31;
    private const string Fallback = "Rapor";
    private static readonly char[] Forbidden = ['\\', '/', '?', '*', '[', ']', ':'];

    /// <summary>Yasak karakterleri <c>-</c> yapar, 31 karakterde keser, uçtaki boşluk/kesme işaretini atar.</summary>
    public static string Clean(string? name)
    {
        var chars = (name ?? string.Empty).Select(c => Array.IndexOf(Forbidden, c) >= 0 || char.IsControl(c) ? '-' : c);
        var s = new string([.. chars]).Trim().Trim('\'').Trim();
        if (s.Length > MaxLength) s = s[..MaxLength].TrimEnd().TrimEnd('\'').TrimEnd();
        if (s.Length == 0) return Fallback;
        return string.Equals(s, "History", StringComparison.OrdinalIgnoreCase) ? s + "-" : s;
    }
}
