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
    public void Uzun_rakam_dizeleri_CSV_de_metne_zorlanir()
    {
        // Belge no artık 13-16 hane. Excel bunu SAYI sanıp "2,02626E+12" gösteriyordu; TC kimlik
        // (11 hane) ve telefonda da baştaki sıfır düşüyordu. Bunlar miktar değil KİMLİK.
        var svc = new ReportExportService();
        var csv = Encoding.UTF8.GetString(svc.Csv(
            ["No", "TC", "Tel", "Tutar", "Plaka"],
            [new object?[] { "2026260801001", "11111111110", "05321112233", 1500.50m, "34ABC123" }]));

        Assert.Contains("=\"2026260801001\"", csv);   // belge no
        Assert.Contains("=\"11111111110\"", csv);     // TC
        Assert.Contains("=\"05321112233\"", csv);     // telefon (baştaki sıfır korunur)

        // Parasal değer DOKUNULMADAN kalır — decimal dalından geçer, toplanabilirliği bozulmaz.
        Assert.Contains(",1500.50,", csv);
        Assert.DoesNotContain("=\"1500.50\"", csv);
        // Kısa / harf içeren dizeler de dokunulmaz.
        Assert.Contains("34ABC123", csv);
        Assert.DoesNotContain("=\"34ABC123\"", csv);
    }

    // #308 L2 — formül enjeksiyonu: formül karakteriyle başlayan METİN başına ' alır; sayılar ve 11+ hane kuralı aynen.
    [Fact]
    public void Csv_neutralizes_formula_text_but_not_numbers()
    {
        var svc = new ReportExportService();
        var csv = Encoding.UTF8.GetString(svc.Csv(
            ["A", "B", "C", "D", "E", "F", "G", "H", "I"],
            [new object?[] { "=HYPERLINK(\"http://x\")", "+90 532", "-kampanya", "@SUM(A1)", "\tsekme", -50.25m, -3, "05321112233", "Normal" }]));
        var line = csv.Split("\r\n")[1];

        // ELLE beklenen satır: formül metinleri '-önekli (tırnaklı olan, içindeki " kaçışıyla), negatif sayılar ham,
        // 11 haneli rakam ="…", sıradan metin dokunulmaz.
        Assert.Equal("\"'=HYPERLINK(\"\"http://x\"\")\",'+90 532,'-kampanya,'@SUM(A1),'\tsekme,-50.25,-3,=\"05321112233\",Normal", line);
    }

    // 2026-09-25 — Excel baştaki boşluk ve satır sonunu atlayıp formülü yine çalıştırabilir: kaçış ilk ANLAMLI karaktere
    // bakar. Değer olduğu gibi korunur, yalnız başına ' eklenir; LF içeren alan CSV kuralıyla tırnaklanır.
    [Fact]
    public void Csv_neutralizes_formula_after_leading_space_and_line_feed()
    {
        var svc = new ReportExportService();
        var csv = Encoding.UTF8.GetString(svc.Csv(
            ["A", "B", "C", "D", "E"],
            [new object?[] { "  =1+1", "\n@SUM(A1)", " \n -kampanya", "  Normal", "   " }]));
        var body = csv[(csv.IndexOf("\r\n", StringComparison.Ordinal) + 2)..];

        // ELLE: "  =1+1" → "'  =1+1"; "\n@SUM(A1)" LF içerdiği için tırnaklı → "\"'\n@SUM(A1)\""; üçüncüsü aynı;
        // formül karakteri olmayan ve tamamen boşluk olan değerler dokunulmaz.
        Assert.Equal("'  =1+1,\"'\n@SUM(A1)\",\"' \n -kampanya\",  Normal,   \r\n", body);
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
