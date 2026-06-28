using System.Text;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap B1 — Rapor export (ReportExportService). CSV başlık+satır+kaçış; XLSX geçerli (zip PK imzası)
/// ve dolu. Bağımsız oracle: beklenen metin/baytlar senaryodan.
/// </summary>
public sealed class ReportExportTests
{
    private static readonly string[] Headers = ["Cari", "Bakiye"];
    private static IReadOnlyList<object?[]> Rows =>
    [
        new object?[] { "Acme A.Ş.", 1500.50m },
        new object?[] { "Virgül, Ltd", 2000m }   // virgül → CSV kaçışı test eder
    ];

    [Fact]
    public void Csv_has_header_and_rows_with_escaping()
    {
        var svc = new ReportExportService();
        var bytes = svc.Csv(Headers, Rows);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Cari,Bakiye", text);                 // başlık
        Assert.Contains("Acme A.Ş.,1500.50", text);           // satır 1 (invariant ondalık)
        Assert.Contains("\"Virgül, Ltd\",2000", text);        // virgüllü alan tırnaklandı
        Assert.Equal(0xEF, bytes[0]); Assert.Equal(0xBB, bytes[1]); Assert.Equal(0xBF, bytes[2]); // UTF-8 BOM (Excel-TR)
    }

    [Fact]
    public void Xlsx_is_nonempty_valid_zip()
    {
        var svc = new ReportExportService();
        var bytes = svc.Xlsx("Cari Bakiye", Headers, Rows);

        Assert.True(bytes.Length > 0);
        // .xlsx = ZIP → "PK" imzası (0x50 0x4B).
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact]
    public void Empty_rows_still_writes_header()
    {
        var svc = new ReportExportService();
        var csv = Encoding.UTF8.GetString(svc.Csv(Headers, []));
        Assert.Contains("Cari,Bakiye", csv);
        Assert.True(svc.Xlsx("Boş", Headers, []).Length > 0);
    }
}
