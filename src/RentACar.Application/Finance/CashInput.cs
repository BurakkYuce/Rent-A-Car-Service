using RentACar.Domain.Entities;
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

    /// <summary>FAZ-50 — hangi SPESİFİK kasa/banka hesabı (<c>FinancialAccount</c>). Boş → eski
    /// davranış (defterde <c>AccountRef=null</c>, "hesap belirtilmemiş" kovası).</summary>
    public Guid? HesapId { get; set; }

    /// <summary>İdempotency anahtarı (adversarial M5): form başına render edilen token; çift-submit
    /// (çift-tık/retry/geri-butonu) aynı anahtarla ikinci kez yazılamaz (kısmi unique index). Null → korumasız.</summary>
    public Guid? IslemAnahtari { get; set; }

    /// <summary>FAZ-84 — tahsilatın/ödemenin yapıldığı kanal ("Masaüstü"/"Mobil"/"Tablet" —
    /// <see cref="RentACar.Domain.Entities.CashKanal"/>). Boş → "Masaüstü". SAF BİLGİ: defter
    /// şemasına/dengesine girmez, yalnız <see cref="RentACar.Domain.Entities.CashTransaction"/> belgesine yazılır.</summary>
    public string? Kanal { get; set; }
}


/// <summary>
/// FAZ-67 — nakit işlem (tahsilat/ödeme) listesi süzgeci. Hepsi opsiyonel; hiçbiri verilmezse
/// davranış bu fazdan öncekiyle AYNI (tüm işlemler, tarihe göre azalan).
/// </summary>
public sealed class CashFilter
{
    /// <summary>İşlem no / cari adı / özel kod içinde arama.</summary>
    public string? Ara { get; set; }
    public CashTransactionType? Tip { get; set; }
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Kasa/Banka türü.</summary>
    public LedgerAccountType? Hesap { get; set; }
    /// <summary>Spesifik kasa/banka hesabı (FAZ-50). <see cref="Guid.Empty"/> = hesap belirtilmemiş.</summary>
    public Guid? HesapId { get; set; }
    /// <summary>Tahsilat kanalı (FAZ-84).</summary>
    public string? Kanal { get; set; }
    public int EnFazla { get; set; } = 500;
}

/// <summary>FAZ-67 — nakit işlem listesi satırı: belge + cari adı/özel kodu çözümlenmiş.</summary>
public sealed record NakitIslemSatirDto(CashTransaction Islem, string CariAd, string? CariKod);
