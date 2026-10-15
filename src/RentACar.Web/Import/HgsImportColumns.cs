using RentACar.Application.Common;
using RentACar.Application.Hgs;

namespace RentACar.Web.Import;

/// <summary>HGS içe aktarım alanı: <paramref name="Kod"/> JSON/eşleme anahtarı, <paramref name="TakmaAdlar"/> dosya
/// başlıklarının bilinen yazımları (ilk eşleşen önerilir; kullanıcı önizlemede değiştirebilir).</summary>
public sealed record HgsImportField(string Kod, string Etiket, bool Zorunlu, IReadOnlyList<string> TakmaAdlar);

/// <summary>
/// HGS geçiş dökümü sütun eşlemesi. <b>Kesin bir dosya biçimi yayımlanmış değil</b>: PTT HGS kurumsal portalı ve
/// bankaların (eski OGS) internet şubeleri geçiş listesini farklı başlıklarla verir (araştırma notu PR açıklamasında).
/// Bu yüzden: (1) bilinen başlık yazımlarından oluşan takma ad tablosu öneri üretir, (2) kullanıcı önizlemede her
/// alan için dosyadaki sütunu seçer; sunucu yalnız dosyada GERÇEKTEN olan başlığı kabul eder.
/// <para>Başlık karşılaştırması <see cref="ImportService.HeaderKey"/> ile (küçük harf, Türkçe katlı, yalnız harf/rakam):
/// "GİRİŞ İSTASYONU" = "Giriş İstasyonu" = "giris_istasyonu".</para>
/// </summary>
public static class HgsImportColumns
{
    public const string Plate = "plaka";
    public const string Tag = "etiketNo";
    public const string VehicleClass = "aracSinifi";
    public const string EntryStation = "girisIstasyonu";
    public const string ExitStation = "cikisIstasyonu";
    public const string EntryTime = "girisZamani";
    public const string CrossingTime = "gecisZamani";
    public const string CrossingClock = "gecisSaati";
    public const string Amount = "tutar";

    /// <summary>Alan sırası ekranda da bu sıradır. Takma adlar: PTT/e-Devlet/banka ekranlarında görülen başlıklar +
    /// referans sistemin HGS geçiş listesi (Baş./Bit. Zamanı, Giriş/Çıkış Noktası, HGS Etiket, Turu).</summary>
    public static readonly IReadOnlyList<HgsImportField> Fields =
    [
        new(Plate, "Plaka", true, ["Plaka", "Araç Plakası", "Araç Plaka", "Plaka No", "Plate"]),
        new(CrossingTime, "Geçiş (çıkış) zamanı", true,
            ["Geçiş Zamanı", "Geçiş Tarihi", "Geçiş Tarih Saat", "Çıkış Zamanı", "Çıkış Tarihi", "Çıkış Tarih",
             "Çıkış Tarih Saat", "Bit. Zamanı", "Bitiş Zamanı", "İşlem Tarihi", "İşlem Zamanı", "Tarih Saat", "Tarih"]),
        new(CrossingClock, "Geçiş saati (ayrı sütunsa)", false, ["Saat", "Geçiş Saati", "Çıkış Saati", "İşlem Saati"]),
        new(Amount, "Tutar", true,
            ["Tutar", "Geçiş Ücreti", "Geçiş Tutarı", "Ücret", "Tutar (TL)", "Ücret (TL)", "Geçiş Ücreti (TL)",
             "Tahsil Edilen Tutar", "Tahsilat Tutarı", "Bedel"]),
        new(EntryStation, "Giriş istasyonu", false,
            ["Giriş İstasyonu", "Giriş İstasyon", "Giriş Noktası", "Giriş Gişesi", "Giriş Yeri"]),
        new(ExitStation, "Çıkış istasyonu / gişe", false,
            ["Çıkış İstasyonu", "Çıkış İstasyon", "Çıkış Noktası", "Çıkış Gişesi", "Çıkış Yeri", "İstasyon", "Gişe",
             "Geçiş Noktası", "Geçiş Yeri", "Köprü/Otoyol", "Otoyol/Köprü"]),
        new(EntryTime, "Giriş zamanı", false,
            ["Giriş Zamanı", "Giriş Tarihi", "Giriş Tarih Saat", "Baş. Zamanı", "Başlangıç Zamanı"]),
        new(Tag, "Etiket no", false,
            ["Etiket No", "HGS Etiket", "HGS Etiket No", "Etiket", "Etiket Numarası", "HGS No", "OGS No", "Ürün No", "Cihaz No"]),
        new(VehicleClass, "Araç sınıfı", false, ["Araç Sınıfı", "Araç Sınıf", "Sınıf", "Turu", "Türü"]),
    ];

    private static readonly HashSet<string> Codes = Fields.Select(f => f.Kod).ToHashSet(StringComparer.Ordinal);

    /// <summary>Dosya başlıklarından öneri: her alan için ilk takma adı dosyada bulunan başlık (bir başlık iki alana
    /// önerilmez — önce alan sırası).</summary>
    public static Dictionary<string, string?> Suggest(IReadOnlyList<string> headers)
    {
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var h in headers)
        {
            var k = ImportService.HeaderKey(h);
            if (k.Length > 0) byKey.TryAdd(k, h);
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var f in Fields)
        {
            string? hit = null;
            foreach (var alias in f.TakmaAdlar)
            {
                var k = ImportService.HeaderKey(alias);
                if (byKey.TryGetValue(k, out var header) && !used.Contains(k))
                {
                    hit = header;
                    used.Add(k);
                    break;
                }
            }
            result[f.Kod] = hit;
        }
        return result;
    }

    /// <summary>
    /// Son eşleme: kullanıcı eşleme verdiyse O (yalnız bilinen alan kodları, değer dosyada OLAN bir başlık, aynı başlık
    /// iki alana verilemez); vermediyse öneri. Zorunlu alan eşlenmemişse <c>errors[eslesme]</c>. Dönen: alan → başlık
    /// ANAHTARI (satır sözlüğündeki anahtar).
    /// </summary>
    public static (Dictionary<string, string?> Labels, Dictionary<string, string> Keys) Resolve(
        IReadOnlyList<string> headers, IReadOnlyDictionary<string, string?>? userMapping)
    {
        var labels = userMapping is null ? Suggest(headers) : Validate(headers, userMapping);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, header) in labels)
            if (header is not null) keys[field] = ImportService.HeaderKey(header);
        return (labels, keys);
    }

    private static Dictionary<string, string?> Validate(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string?> mapping)
    {
        var known = headers.Select(ImportService.HeaderKey).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        var result = Fields.ToDictionary(f => f.Kod, _ => (string?)null, StringComparer.Ordinal);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, raw) in mapping)
        {
            if (!Codes.Contains(field)) throw new ValidationException($"Bilinmeyen eşleme alanı: '{field}'.", "eslesme");
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var header = raw.Trim();
            var key = ImportService.HeaderKey(header);
            if (!known.Contains(key)) throw new ValidationException($"'{header}' sütunu dosyada yok.", "eslesme");
            if (used.TryGetValue(key, out var other))
                throw new ValidationException($"'{header}' sütunu iki alana birden eşlenemez ({other}, {field}).", "eslesme");
            used[key] = field;
            result[field] = header;
        }
        return result;
    }

    /// <summary>Zorunlu alanlar eşlenmiş mi? Eksikse <c>errors[eslesme]</c>.</summary>
    public static void RequireMandatory(IReadOnlyDictionary<string, string> keys)
    {
        var missing = Fields.Where(f => f.Zorunlu && !keys.ContainsKey(f.Kod)).Select(f => f.Etiket).ToList();
        if (missing.Count > 0)
            throw new ValidationException($"Şu alanlar için sütun seçin: {string.Join(", ", missing)}.", "eslesme");
    }

    /// <summary>Satır sözlükleri → ham HGS satırları (değer dönüşümü YOK — servisin işi). Satır no = veri sırası + 1
    /// (başlık 1. satır).</summary>
    public static IReadOnlyList<HgsRawRow> Extract(IReadOnlyList<Dictionary<string, string>> rows, IReadOnlyDictionary<string, string> keys)
    {
        string? Get(Dictionary<string, string> r, string field)
            => keys.TryGetValue(field, out var k) && r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        var list = new List<HgsRawRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            list.Add(new HgsRawRow(i + 2)
            {
                Plaka = Get(r, Plate), EtiketNo = Get(r, Tag), AracSinifi = Get(r, VehicleClass),
                GirisIstasyonu = Get(r, EntryStation), CikisIstasyonu = Get(r, ExitStation),
                GirisZamani = Get(r, EntryTime), GecisZamani = Get(r, CrossingTime), GecisSaati = Get(r, CrossingClock),
                Tutar = Get(r, Amount),
            });
        }
        return list;
    }
}
