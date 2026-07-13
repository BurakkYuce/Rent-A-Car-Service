using RentACar.Application.Common;

namespace RentACar.Application.Kur;

/// <summary>
/// Ödeme uçları için kur çözümü (araç-karne serisinden taşınan açık iş: dövizli ödeme default kur=1m
/// ile TL maliyeti eksik yazabiliyordu — yalnız InvoiceService kuru otomatik çözüyordu).
/// Sözleşme: açık kur verilirse (>0 guard) ONA saygı duyulur (tarihsel düzeltme senaryosu);
/// verilmezse TRY→1, döviz → <see cref="KurService.GetRateAsync"/> (tenant sabit kur → TCMB;
/// bulunamazsa ValidationException — SESSİZ 1 YOK).
/// </summary>
public sealed class KurCozucu(KurService kur)
{
    private readonly KurService _kur = kur;

    public async Task<decimal> CozAsync(
        string? doviz, decimal? kur, DateTimeOffset? tarih, CancellationToken ct = default)
    {
        if (kur is { } k)
        {
            if (k <= 0m) throw new ValidationException("Kur pozitif olmalıdır.");
            return k;
        }
        return await _kur.GetRateAsync(doviz ?? "TRY", tarih, ct: ct);
    }
}
