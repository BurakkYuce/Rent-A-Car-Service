namespace RentACar.Web.Reports;

/// <summary>
/// Bir export'un sunum-bağımsız tanımı: sheet adı + başlıklar + satırlar. Saf veri → ReportExportService.Xlsx/Csv
/// ile byte'a çevrilir. Liste export projeksiyonları (ListExportCatalog) bunu döndürür → birim-test edilebilir
/// (başlık dizisi + hücre eşlemesi tek yerde; sütun değişikliği katalogda). Tam referans sistem sütun setleri:
/// docs/parite/09-export-karsilastirma.md (genişletme referansı).
/// </summary>
public sealed record ExportTable(string Sheet, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);
