using RentACar.Application.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Kur;

/// <summary>
/// Döviz kuru çözümleme + çevirim. GetRate ÇÖZÜM SIRASI: (1) tenant aktif SabitKur (bu kod + tarih
/// pencerede) → onun kuru; (2) yoksa TCMB (KurKaydi). TL/TRY → 1. Kur = 1 birim dövizin TL değeri
/// (KurKaydi.Birim'e bölünür — JPY=100 vb.). Eksik/geçersiz kur → ValidationException (sessiz 0/1 DÖNMEZ).
/// Bu servis yalnız KUR sağlar; işlemin kurunu Money.Rate'e YAZMAK tüketicinin işi (tarihsel immutability).
/// </summary>
public sealed class KurService(IKurRepository kurRepo, ISabitKurRepository sabitRepo)
{
    private readonly IKurRepository _kur = kurRepo;
    private readonly ISabitKurRepository _sabit = sabitRepo;

    /// <summary>Serbest döviz etiketini ISO koda indirger (EURO→EUR, TL→TRY…). Boş → TRY (baz).</summary>
    public static string NormalizeKod(string? kod)
    {
        var k = (kod ?? "").Trim().ToUpperInvariant();
        return k switch
        {
            "" or "TL" or "TRY" or "TRL" or "TÜRK LİRASI" or "TURK LIRASI" or "₺" => "TRY",
            "EUR" or "EURO" or "AVRO" or "€" => "EUR",
            "USD" or "DOLAR" or "ABD DOLARI" or "$" => "USD",
            "GBP" or "STERLIN" or "STERLİN" or "£" => "GBP",
            _ => k
        };
    }

    /// <summary>1 birim <paramref name="kod"/> dövizinin TL karşılığı.</summary>
    public async Task<decimal> GetRateAsync(string kod, DateTimeOffset? tarih = null, KurTuru tur = KurTuru.Satis, CancellationToken ct = default)
    {
        var k = NormalizeKod(kod);
        if (k == "TRY") return 1m; // baz para
        var t = tarih ?? DateTimeOffset.UtcNow;

        // (1) tenant sabit kur (aktif + pencere içinde)
        var sabit = await _sabit.GetActiveAsync(k, t, ct);
        if (sabit is not null)
        {
            if (sabit.Kur <= 0) throw new ValidationException($"'{k}' sabit kuru geçersiz (≤0).");
            return sabit.Kur;
        }

        // (2) TCMB (≤tarih en yeni)
        var kayit = await _kur.GetAsync(k, t, ct)
            ?? throw new ValidationException($"'{k}' için TCMB kuru bulunamadı.");
        var deger = tur switch
        {
            KurTuru.Alis => kayit.ForexAlis ?? kayit.EfektifAlis,
            KurTuru.Satis => kayit.ForexSatis ?? kayit.EfektifSatis,
            KurTuru.EfektifAlis => kayit.EfektifAlis,
            KurTuru.EfektifSatis => kayit.EfektifSatis,
            _ => kayit.ForexSatis
        };
        if (deger is not > 0) throw new ValidationException($"'{k}' {tur} kuru yok/geçersiz.");
        var birim = kayit.Birim <= 0 ? 1 : kayit.Birim;
        return deger.Value / birim;
    }

    /// <summary><paramref name="tutar"/> tutarını <paramref name="from"/>→<paramref name="to"/> çevirir (TL bazı üzerinden).</summary>
    public async Task<decimal> CevirAsync(decimal tutar, string from, string to, DateTimeOffset? tarih = null, KurTuru tur = KurTuru.Satis, CancellationToken ct = default)
    {
        var f = NormalizeKod(from);
        var t = NormalizeKod(to);
        if (f == t) return tutar;
        var rFrom = await GetRateAsync(f, tarih, tur, ct); // TL / 1 from
        var rTo = await GetRateAsync(t, tarih, tur, ct);   // TL / 1 to  (>0 garantili)
        return tutar * rFrom / rTo;
    }

    /// <summary>En yeni günün TCMB kurları (görüntüleme). Kayıt yoksa boş.</summary>
    public async Task<IReadOnlyList<KurKaydi>> BugunKurlarAsync(CancellationToken ct = default)
    {
        var tarih = await _kur.EnYeniTarihAsync(ct);
        return tarih is null ? [] : await _kur.ListByDateAsync(tarih.Value, ct);
    }
}
