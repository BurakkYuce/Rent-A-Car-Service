using System.Globalization;
using System.Text;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Application.Vehicles;

namespace RentACar.Application.Hgs;

/// <summary>
/// HGS dosya hücrelerinin SAF ayrıştırıcıları (DB yok, saat yok — "şimdi" parametre). Hatalı değer
/// <see cref="ValidationException"/> fırlatır; mesaj satır hatası olarak kullanıcıya gider.
/// </summary>
public static class HgsValueParser
{
    /// <summary>Tutar üst sınırı: tek bir geçiş için makul tavan (numeric(19,4) taşmasından çok uzak).</summary>
    public const decimal MaxAmount = 1_000_000m;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Plaka → eşleştirme anahtarı: araç kartındaki TEK kural (<see cref="VehicleService.PlateKey"/>) + tire/nokta
    /// atılır (bazı dökümler "34-ABC-123" yazar). Yalnız harf/rakam, 4–12 karakter.
    /// </summary>
    public static string ParsePlate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new ValidationException("Plaka boş.");
        var key = PlateMatchKey(raw);
        if (key.Length is < 4 or > 12 || !key.All(char.IsAsciiLetterOrDigit))
            throw new ValidationException($"'{raw.Trim()}' geçerli bir plaka değil.");
        return key;
    }

    /// <summary>Araç plakası ile dosya plakasının karşılaştırma anahtarı (iki taraf da bundan geçer).</summary>
    public static string PlateMatchKey(string? plate)
        => VehicleService.PlateKey(plate).Replace("-", string.Empty).Replace(".", string.Empty);

    /// <summary>
    /// TR/EN tutar: "1.250,50", "1250,50", "1250.50", "47,25", "₺47,25", "47,25 TL". Son ayraç ondalıktır.
    /// <para><b>Belirsizlik (tarife içe aktarım dersi, #365):</b> TEK ayraç TEK kez ve ardından TAM 3 hane ("1.250",
    /// "1,250") binlik de ondalık da olabilir — sessizce 1,25 okumak 1000 kat yanlış borç demektir. Reddedilir.</para>
    /// <para>Pozitif olmalı; kuruştan küçük hane (ör. 12,3456 ≠ 12,35) reddedilir — yuvarlayarak tahmin edilmez.</para>
    /// </summary>
    public static decimal ParseAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new ValidationException("Tutar boş.");
        var t = raw.Trim()
            .Replace("₺", string.Empty).Replace("TRY", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("TL", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty).Replace(" ", string.Empty);
        if (t.Length == 0) throw new ValidationException($"'{raw.Trim()}' tutar olarak okunamadı.");
        int dot = t.LastIndexOf('.'), comma = t.LastIndexOf(',');
        var separators = t.Count(ch => ch is '.' or ',');
        if (separators == 1 && t.Length - Math.Max(dot, comma) - 1 == 3)
            throw new ValidationException(
                $"'{raw.Trim()}' belirsiz: binlik ayraç mı ondalık mı anlaşılamıyor. Kuruşu ekleyin (1.250,00) ya da ayraçsız yazın (1250).");
        if (dot >= 0 && comma >= 0)
        {
            // Son ayraç ondalık; diğeri YALNIZ tam kısımda binlik olabilir ve gruplama 3'lü olmalı.
            var decimalSeparator = dot > comma ? '.' : ',';
            var thousands = dot > comma ? ',' : '.';
            var cut = Math.Max(dot, comma);
            var whole = t[..cut];
            if (whole.Contains(decimalSeparator) || !ValidGrouping(whole, thousands))
                throw Malformed(raw);
            t = whole.Replace(thousands.ToString(), string.Empty) + "." + t[(cut + 1)..];
        }
        else if (separators > 1)
        {
            // Tek tür ayraç birden çok kez → yalnız binlik ("1.250.000"); gruplama 3'lü değilse red ("1.25.0").
            var thousands = comma >= 0 ? ',' : '.';
            if (!ValidGrouping(t, thousands)) throw Malformed(raw);
            t = t.Replace(thousands.ToString(), string.Empty);
        }
        else if (comma >= 0) t = t.Replace(',', '.');
        if (!decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Inv, out var v))
            throw new ValidationException($"'{raw.Trim()}' tutar olarak okunamadı.");
        if (v <= 0m) throw new ValidationException("Tutar sıfırdan büyük olmalı.");
        if (v >= MaxAmount) throw new ValidationException($"'{raw.Trim()}' tek bir geçiş için makul sınırın dışında.");
        if (decimal.Round(v, 2) != v) throw new ValidationException($"'{raw.Trim()}' kuruştan küçük hane içeriyor.");
        return decimal.Round(v, 2);
    }

    /// <summary>Binlik gruplama: ilk grup 1–3 rakam, sonrakiler TAM 3 rakam ("1.250.000" geçerli; "1,250,50",
    /// "12,50", "1.2.3" değil — adversarial #391 L2: bozuk gruplama sessizce binlik sayılıyordu).</summary>
    private static bool ValidGrouping(string whole, char separator)
    {
        var groups = whole.TrimStart('-', '+').Split(separator);
        if (groups[0].Length is < 1 or > 3 || !groups[0].All(char.IsAsciiDigit)) return false;
        return groups.Skip(1).All(g => g.Length == 3 && g.All(char.IsAsciiDigit));
    }

    private static ValidationException Malformed(string raw)
        => new($"'{raw.Trim()}' tutar olarak okunamadı (binlik ayraç gruplaması hatalı).");

    private static readonly string[] DateTimeFormats =
    [
        "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm:ss", "d.M.yyyy H:mm",
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm:ss", "d/M/yyyy H:mm",
        "dd-MM-yyyy HH:mm:ss", "dd-MM-yyyy HH:mm",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm",
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fff",
    ];

    private static readonly string[] DateFormats =
        ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"];

    /// <summary>Açık ofsetli ISO ("+03:00"). <c>zzz</c> ofsetin VARLIĞINI şart koşar (ofsetsiz değer buraya düşmez).</summary>
    private static readonly string[] OffsetFormats = ["yyyy-MM-ddTHH:mm:sszzz", "yyyy-MM-ddTHH:mm:ss.fffzzz"];

    /// <summary>"Z" son ekli ISO (UTC) — AssumeUniversal ile okunur (makinenin yerel saatine düşmesin).</summary>
    private static readonly string[] ZuluFormats = ["yyyy-MM-ddTHH:mm:ss'Z'", "yyyy-MM-ddTHH:mm:ss.fff'Z'"];

    private static readonly string[] TimeFormats = ["HH:mm:ss", "H:mm:ss", "HH:mm", "H:mm"];

    /// <summary>
    /// Geçiş anı → UTC. Dosyadaki saatler İSTANBUL duvar saatidir (açık ofsetli ISO değer hariç). Ayrı saat sütunu
    /// verilirse tarih sütunundaki saat 00:00:00 olmalı (Excel tarih hücresi) ya da saat sütunuyla aynı olmalı.
    /// Saat hiç yoksa (yalnız gün) satır reddedilir: kiraya eşleştirme saate bağlıdır, gece yarısı varsaymak yanlış
    /// müşteriyi borçlandırır. Gelecek anı ve 2000 öncesi reddedilir.
    /// </summary>
    public static DateTimeOffset ParseInstant(string? date, string? time, DateTimeOffset nowUtc, string label = "Geçiş zamanı")
    {
        if (string.IsNullOrWhiteSpace(date)) throw new ValidationException($"{label} boş.");
        var d = CollapseSpaces(date);
        DateTimeOffset result;
        if (DateTimeOffset.TryParseExact(d, OffsetFormats, Inv, DateTimeStyles.None, out var withOffset)
            || DateTimeOffset.TryParseExact(d, ZuluFormats, Inv, DateTimeStyles.AssumeUniversal, out withOffset))
        {
            if (!string.IsNullOrWhiteSpace(time))
                throw new ValidationException($"{label}: saat dilimli değerle ayrı saat sütunu birlikte kullanılamaz.");
            result = withOffset.ToUniversalTime();
        }
        else
        {
            DateTime local;
            if (DateTime.TryParseExact(d, DateTimeFormats, Inv, DateTimeStyles.None, out var dt))
            {
                local = dt;
                if (!string.IsNullOrWhiteSpace(time))
                {
                    var tod = ParseTime(time, label);
                    if (dt.TimeOfDay == TimeSpan.Zero) local = dt.Date + tod;
                    else if (dt.TimeOfDay != tod)
                        throw new ValidationException($"{label}: tarih sütunundaki saat ({dt:HH:mm:ss}) ile saat sütunu ({tod:hh\\:mm\\:ss}) farklı.");
                }
            }
            else if (DateTime.TryParseExact(d, DateFormats, Inv, DateTimeStyles.None, out var day))
            {
                if (string.IsNullOrWhiteSpace(time))
                    throw new ValidationException($"{label}: '{d}' saat içermiyor; saat sütununu eşleyin.");
                local = day.Date + ParseTime(time, label);
            }
            else throw new ValidationException($"{label}: '{d}' okunamadı (beklenen: 15.10.2026 08:30 ya da 2026-10-15 08:30:00).");
            result = IstanbulToUtc(local, label);
        }
        result = new DateTimeOffset(result.Ticks - result.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero); // saniye hassasiyeti
        if (result.Year < 2000) throw new ValidationException($"{label} 2000 yılından önce olamaz.");
        if (result > nowUtc.AddMinutes(5)) throw new ValidationException($"{label} gelecekte olamaz.");
        return result;
    }

    /// <summary>İstanbul duvar saati → UTC (2016 öncesi yaz saati dahil; var olmayan an reddedilir).</summary>
    public static DateTimeOffset IstanbulToUtc(DateTime local, string label = "Geçiş zamanı")
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (PeriodLock.Zone.IsInvalidTime(unspecified))
            throw new ValidationException($"{label}: {local:dd.MM.yyyy HH:mm} saat değişiminde var olmayan bir an.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, PeriodLock.Zone), TimeSpan.Zero);
    }

    private static TimeSpan ParseTime(string raw, string label)
    {
        var t = CollapseSpaces(raw);
        if (TimeSpan.TryParseExact(t, ["hh\\:mm\\:ss", "h\\:mm\\:ss", "hh\\:mm", "h\\:mm"], Inv, out var ts)
            && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
            return ts;
        // Excel saat hücresi "1900-01-01 08:30:00" / "08:30:00" biçiminde gelebilir.
        if (DateTime.TryParseExact(t, DateTimeFormats, Inv, DateTimeStyles.None, out var dt)) return dt.TimeOfDay;
        if (DateTime.TryParseExact(t, TimeFormats, Inv, DateTimeStyles.None, out var only)) return only.TimeOfDay;
        throw new ValidationException($"{label}: saat '{t}' okunamadı (beklenen: 08:30 ya da 08:30:15).");
    }

    /// <summary>Serbest metin: kırp, iç boşlukları teke indir, boşsa null, en çok <paramref name="max"/> karakter.</summary>
    public static string? Text(string? raw, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = CollapseSpaces(raw);
        if (t.Length > max) throw new ValidationException($"{label} en çok {max} karakter olabilir.");
        return t;
    }

    /// <summary>
    /// İstasyon karşılaştırma anahtarı: Türkçe harfler ASCII'ye katlanır, büyük harf, tek boşluk. "Çamlıca" ile
    /// "ÇAMLICA" aynı anahtarı üretir (iki döküm aynı gişeyi farklı yazınca mükerrer kaçmasın).
    /// </summary>
    public static string StationKey(string? entry, string? exit)
        => $"{Fold(entry)}>{Fold(exit)}";

    /// <summary>
    /// Olası mükerrer YAKINLIK karşılaştırması için istasyon anahtarı (#407 L2): NFC, Türkçe katlama, yalnız harf ve
    /// rakam ("Kınalı-Gişe" = "KINALI GİŞE", "Avrasya Tüneli." = "AVRASYA TUNELI", ayrık çengelli "Çamlıca" =
    /// "Çamlıca"). Doğal anahtar (<see cref="StationKey"/>, unique index) BUNU KULLANMAZ — o değişmez.
    /// </summary>
    public static string NearKey(string? station)
    {
        if (string.IsNullOrWhiteSpace(station)) return string.Empty;
        return string.Concat(Fold(station.Normalize(NormalizationForm.FormC)).Where(char.IsLetterOrDigit));
    }

    private static string Fold(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in CollapseSpaces(s))
        {
            sb.Append(ch switch
            {
                'ı' or 'i' or 'İ' or 'I' => 'I',
                'ş' or 'Ş' => 'S',
                'ğ' or 'Ğ' => 'G',
                'ü' or 'Ü' => 'U',
                'ö' or 'Ö' => 'O',
                'ç' or 'Ç' => 'C',
                'â' or 'Â' => 'A',
                'î' or 'Î' => 'I',
                'û' or 'Û' => 'U',
                _ => char.ToUpperInvariant(ch),
            });
        }
        return sb.ToString();
    }

    private static string CollapseSpaces(string s)
        => string.Join(' ', s.Replace(' ', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
