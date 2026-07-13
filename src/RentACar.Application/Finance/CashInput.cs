using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>Nakit tahsilat/ödeme giriş modeli (çok-dövizli).</summary>
public sealed class CashInput
{
    public Guid CariId { get; set; }
    public Guid? RentalId { get; set; }
    public decimal Tutar { get; set; }
    public string Doviz { get; set; } = "TRY";
    /// <summary>Boş → otomatik çözüm (TRY=1; döviz KurService). Açık değer aynen kullanılır (1.1b).</summary>
    public decimal? Kur { get; set; }
    public DateTimeOffset? Tarih { get; set; }
    public string? Aciklama { get; set; }
    /// <summary>Para hareketinin geçtiği hesap: Kasa veya Banka.</summary>
    public LedgerAccountType Hesap { get; set; } = LedgerAccountType.Kasa;

    /// <summary>İdempotency anahtarı (adversarial M5): form başına render edilen token; çift-submit
    /// (çift-tık/retry/geri-butonu) aynı anahtarla ikinci kez yazılamaz (kısmi unique index). Null → korumasız.</summary>
    public Guid? IslemAnahtari { get; set; }
}
