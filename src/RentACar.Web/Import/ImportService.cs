using ClosedXML.Excel;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;

namespace RentACar.Web.Import;

/// <summary>İçe-aktarım sonucu: eklenen / atlanan (tekrar) / hatalı satır + ilk hataların özeti.</summary>
public sealed record ImportResult(int Eklenen, int Atlanan, int Hatali, IReadOnlyList<string> Hatalar)
{
    /// <summary>F11.2d — TÜM hatalı satırlar, etiket (plaka / müşteri adı / tarife kodu) ve mesaj AYRI. Kişisel veri
    /// taşımaması gereken yüzeyler (SPA içe aktarım özeti) yalnız <see cref="ImportError.Message"/>'ı kullanır.</summary>
    public IReadOnlyList<ImportError> Errors { get; init; } = [];
}

/// <summary>Hatalı satır: <paramref name="Label"/> satırı tanıtan değer (kişisel veri olabilir), <paramref name="Message"/>
/// servis doğrulama mesajı.</summary>
public sealed record ImportError(string Label, string Message);

/// <summary>F11.2d (H1) — ayrıştırma sınırları: bellek tüketimi dosya boyutundan bağımsız olarak sınırlı kalır.</summary>
public static class ImportLimits
{
    /// <summary>Veri satırı üst sınırı (başlık hariç); aşılınca ayrıştırma ANINDA durur.</summary>
    public const int MaxRows = 20_000;
    /// <summary>Sütun (başlık hücresi) üst sınırı.</summary>
    public const int MaxColumns = 100;
    /// <summary>Başlık satırının karakter üst sınırı (CSV) / tek başlık hücresi (xlsx).</summary>
    public const int MaxHeaderChars = 8_192;
    public const int MaxHeaderCellChars = 256;
    /// <summary>CSV veri satırının karakter üst sınırı.</summary>
    public const int MaxLineChars = 65_536;
    /// <summary>Okunan girdi üst sınırı (SPA ucu 5 MB'ı ayrıca denetler; Blazor yolu bununla sınırlı).</summary>
    public const long MaxInputBytes = 8L * 1024 * 1024;
    /// <summary>xlsx (ZIP) açılmış toplam boyut üst sınırı — sıkıştırma bombasına karşı, yükleme öncesi sayılır.</summary>
    public const long MaxUncompressedBytes = 16L * 1024 * 1024;
    public const int MaxZipEntries = 2_000;
    /// <summary>xlsx: tüm sayfalardaki toplam hücre (<c>&lt;c&gt;</c>) üst sınırı — ClosedXML yüklemesinden ÖNCE akışla sayılır.</summary>
    /// ClosedXML hücre başına ~1,7 KB ayırıyor (inceleme ölçümü: 500.000 hücre ≈ 870 MB, 3 sn) → 500.000.
    public const int MaxCells = 500_000;
}

/// <summary>
/// Veri göçü / onboarding: referans sistem (veya benzeri) Excel/CSV export'undan araç + müşteri içe-aktarımı.
/// TASARIM: gerçek PII (TC/ehliyet/pasaport) CustomerService üzerinden yazılır → at-rest ŞİFRELİ (ISecretProtector)
/// + blind-index; DÜZ metin DB'ye GİRMEZ. Benzersizlik (plaka / TC-hash) servis katmanında zorlanır → tekrarlar
/// atlanır. Uç ManageUsers-gate'li (PII toplu-yazımı). Başlık eşleme esnek (Türkçe-katlı + alias'lar).
/// </summary>
public sealed class ImportService(VehicleService vehicles, CustomerService customers, RateMatrixService rateMatrices)
{
    private readonly VehicleService _vehicles = vehicles;
    private readonly CustomerService _customers = customers;
    private readonly RateMatrixService _rateMatrices = rateMatrices;

    // ---------- Ayrıştırma ----------
    /// <summary>
    /// Excel/CSV → satır sözlükleri. F11.2d (H1) sınırları <see cref="ImportLimits"/>: girdi en çok 8 MB okunur, sütun
    /// ≤ 100, başlık uzunluğu sınırlı, satır sınırı ayrıştırma SIRASINDA (aşılınca anında red), boş/eksik değer sözlüğe
    /// yazılmaz (satır başına bellek = dolu hücre sayısı), xlsx'in ZIP girdileri yüklemeden ÖNCE gerçekten açılarak
    /// sayılır (sıkıştırma bombası). İhlal → <see cref="ValidationException"/> (<c>dosya</c>). Blazor ve SPA aynı yol.
    /// </summary>
    public static IReadOnlyList<Dictionary<string, string>> Parse(Stream stream, string fileName)
    {
        var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                   || fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
        using var buffer = CopyBounded(stream);
        try
        {
            return isExcel ? ParseExcel(buffer) : ParseCsv(buffer);
        }
        catch (Exception ex) when (ex is not ValidationException and not OperationCanceledException and not OutOfMemoryException)
        {
            // Bozuk dosya (ClosedXML/OpenXML/ZIP/XML istisnası) 500 değil: Blazor sayfası mesaj, SPA 400 errors[dosya].
            throw Refuse("Dosya okunamadı (biçim bozuk ya da desteklenmiyor).");
        }
    }

    private static ValidationException Refuse(string message) => new(message, "dosya");

    private static ValidationException TooManyRows()
        => Refuse("Tek seferde en çok 20.000 satır aktarılabilir."); // = ImportLimits.MaxRows

    private static MemoryStream CopyBounded(Stream s)
    {
        var ms = new MemoryStream();
        var chunk = new byte[81_920];
        int n;
        while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (ms.Length + n > ImportLimits.MaxInputBytes)
                throw Refuse($"Dosya en fazla {ImportLimits.MaxInputBytes / (1024 * 1024)} MB olabilir.");
            ms.Write(chunk, 0, n);
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>xlsx bir ZIP'tir: girdi sayısı ve AÇILMIŞ toplam boyut (başlıktaki beyana güvenmeden, gerçekten açıp
    /// sayarak) sınırlanır; ancak sonra ClosedXML'e verilir.</summary>
    private static void CheckZip(MemoryStream ms)
    {
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > ImportLimits.MaxZipEntries) throw Refuse("Dosya çok fazla bileşen içeriyor.");
            long total = 0;
            var chunk = new byte[81_920];
            foreach (var entry in zip.Entries)
            {
                using var es = entry.Open();
                int n;
                while ((n = es.Read(chunk, 0, chunk.Length)) > 0)
                    if ((total += n) > ImportLimits.MaxUncompressedBytes)
                        throw Refuse("Dosyanın açılmış boyutu çok büyük.");
            }
            // #308 ikinci tur: ClosedXML tüm çalışma kitabını belleğe kurar (127 KB'lık 20.000×100 dosya 3,5 GB ayırdı).
            // Satır/sütun/hücre sınırları bu yüzden yüklemeden ÖNCE, sayfa XML'i akışla okunarak denetlenir. ClosedXML
            // TÜM sayfaları yüklediği için yalnız ilk sayfa değil her sayfa sayılır (toplam hücre tüm kitap için).
            // #308 L1: sayfanın yolu serbesttir (workbook.xml.rels hedefi; ör. "xl/tasinmis/veri.dat") — yol süzgeci
            // taşınmış sayfayı saymıyordu. Bu yüzden XML olan HER girdi taranır; yalnız SpreadsheetML ad alanındaki
            // <row>/<c> sayılır (başka parçalardaki aynı adlı öğeler sayılmaz). XML olmayan girdi (görsel vb.) atlanır.
            long cells = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue; // klasör girdisi
                if (!LooksLikeXml(entry)) continue;
                using var es = entry.Open();
                cells = ScanSheet(es, cells);
            }
        }
        catch (InvalidDataException)
        {
            throw Refuse("Dosya okunamadı (biçim bozuk ya da desteklenmiyor).");
        }
        catch (System.Xml.XmlException)
        {
            throw Refuse("Dosya okunamadı (biçim bozuk ya da desteklenmiyor).");
        }
        ms.Position = 0;
    }

    /// <summary>
    /// #308 L1 — girdi XML mi? XmlReader kodlamayı BOM'suz da ilk baytlardan tanır (UTF-16 BE/LE, UCS-4) — r314 M1:
    /// yalnız UTF-8 bakılınca BOM'suz UTF-16BE sayfa (<c>00 3C 00 3F</c>) taramayı atlayıp 501.000 hücreyle ClosedXML'e
    /// ulaşıyordu. Bu yüzden: herhangi bir BOM (UTF-8/16/32) → XML; değilse UTF-8, UTF-16 LE/BE ve UCS-4 LE/BE kod
    /// birimlerinin HER BİRİNDE baştaki boşluk atlanıp ilk birim <c>&lt;</c> ise (diğer baytları 0) → XML. Yanlış pozitif
    /// güvenli yöndedir: XML sanılan ikili girdi taramada <see cref="System.Xml.XmlException"/> verir → red. Yalnız hiçbir
    /// kodlamada <c>&lt;</c> ile başlamayan ikili girdiler (png, jpeg, emf, printerSettings.bin…) atlanır — XmlReader ve
    /// dolayısıyla ClosedXML onları sayfa olarak okuyamaz.
    /// </summary>
    private static bool LooksLikeXml(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])
            || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF])
            || head.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFF, 0xFE])) // UCS-4 2143 BOM (3412 BOM FE FF ile yakalanır)
            return true;
        // (genişlik, anlamlı baytın birim içindeki yeri): UTF-8; UTF-16 LE/BE; UCS-4 LE (1234 ters = 4321), 2143, 3412, BE (1234).
        // 2026-09-25: UCS-4 2143 (<c>00 00 3C 00</c>) eksikti — XmlReader bu sırayı da tanır ve 3412'yi UTF-16BE yakalıyordu
        // ama 2143 hiçbir kalıba uymuyor, sayfa taranmadan ClosedXML'e gidiyordu. Dört sıra artık açıkça sayılır.
        foreach (var (width, lowIndex) in new[] { (1, 0), (2, 0), (2, 1), (4, 0), (4, 2), (4, 1), (4, 3) })
            if (StartsWithTag(head, width, lowIndex)) return true;
        return false;
    }

    /// <summary>Verilen kod birimi genişliğinde (anlamlı bayt <paramref name="lowIndex"/>'te, diğerleri 0) baştaki XML
    /// boşluğu atlanınca ilk birim '&lt;' mi (ya da tümü boşluk mu).</summary>
    private static bool StartsWithTag(ReadOnlySpan<byte> head, int width, int lowIndex)
    {
        var i = 0;
        for (; i + width <= head.Length; i += width)
        {
            var unit = head.Slice(i, width);
            var low = unit[lowIndex];
            if (unit[..lowIndex].IndexOfAnyExcept((byte)0) >= 0 || unit[(lowIndex + 1)..].IndexOfAnyExcept((byte)0) >= 0)
                return false; // ASCII dışı birim: bu kodlamada '<' ya da boşluk değil
            if (low == (byte)'<') return true;
            if (low is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) return false;
        }
        return true; // tamamen boşluk (ya da boş) → taransın, zararsız
    }

    private static bool LooksLikeXml(System.IO.Compression.ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        var buf = new byte[4096];
        var read = 0;
        int n;
        while (read < buf.Length && (n = s.Read(buf, read, buf.Length - read)) > 0) read += n;
        return LooksLikeXml(buf.AsSpan(0, read));
    }

    private static readonly HashSet<string> SpreadsheetNamespaces = new(StringComparer.Ordinal)
    {
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main", // Transitional
        "http://purl.oclc.org/ooxml/spreadsheetml/main",              // Strict
    };

    /// <summary>Sayfa XML'inde <c>&lt;row&gt;</c> ve <c>&lt;c&gt;</c> öğelerini akışla sayar (DOM yok, DTD yasak); satır
    /// (başlık dahil) &gt; MaxRows+1, satırda hücre &gt; MaxColumns ya da toplam hücre &gt; MaxCells → anında red.</summary>
    private static long ScanSheet(Stream sheet, long cellsSoFar)
    {
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true,
            IgnoreWhitespace = true, IgnoreProcessingInstructions = true,
        };
        using var reader = System.Xml.XmlReader.Create(sheet, settings);
        int rows = 0, rowCells = 0;
        var cells = cellsSoFar;
        while (reader.Read())
        {
            if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;
            if (!SpreadsheetNamespaces.Contains(reader.NamespaceURI)) continue;
            if (reader.LocalName == "row")
            {
                if (++rows > ImportLimits.MaxRows + 1) throw TooManyRows();
                rowCells = 0;
            }
            else if (reader.LocalName == "c")
            {
                if (++rowCells > ImportLimits.MaxColumns)
                    throw Refuse($"Dosya en çok {ImportLimits.MaxColumns} sütun içerebilir.");
                if (++cells > ImportLimits.MaxCells) throw Refuse(
                    $"Dosya en çok {ImportLimits.MaxCells.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} dolu hücre içerebilir.");
            }
        }
        return cells;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseExcel(MemoryStream s)
    {
        CheckZip(s);
        var rows = new List<Dictionary<string, string>>();
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.FirstOrDefault();
        var used = ws?.RangeUsed();
        if (used is null) return rows;
        if (used.ColumnCount() > ImportLimits.MaxColumns)
            throw Refuse($"Dosya en çok {ImportLimits.MaxColumns} sütun içerebilir.");
        if (used.RowCount() - 1 > ImportLimits.MaxRows) throw TooManyRows();
        var headers = used.FirstRow().Cells().Select(c => c.GetString()).ToList();
        if (headers.Any(h => h.Length > ImportLimits.MaxHeaderCellChars))
            throw Refuse("Başlık hücresi çok uzun.");
        var keys = headers.Select(Norm).ToList();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i])) continue;
                var v = row.Cell(i + 1).GetString().Trim();
                if (v.Length > 0) dict[keys[i]] = v;
            }
            if (dict.Count > 0) rows.Add(dict);
        }
        return rows;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseCsv(Stream s)
    {
        var rows = new List<Dictionary<string, string>>();
        using var reader = new StreamReader(s);
        var lineBuffer = new System.Text.StringBuilder();
        var headerLine = ReadLineBounded(reader, lineBuffer, ImportLimits.MaxHeaderChars, "Başlık satırı çok uzun.");
        if (headerLine is null) return rows;
        var sep = headerLine.Contains(';') && !headerLine.Contains(',') ? ';' : (headerLine.Contains(';') ? ';' : ',');
        var headerCells = SplitCsv(headerLine, sep);
        if (headerCells.Count > ImportLimits.MaxColumns)
            throw Refuse($"Dosya en çok {ImportLimits.MaxColumns} sütun içerebilir.");
        var headers = headerCells.Select(Norm).ToList();
        string? line;
        while ((line = ReadLineBounded(reader, lineBuffer, ImportLimits.MaxLineChars, "Bir satır çok uzun.")) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (rows.Count >= ImportLimits.MaxRows) throw TooManyRows();
            var vals = SplitCsv(line, sep, headers.Count);
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < headers.Count && i < vals.Count; i++)
            {
                if (string.IsNullOrEmpty(headers[i])) continue;
                var v = vals[i].Trim();
                if (v.Length > 0) dict[headers[i]] = v;
            }
            rows.Add(dict);
        }
        return rows;
    }

    /// <summary><see cref="TextReader.ReadLine"/> gibi ama en çok <paramref name="max"/> karakter; aşılınca red. Tampon
    /// satırlar arasında paylaşılır; boş satır paylaşılan <see cref="string.Empty"/> döner (milyonlarca boş satır ayırma
    /// üretmesin).</summary>
    private static string? ReadLineBounded(TextReader r, System.Text.StringBuilder sb, int max, string tooLong)
    {
        sb.Clear();
        int c;
        while ((c = r.Read()) >= 0)
        {
            if (c == '\n') return sb.Length == 0 ? string.Empty : sb.ToString();
            if (c == '\r')
            {
                if (r.Peek() == '\n') r.Read();
                return sb.Length == 0 ? string.Empty : sb.ToString();
            }
            if (sb.Length >= max) throw Refuse(tooLong);
            sb.Append((char)c);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <param name="maxFields">Bu kadar alan toplanınca durur (başlıkta olmayan fazladan sütunlar hiç ayrılmaz).</param>
    private static List<string> SplitCsv(string line, char sep, int maxFields = int.MaxValue)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (result.Count >= maxFields) return result;
            var ch = line[i];
            if (ch == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQ = !inQ;
            }
            // Boş alan paylaşılan string.Empty (satır başına yüzlerce boş hücre ayrı string ayırmasın).
            else if (ch == sep && !inQ) { result.Add(sb.Length == 0 ? string.Empty : sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    /// <summary>Başlık normalizasyonu: küçük harf + Türkçe katla + alfanumerik dışını at (esnek eşleme).</summary>
    private static string Norm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var lowered = s.Trim().ToLowerInvariant()
            .Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g')
            .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c').Replace('â', 'a').Replace('î', 'i');
        return new string(lowered.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string? Get(Dictionary<string, string> row, params string[] aliases)
    {
        foreach (var a in aliases)
            if (row.TryGetValue(Norm(a), out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return null;
    }

    // ---------- Araç ----------
    public async Task<ImportResult> ImportVehiclesAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        int added = 0, skipped = 0;
        var errors = new List<ImportError>();
        foreach (var r in rows)
        {
            var plate = Get(r, "Plaka", "Plaka No");
            if (string.IsNullOrWhiteSpace(plate)) continue;
            try
            {
                await _vehicles.CreateAsync(new VehicleInput
                {
                    Plaka = plate,
                    Marka = Get(r, "Marka"),
                    // "Tipi" = model adı (Egea); "Model" bazı export'larda YIL'dır → ModelYili'ye gider, Tip'e DEĞİL.
                    Tip = Get(r, "Tip", "Tipi", "Model Tipi", "Araç Model"),
                    Grup = Get(r, "Grup", "Araç Grubu", "Grubu"),
                    Segment = Get(r, "Segment", "Sınıf"),
                    Renk = Get(r, "Renk"),
                    ModelYili = ParseInt(Get(r, "Model Yılı", "Yıl", "Model Yili", "Model")),
                    Yakit = ParseEnum<FuelType>(Get(r, "Yakıt Türü", "Yakıt", "Yakit")) ?? FuelType.Benzin,
                    Vites = ParseEnum<Transmission>(NormalizeTransmission(Get(r, "Vites", "Şanzıman"))),
                    Km = ParseInt(Get(r, "KM", "Kilometre", "Son Km")) ?? 0,
                    SasiNo = Get(r, "Şasi No", "Şase No", "Şase", "Şasi", "Şasi Numarası"),
                    MotorNo = Get(r, "Motor No", "Motor Numarası"),
                    Sube = Get(r, "Şube", "Ofis"),
                    Sipp = Get(r, "SIPP"),
                    HgsNo = Get(r, "HGS", "HGS No", "HGS Etiket"),
                    OgsNo = Get(r, "OGS", "OGS No"),
                    AracSahibi = Get(r, "Araç Sahibi", "Sahibi", "Malik"),
                    Durum = VehicleStatus.Musait
                }, ct);
                added++;
            }
            catch (DuplicatePlakaException) { skipped++; }
            catch (ValidationException ex) { errors.Add(new ImportError(plate, ex.Message)); }
        }
        return Result(added, skipped, errors);
    }

    // ---------- Müşteri (cari) ----------
    public async Task<ImportResult> ImportCustomersAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        int added = 0, skipped = 0;
        var errors = new List<ImportError>();
        foreach (var r in rows)
        {
            var tipStr = Norm(Get(r, "Tip", "Cari Tipi", "Müşteri Tipi", "Cari Türü") ?? "");
            var title = Get(r, "Ünvan", "Unvan", "Firma", "Firma Adı", "Firma Unvanı", "Cari Ünvan", "Ünvan1", "Ünvan 1");
            // "Cari Bilgi" referans sistem müşteri export'unda ad(-ünvan) alanıdır; Soyad ayrı sütunda.
            var name = Get(r, "Ad", "Adı", "İsim", "Cari Bilgi", "Cari", "Cari Adı");
            var soyad = Get(r, "Soyad", "Soyadı");
            var fullName = Get(r, "Ad Soyad", "Adı Soyadı", "Müşteri", "Müşteri Adı", "İsim Soyisim", "Ad-Soyad");
            var taxNoPrefix = Get(r, "Vergi No", "VKN", "Vergi Numarası");
            var nationalIdPrefix = Digits(Get(r, "TC", "TC Kimlik", "TC Kimlik No", "TCKN", "Kimlik No", "T.C. Kimlik", "T.C. No"));

            bool corporate = tipStr.Contains("kurum") || tipStr.Contains("tuzel") || tipStr.Contains("firma")
                || (!string.IsNullOrWhiteSpace(taxNoPrefix) && string.IsNullOrWhiteSpace(soyad) && string.IsNullOrWhiteSpace(nationalIdPrefix))
                || (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(title));

            if (!corporate && string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(fullName))
            {
                var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) name = parts[0];
                else { soyad = parts[^1]; name = string.Join(' ', parts[..^1]); }
            }
            if (corporate && string.IsNullOrWhiteSpace(title)) title = fullName;

            var label = title ?? fullName ?? name ?? "(satır)";
            try
            {
                await _customers.CreateAsync(new CustomerInput
                {
                    Tip = corporate ? CustomerType.Kurumsal : CustomerType.Bireysel,
                    Ad = name, Soyad = soyad, Unvan = title,
                    TcKimlik = nationalIdPrefix,
                    VergiNo = taxNoPrefix,
                    VergiDairesi = Get(r, "Vergi Dairesi"),
                    CepTel = Get(r, "Telefon", "GSM", "Cep", "Cep Tel", "Cep Telefonu", "Gsm No"),
                    Gsm2 = Get(r, "Telefon 2", "GSM 2", "Diğer Telefon", "Tel2", "Tel 2"),
                    Email = Get(r, "Email", "E-posta", "Eposta", "Mail", "E-Mail", "Mail Adresi"),
                    Il = Get(r, "İl", "Şehir"),
                    Ilce = Get(r, "İlçe"),
                    Adres = Get(r, "Adres", "Açık Adres"),
                    EhliyetNo = Get(r, "Ehliyet No", "Ehliyet", "Ehliyet Numarası"),
                    EhliyetSinifi = Get(r, "Ehliyet Sınıfı"),
                    PasaportNo = Get(r, "Pasaport", "Pasaport No"),
                    MusteriTemsilcisi = Get(r, "Müşteri Temsilcisi", "Temsilci"),
                    Kaynak = Get(r, "Kaynak", "Entegrasyon Kodu")
                }, ct);
                added++;
            }
            catch (DuplicateCariException) { skipped++; }
            catch (ValidationException ex) { errors.Add(new ImportError(label, ex.Message)); }
        }
        return Result(added, skipped, errors);
    }

    // ---------- Tarife matrisi (FAZ 6.1 — xml_fiyat_aktar karşılığı) ----------
    /// <summary>Toplu tarife içe-aktarımı. GÜVENLİK ÇİTİ: satırlar DAİMA OnayDurumu=Bekliyor girer —
    /// dosyadaki onay kolonları YOK SAYILIR; onay akışı (Tarife Matrisi ekranı) atlanamaz, toplu import
    /// onaysız fiyatı CANLIYA çıkaramaz (motor yalnız Onaylı seçer). Kod tekrarı (mevcut/dosya-içi)
    /// atlanır; bozuk satır (kodsuz, negatif/sayı-olmayan fiyat, bozuk tarih) satır-hata raporuna düşer,
    /// diğerleri girer (atomik değil — /ice-aktar deseniyle tutarlı).</summary>
    public async Task<ImportResult> ImportTariffsAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        int added = 0, skipped = 0;
        var errors = new List<ImportError>();
        var existingCodes = new HashSet<string>(
            (await _rateMatrices.ListAsync(ct)).Select(m => m.Kod), StringComparer.Ordinal);

        int order = 1;
        foreach (var r in rows)
        {
            order++; // başlık 1. satır → veri 2'den başlar (hata mesajında dosya satırı)
            var rawCode = Get(r, "Kod", "Tarife Kodu", "Kodu");
            var label = rawCode ?? $"(satır {order})";
            try
            {
                if (string.IsNullOrWhiteSpace(rawCode))
                    throw new ValidationException("Tarife kodu zorunludur.");
                var code = rawCode.Trim().ToUpperInvariant();
                if (!existingCodes.Add(code)) { skipped++; continue; } // mevcut VEYA dosya-içi tekrar

                await _rateMatrices.CreateAsync(new RateMatrixInput
                {
                    Kod = code,
                    Ad = Get(r, "Ad", "Adı", "Tarife Adı") ?? code,
                    Aciklama = Get(r, "Açıklama"),
                    Kanal = Get(r, "Kanal"),
                    Sube = Get(r, "Şube"),
                    Lokasyon = Get(r, "Lokasyon", "Ofis"),
                    AracGrupKod = Get(r, "Grup", "Araç Grubu", "Grubu", "Araç Grup Kod"),
                    ParaBirimi = Get(r, "Para Birimi", "Döviz", "ParaBirimi"),
                    BasTar = ParseDate(Get(r, "Başlangıç", "Başlangıç Tarihi", "BasTar", "Geçerlilik Başlangıç")),
                    BitTar = ParseDate(Get(r, "Bitiş", "Bitiş Tarihi", "BitTar", "Geçerlilik Bitiş")),
                    Gun1 = ParseDec(Get(r, "Gün 1", "Gun1")), Gun2 = ParseDec(Get(r, "Gün 2", "Gun2")),
                    Gun3 = ParseDec(Get(r, "Gün 3", "Gun3")), Gun4 = ParseDec(Get(r, "Gün 4", "Gun4")),
                    Gun5 = ParseDec(Get(r, "Gün 5", "Gun5")), Gun6 = ParseDec(Get(r, "Gün 6", "Gun6")),
                    Gun7 = ParseDec(Get(r, "Gün 7", "Gun7")),
                    GunHaftalik = ParseDec(Get(r, "Haftalık", "Gün Haftalık", "Haftalık (8-29)")),
                    GunAylik = ParseDec(Get(r, "Aylık", "Gün Aylık", "Aylık (30+)")),
                    // Onay alanları BİLİNÇLİ sabit (dosyadan okunmaz) — çit yukarıdaki özet.
                    OnayDurumu = TariffApprovalStatus.Bekliyor,
                    Onaylayan = null, OnayZaman = null, Aktif = true
                }, ct);
                added++;
            }
            catch (ValidationException ex)
            {
                existingCodes.Remove(rawCode?.Trim().ToUpperInvariant() ?? "");
                errors.Add(new ImportError(label, ex.Message));
            }
        }
        return Result(added, skipped, errors);
    }

    /// <summary>TR/EN sayı: "1.250,50" ve "1250.50" ikisi de çalışır (son ayraç ondalık; TCMB
    /// InvariantCulture dersi). Boş → null; sayı-olmayan dolu değer → ValidationException (sessiz yutma yok).</summary>
    private static decimal? ParseDec(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().Replace(" ", "");
        int dot = t.LastIndexOf('.'), comma = t.LastIndexOf(',');
        if (dot >= 0 && comma >= 0)
        {
            var thousandsSeparator = dot > comma ? "," : ".";
            t = t.Replace(thousandsSeparator, "");
            t = t.Replace(',', '.');
        }
        else if (comma >= 0) t = t.Replace(',', '.');
        return decimal.TryParse(t, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new ValidationException($"'{s}' sayı olarak okunamadı.");
    }

    /// <summary>Tarih: "2026-01-15" / "15.01.2026" / "15/01/2026" (gün-hassas, UTC). Bozuk dolu değer → hata.</summary>
    private static DateTimeOffset? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        string[] fmt = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy"];
        return DateTime.TryParseExact(s.Trim(), fmt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero)
            : throw new ValidationException($"'{s}' tarih olarak okunamadı (bekleneni: 2026-01-15 veya 15.01.2026).");
    }

    /// <summary>Blazor/tarife metin özeti (etiket: mesaj, ilk 20 + "+N hata daha") VE yapılandırılmış satırlar
    /// (<see cref="ImportResult.Errors"/>; etiket ayrı — kişisel veri taşımayan özet yalnız mesajı kullanır).</summary>
    private static ImportResult Result(int added, int skipped, List<ImportError> errors)
    {
        var lines = errors.Select(e => $"{e.Label}: {e.Message}").ToList();
        IReadOnlyList<string> text = lines.Count <= 20
            ? lines
            : lines.Take(20).Append($"... (+{lines.Count - 20} hata daha)").ToList();
        return new ImportResult(added, skipped, errors.Count, text) { Errors = errors };
    }
    private static string? Digits(string? s) => string.IsNullOrWhiteSpace(s) ? null : new string(s.Where(char.IsDigit).ToArray());
    private static int? ParseInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var d = new string(s.Where(char.IsDigit).ToArray());
        return int.TryParse(d, out var v) ? v : null;
    }
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) ? v : null;

    // Vites serbest-metnini enum'a köprüle (referans sistem "Düz" = Manuel, "Oto" = Otomatik).
    private static string? NormalizeTransmission(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var n = s.Trim().ToLowerInvariant();
        if (n.StartsWith("düz") || n.StartsWith("duz") || n.StartsWith("man")) return "Manuel";
        if (n.StartsWith("oto")) return "Otomatik";
        return s;
    }
}
