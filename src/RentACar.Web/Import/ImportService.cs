using ClosedXML.Excel;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;

namespace RentACar.Web.Import;

/// <summary>İçe-aktarım sonucu: eklenen / atlanan (tekrar) / hatalı satır + ilk hataların özeti.</summary>
public sealed record ImportResult(int Eklenen, int Atlanan, int Hatali, IReadOnlyList<string> Hatalar);

/// <summary>
/// Veri göçü / onboarding: TürevRent (veya benzeri) Excel/CSV export'undan araç + müşteri içe-aktarımı.
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
    public static IReadOnlyList<Dictionary<string, string>> Parse(Stream stream, string fileName)
    {
        var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                   || fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);
        return isExcel ? ParseExcel(stream) : ParseCsv(stream);
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseExcel(Stream s)
    {
        var rows = new List<Dictionary<string, string>>();
        using var wb = new XLWorkbook(s);
        var ws = wb.Worksheets.FirstOrDefault();
        var used = ws?.RangeUsed();
        if (used is null) return rows;
        var headers = used.FirstRow().Cells().Select(c => Norm(c.GetString())).ToList();
        foreach (var row in used.RowsUsed().Skip(1))
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < headers.Count; i++)
            {
                if (string.IsNullOrEmpty(headers[i])) continue;
                dict[headers[i]] = row.Cell(i + 1).GetString().Trim();
            }
            if (dict.Values.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add(dict);
        }
        return rows;
    }

    private static IReadOnlyList<Dictionary<string, string>> ParseCsv(Stream s)
    {
        var rows = new List<Dictionary<string, string>>();
        using var reader = new StreamReader(s);
        var headerLine = reader.ReadLine();
        if (headerLine is null) return rows;
        var sep = headerLine.Contains(';') && !headerLine.Contains(',') ? ';' : (headerLine.Contains(';') ? ';' : ',');
        var headers = SplitCsv(headerLine, sep).Select(Norm).ToList();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var vals = SplitCsv(line, sep);
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < headers.Count; i++)
            {
                if (string.IsNullOrEmpty(headers[i])) continue;
                dict[headers[i]] = i < vals.Count ? vals[i].Trim() : "";
            }
            rows.Add(dict);
        }
        return rows;
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if (ch == sep && !inQ) { result.Add(sb.ToString()); sb.Clear(); }
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
    public async Task<ImportResult> ImportAraclarAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        int eklenen = 0, atlanan = 0;
        var hatalar = new List<string>();
        foreach (var r in rows)
        {
            var plaka = Get(r, "Plaka", "Plaka No");
            if (string.IsNullOrWhiteSpace(plaka)) continue;
            try
            {
                await _vehicles.CreateAsync(new VehicleInput
                {
                    Plaka = plaka,
                    Marka = Get(r, "Marka"),
                    // "Tipi" = model adı (Egea); "Model" bazı export'larda YIL'dır → ModelYili'ye gider, Tip'e DEĞİL.
                    Tip = Get(r, "Tip", "Tipi", "Model Tipi", "Araç Model"),
                    Grup = Get(r, "Grup", "Araç Grubu", "Grubu"),
                    Segment = Get(r, "Segment", "Sınıf"),
                    Renk = Get(r, "Renk"),
                    ModelYili = ParseInt(Get(r, "Model Yılı", "Yıl", "Model Yili", "Model")),
                    Yakit = ParseEnum<FuelType>(Get(r, "Yakıt Türü", "Yakıt", "Yakit")) ?? FuelType.Benzin,
                    Vites = ParseEnum<Vites>(VitesNorm(Get(r, "Vites", "Şanzıman"))),
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
                eklenen++;
            }
            catch (DuplicatePlakaException) { atlanan++; }
            catch (ValidationException ex) { hatalar.Add($"{plaka}: {ex.Message}"); }
        }
        return new ImportResult(eklenen, atlanan, hatalar.Count, Trunc(hatalar));
    }

    // ---------- Müşteri (cari) ----------
    public async Task<ImportResult> ImportCarilerAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        int eklenen = 0, atlanan = 0;
        var hatalar = new List<string>();
        foreach (var r in rows)
        {
            var tipStr = Norm(Get(r, "Tip", "Cari Tipi", "Müşteri Tipi", "Cari Türü") ?? "");
            var unvan = Get(r, "Ünvan", "Unvan", "Firma", "Firma Adı", "Firma Unvanı", "Cari Ünvan", "Ünvan1", "Ünvan 1");
            // "Cari Bilgi" TürevRent müşteri export'unda ad(-ünvan) alanıdır; Soyad ayrı sütunda.
            var ad = Get(r, "Ad", "Adı", "İsim", "Cari Bilgi", "Cari", "Cari Adı");
            var soyad = Get(r, "Soyad", "Soyadı");
            var adSoyad = Get(r, "Ad Soyad", "Adı Soyadı", "Müşteri", "Müşteri Adı", "İsim Soyisim", "Ad-Soyad");
            var vergiNoOn = Get(r, "Vergi No", "VKN", "Vergi Numarası");
            var tcOn = Digits(Get(r, "TC", "TC Kimlik", "TC Kimlik No", "TCKN", "Kimlik No", "T.C. Kimlik", "T.C. No"));

            bool kurumsal = tipStr.Contains("kurum") || tipStr.Contains("tuzel") || tipStr.Contains("firma")
                || (!string.IsNullOrWhiteSpace(vergiNoOn) && string.IsNullOrWhiteSpace(soyad) && string.IsNullOrWhiteSpace(tcOn))
                || (string.IsNullOrWhiteSpace(ad) && string.IsNullOrWhiteSpace(adSoyad) && !string.IsNullOrWhiteSpace(unvan));

            if (!kurumsal && string.IsNullOrWhiteSpace(ad) && !string.IsNullOrWhiteSpace(adSoyad))
            {
                var parts = adSoyad.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) ad = parts[0];
                else { soyad = parts[^1]; ad = string.Join(' ', parts[..^1]); }
            }
            if (kurumsal && string.IsNullOrWhiteSpace(unvan)) unvan = adSoyad;

            var etiket = unvan ?? adSoyad ?? ad ?? "(satır)";
            try
            {
                await _customers.CreateAsync(new CustomerInput
                {
                    Tip = kurumsal ? CariType.Kurumsal : CariType.Bireysel,
                    Ad = ad, Soyad = soyad, Unvan = unvan,
                    TcKimlik = tcOn,
                    VergiNo = vergiNoOn,
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
                eklenen++;
            }
            catch (DuplicateCariException) { atlanan++; }
            catch (ValidationException ex) { hatalar.Add($"{etiket}: {ex.Message}"); }
        }
        return new ImportResult(eklenen, atlanan, hatalar.Count, Trunc(hatalar));
    }

    // ---------- Tarife matrisi (FAZ 6.1 — xml_fiyat_aktar karşılığı) ----------
    /// <summary>Toplu tarife içe-aktarımı. GÜVENLİK ÇİTİ: satırlar DAİMA OnayDurumu=Bekliyor girer —
    /// dosyadaki onay kolonları YOK SAYILIR; onay akışı (Tarife Matrisi ekranı) atlanamaz, toplu import
    /// onaysız fiyatı CANLIYA çıkaramaz (motor yalnız Onaylı seçer). Kod tekrarı (mevcut/dosya-içi)
    /// atlanır; bozuk satır (kodsuz, negatif/sayı-olmayan fiyat, bozuk tarih) satır-hata raporuna düşer,
    /// diğerleri girer (atomik değil — /ice-aktar deseniyle tutarlı).</summary>
    public async Task<ImportResult> ImportTarifelerAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        int eklenen = 0, atlanan = 0;
        var hatalar = new List<string>();
        var mevcutKodlar = new HashSet<string>(
            (await _rateMatrices.ListAsync(ct)).Select(m => m.Kod), StringComparer.Ordinal);

        int sira = 1;
        foreach (var r in rows)
        {
            sira++; // başlık 1. satır → veri 2'den başlar (hata mesajında dosya satırı)
            var kodHam = Get(r, "Kod", "Tarife Kodu", "Kodu");
            var etiket = kodHam ?? $"(satır {sira})";
            try
            {
                if (string.IsNullOrWhiteSpace(kodHam))
                    throw new ValidationException("Tarife kodu zorunludur.");
                var kod = kodHam.Trim().ToUpperInvariant();
                if (!mevcutKodlar.Add(kod)) { atlanan++; continue; } // mevcut VEYA dosya-içi tekrar

                await _rateMatrices.CreateAsync(new RateMatrixInput
                {
                    Kod = kod,
                    Ad = Get(r, "Ad", "Adı", "Tarife Adı") ?? kod,
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
                    OnayDurumu = TarifeOnayDurumu.Bekliyor,
                    Onaylayan = null, OnayZaman = null, Aktif = true
                }, ct);
                eklenen++;
            }
            catch (ValidationException ex)
            {
                mevcutKodlar.Remove(kodHam?.Trim().ToUpperInvariant() ?? "");
                hatalar.Add($"{etiket}: {ex.Message}");
            }
        }
        return new ImportResult(eklenen, atlanan, hatalar.Count, Trunc(hatalar));
    }

    /// <summary>TR/EN sayı: "1.250,50" ve "1250.50" ikisi de çalışır (son ayraç ondalık; TCMB
    /// InvariantCulture dersi). Boş → null; sayı-olmayan dolu değer → ValidationException (sessiz yutma yok).</summary>
    private static decimal? ParseDec(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim().Replace(" ", "");
        int nokta = t.LastIndexOf('.'), virgul = t.LastIndexOf(',');
        if (nokta >= 0 && virgul >= 0)
        {
            var binlik = nokta > virgul ? "," : ".";
            t = t.Replace(binlik, "");
            t = t.Replace(',', '.');
        }
        else if (virgul >= 0) t = t.Replace(',', '.');
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

    private static IReadOnlyList<string> Trunc(List<string> h)
        => h.Count <= 20 ? h : h.Take(20).Append($"... (+{h.Count - 20} hata daha)").ToList();
    private static string? Digits(string? s) => string.IsNullOrWhiteSpace(s) ? null : new string(s.Where(char.IsDigit).ToArray());
    private static int? ParseInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var d = new string(s.Where(char.IsDigit).ToArray());
        return int.TryParse(d, out var v) ? v : null;
    }
    private static T? ParseEnum<T>(string? s) where T : struct, Enum
        => Enum.TryParse<T>(s, true, out var v) && Enum.IsDefined(v) ? v : null;

    // Vites serbest-metnini enum'a köprüle (TürevRent "Düz" = Manuel, "Oto" = Otomatik).
    private static string? VitesNorm(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        var n = s.Trim().ToLowerInvariant();
        if (n.StartsWith("düz") || n.StartsWith("duz") || n.StartsWith("man")) return "Manuel";
        if (n.StartsWith("oto")) return "Otomatik";
        return s;
    }
}
