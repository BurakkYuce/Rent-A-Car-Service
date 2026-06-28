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
            sb.Append(string.Join(",", row.Select(c => Quote(Fmt(c))))).Append("\r\n");
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
        DateTimeOffset dto => dto.DateTime,
        DateTime dt => dt,
        _ => v.ToString() ?? string.Empty
    };

    private static string Fmt(object? v) => v switch
    {
        null => string.Empty,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double db => db.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd"),
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        _ => v.ToString() ?? string.Empty
    };

    private static string Quote(string s)
        => s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
