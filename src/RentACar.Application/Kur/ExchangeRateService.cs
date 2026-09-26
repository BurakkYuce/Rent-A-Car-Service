using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Kur;

/// <summary>
/// Döviz kuru çözümleme + çevirim. GetRate ÇÖZÜM SIRASI: (1) tenant aktif SabitKur (bu kod + tarih
/// pencerede) → onun kuru; (2) yoksa TCMB (KurKaydi). TL/TRY → 1. Kur = 1 birim dövizin TL değeri
/// (KurKaydi.Birim'e bölünür — JPY=100 vb.). Eksik/geçersiz kur → ValidationException (sessiz 0/1 DÖNMEZ).
/// Bu servis yalnız KUR sağlar; işlemin kurunu Money.Rate'e YAZMAK tüketicinin işi (tarihsel immutability).
/// </summary>
public sealed class ExchangeRateService(IExchangeRateRepository exchangeRateRepo, IPinnedRateRepository fixedRepo)
{
    private readonly IExchangeRateRepository _rate = exchangeRateRepo;
    private readonly IPinnedRateRepository _pinned = fixedRepo;

    /// <summary>Kullanıcı girdisi dövizi SAKLANABİLİR ISO koda çevirir (kolonlar varchar(3)). Tanınmayan/uzun
    /// etiket ("KRONER") → ValidationException — aksi hâlde 22001 truncation ile işlenmemiş 500 olurdu (denetim M1:
    /// FX kirada zorunlu döviz-tahsilat yolu form etiketi "EURO"/"DOLAR" ile buraya düşer).</summary>
    public static string NormalizeCodeStrict(string? code)
    {
        var k = NormalizeCode(code);
        if (k.Length != 3) throw new ValidationException($"Geçersiz döviz kodu: '{code}'.");
        return k;
    }

    /// <summary>Serbest döviz etiketini ISO koda indirger (EURO→EUR, TL→TRY…). Boş → TRY (baz).</summary>
    public static string NormalizeCode(string? code)
    {
        var k = (code ?? "").Trim().ToUpperInvariant();
        return k switch
        {
            "" or "TL" or "TRY" or "TRL" or "TÜRK LİRASI" or "TURK LIRASI" or "₺" => "TRY",
            "EUR" or "EURO" or "AVRO" or "€" => "EUR",
            "USD" or "DOLAR" or "ABD DOLARI" or "$" => "USD",
            "GBP" or "STERLIN" or "STERLİN" or "£" => "GBP",
            _ => k
        };
    }

    /// <summary>1 birim <paramref name="code"/> dövizinin TL karşılığı.</summary>
    public async Task<decimal> GetRateAsync(string code, DateTimeOffset? date = null, ExchangeRateType type = ExchangeRateType.Satis, CancellationToken ct = default)
    {
        var k = NormalizeCode(code);
        if (k == "TRY") return 1m; // baz para
        var t = date ?? DateTimeOffset.UtcNow;

        // (1) tenant sabit kur (aktif + pencere içinde)
        var fixedValue = await _pinned.GetActiveAsync(k, t, ct);
        if (fixedValue is not null)
        {
            if (fixedValue.Kur <= 0) throw new ValidationException($"'{k}' sabit kuru geçersiz (≤0).");
            return fixedValue.Kur;
        }

        // (2) TCMB (≤tarih en yeni)
        var record = await _rate.GetAsync(k, t, ct)
            ?? throw new ValidationException($"'{k}' için TCMB kuru bulunamadı.");
        var value = type switch
        {
            ExchangeRateType.Alis => record.ForexAlis ?? record.EfektifAlis,
            ExchangeRateType.Satis => record.ForexSatis ?? record.EfektifSatis,
            ExchangeRateType.EfektifAlis => record.EfektifAlis,
            ExchangeRateType.EfektifSatis => record.EfektifSatis,
            _ => record.ForexSatis
        };
        if (value is not > 0) throw new ValidationException($"'{k}' {type} kuru yok/geçersiz.");
        var unit = record.Birim <= 0 ? 1 : record.Birim;
        return value.Value / unit;
    }

    /// <summary><paramref name="amount"/> tutarını <paramref name="from"/>→<paramref name="to"/> çevirir (TL bazı
    /// üzerinden). YUVARLAMAZ — kuruş-altı hane dönebilir; parasal kayda yazan TÜKETİCİ konvansiyona göre yuvarlar
    /// (Math.Round(x, 2, AwayFromZero) — KdvMath ile aynı). Görüntüleme ToString("N2") ile zaten yuvarlar.</summary>
    public async Task<decimal> ConvertAsync(decimal amount, string from, string to, DateTimeOffset? date = null, ExchangeRateType type = ExchangeRateType.Satis, CancellationToken ct = default)
    {
        var f = NormalizeCode(from);
        var t = NormalizeCode(to);
        if (f == t) return amount;
        var rFrom = await GetRateAsync(f, date, type, ct); // TL / 1 from
        var rTo = await GetRateAsync(t, date, type, ct);   // TL / 1 to  (>0 garantili)
        return amount * rFrom / rTo;
    }

    /// <summary>En yeni günün TCMB kurları (görüntüleme). Kayıt yoksa boş.</summary>
    public async Task<IReadOnlyList<KurKaydi>> TodayRatesAsync(CancellationToken ct = default)
    {
        var date = await _rate.LatestDateAsync(ct);
        return date is null ? [] : await _rate.ListByDateAsync(date.Value, ct);
    }
}
