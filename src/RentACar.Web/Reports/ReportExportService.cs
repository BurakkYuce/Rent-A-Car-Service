using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace RentACar.Web.Reports;

/// <summary>
/// Rapor export (roadmap B1): tablo verisini (başlıklar + satırlar) Excel (.xlsx, ClosedXML) veya
/// CSV (UTF-8 BOM, Excel-TR uyumlu) byte[]'ine çevirir. Sunum katmanı yardımcısı; veri ReportService'ten.
/// </summary>
public sealed class ReportExportService
{
    public byte[] Xlsx(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Trunc(string.IsNullOrWhiteSpace(sheetName) ? "Rapor" : sheetName, 31));

        for (var i = 0; i < headers.Count; i++)
            ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
                ws.Cell(r, i + 1).Value = ToCell(row[i]);
            r++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] Csv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", headers.Select(Quote))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(",", row.Select(Field))).Append("\r\n");
        // UTF-8 BOM elle eklenir (GetBytes preamble emit etmez) → Excel-TR Türkçe karakterleri doğru okur.
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. enc.GetPreamble(), .. enc.GetBytes(sb.ToString())];
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    private static XLCellValue ToCell(object? v) => v switch
    {
        null => Blank.Value,
        decimal d => d,
        int i => i,
        long l => l,
        double db => db,
        bool b => b,
        // Ham DateTimeOffset hücresi YEREL takvim gününe düşer: tarihler forma yerel gün
        // olarak girilip UTC'ye çevriliyor (FormParse.Date); UTC yazınca +03'te bir gün
        // GERİ görünürdü. Ekranla aynı gün inmeli.
        DateTimeOffset dto => ExportDate.Cell(dto),
        DateTime dt => dt,
        _ => v.ToString() ?? string.Empty
    };

    private static string Fmt(object? v) => v switch
    {
        null => string.Empty,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double db => db.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        DateTimeOffset dto => ExportDate.Day(dto) ?? string.Empty,
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        _ => v.ToString() ?? string.Empty
    };

    /// <summary>
    /// #308 L2 — CSV formül enjeksiyonu (CWE-1236): METİN değer <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, sekme ya da CR ile
    /// başlıyorsa Excel onu formül olarak çalıştırır (ör. müşteri adı <c>=HYPERLINK(…)</c>). Başına <c>'</c> eklenir.
    /// Yalnız metin: sayı/tarih değerleri (negatif tutar <c>-50.00</c> dahil) tipten biçimlenir ve dokunulmaz; 11+ haneli
    /// rakam dizesi kuralı (<see cref="Alan"/>) rakamla başladığı için etkilenmez.
    /// </summary>
    private static string Field(object? value)
    {
        var s = Fmt(value);
        return value is null or decimal or double or float or int or long or short or DateTimeOffset or DateTime
            ? Alan(s)
            : Alan(NeutralizeFormula(s));
    }

    /// <summary>2026-09-25: baştaki boşluk ve satır sonu (LF) atlanıp İLK ANLAMLI karaktere bakılır — Excel
    /// <c>"  =1+1"</c> ya da <c>"\n@SUM(A1)"</c> gibi değerleri de formül olarak çalıştırabiliyor. Sekme ve CR kendileri
    /// tetikleyicidir (değer onlarla başlıyorsa her durumda kaçışlanır). Değer korunur, yalnız başına <c>'</c> eklenir.</summary>
    private static string NeutralizeFormula(string s)
    {
        if (s.Length == 0) return s;
        if (s[0] is '\t' or '\r') return "'" + s;
        var first = s.AsSpan().TrimStart();
        return first.Length > 0 && first[0] is '=' or '+' or '-' or '@' ? "'" + s : s;
    }

    private static string Quote(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    /// <summary>
    /// UZUN ve TAMAMI RAKAM olan dizeler Excel'de bozulur: sayı sanılıp bilimsel gösterime
    /// (<c>2,02626E+12</c>) çevrilir, baştaki sıfır düşer (<c>0532…</c> → <c>532…</c>).
    /// Bunlar MİKTAR değil KİMLİK: belge no (13-16 hane), TC kimlik (11), telefon (10-11).
    ///
    /// <para><c>="…"</c> Excel'e "bu metindir" der. Parasal değerler bu yoldan GEÇMEZ — onlar
    /// <c>decimal</c>/<c>double</c> dalında biçimlenir, dolayısıyla toplanabilirlikleri korunur.</para>
    ///
    /// <para>Xlsx yolu zaten güvenli (<c>XLCellValue</c> metin kalır); bu yalnız CSV içindir.
    /// Bilinen ödün: <c>="…"</c> Excel'e özgüdür, ham CSV okuyan bir tüketici bunu aynen görür.</para>
    /// </summary>
    private static string Alan(string s)
        // SIRA ÖNEMLİ: `="…"` biçimi Quote'tan GEÇMEMELİ — geçerse tırnaklar kaçışlanır
        // (`"=""123"""`) ve Excel formülü artık metin sanır, hile işlevini yitirir. Rakam dizesi
        // virgül/satırsonu içeremeyeceği için ham yazmak CSV açısından da güvenlidir.
        => s.Length >= 11 && s.All(char.IsAsciiDigit) ? $"=\"{s}\"" : Quote(s);
}
