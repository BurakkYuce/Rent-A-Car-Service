using RentACar.Application.Common;
using RentACar.Application.TenantSettings;

namespace RentACar.Application.Kur;

/// <summary>
/// Ödeme uçları için kur çözümü (araç-karne serisinden taşınan açık iş: dövizli ödeme default kur=1m
/// ile TL maliyeti eksik yazabiliyordu — yalnız InvoiceService kuru otomatik çözüyordu).
/// Sözleşme: açık kur verilirse (>0 guard) ONA saygı duyulur (tarihsel düzeltme senaryosu);
/// verilmezse TRY→1, döviz → <see cref="KurService.GetRateAsync"/> (tenant sabit kur → TCMB;
/// bulunamazsa ValidationException — SESSİZ 1 YOK).
///
/// <para>FAZ-82: tenant <c>KurElleGirisKilitli</c> ayarını AÇARSA açık kur artık kabul edilmez —
/// kur daima tenant sabit kuru/TCMB'den çözülür. Ayar varsayılanı <c>false</c>, yani BUGÜNKÜ
/// davranış aynen korunur. Bkz. <c>TenantSettings.KurElleGirisKilitli</c> (kapsam + bedeli).</para>
/// </summary>
public sealed class KurCozucu(KurService kur, ITenantSettingsRepository ayarlar)
{
    private readonly KurService _kur = kur;
    private readonly ITenantSettingsRepository _ayarlar = ayarlar;

    public async Task<decimal> CozAsync(
        string? doviz, decimal? kur, DateTimeOffset? tarih, CancellationToken ct = default)
    {
        if (kur is { } k)
        {
            // Guard GİRİŞ noktasında (CLAUDE.md "yorumdaki hafifletme bayatlar" dersi): elle kur
            // yolunun TEK kapısı burasıdır — 10+ çağrı sitesi (tahsilat/ödeme/virman/gider/depozito/
            // ceza/MTV/muayene/sigorta/araç satış/dış hizmet) hepsi bu satırdan geçer.
            // Ayar okuması BİLEREK yalnız bu dalda: kur boş gelen (çok daha sık) yol ekstra bir DB
            // sorgusu yapmaz, yani kilit kapalıyken performans profili de bugünküyle aynı kalır.
            if ((await _ayarlar.GetAsync(ct))?.KurElleGirisKilitli == true)
                throw new ValidationException(
                    "Bu firmada kur elle girilemez (Ayarlar → kur elle giriş kilidi). "
                    + "Kur alanını boş bırakın; günün tanımlı/TCMB kuru kullanılacaktır.");
            if (k <= 0m) throw new ValidationException("Kur pozitif olmalıdır.", "kur");
            // F4.4a adversarial HIGH-1: temel para (TRY) işleminde kur TANIM GEREĞİ 1'dir. Açık kur ≠ 1
            // kabul edildiğinde baz tutar şişiyordu (100 TRY @5 → kira Tahsilat 500, cari −500, kasa +500) —
            // Blazor'da döviz seçimi TRY'ye geri alınıp kur alanı dolu bırakıldığında da aynı. Sessizce 1'e
            // düzeltmek yerine gürültülü red: çağıran niyetini (döviz mi, kur mu yanlış) kendisi netleştirsin.
            if (KurService.NormalizeKod(doviz) == "TRY" && k != 1m)
                throw new ValidationException("TRY işlemde kur 1 olmalıdır; kur alanını boş bırakın.", "kur");
            return k;
        }
        return await _kur.GetRateAsync(doviz ?? "TRY", tarih, ct: ct);
    }
}
