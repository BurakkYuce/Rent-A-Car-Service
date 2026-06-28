using RentACar.Domain.Enums;

namespace RentACar.Application.Pricing;

/// <summary>Fiyat motoru v1 teklif girdisi (salt-hesap; defter postalamaz).</summary>
public sealed class QuoteRequest
{
    public string AracGrupKod { get; set; } = string.Empty;
    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    /// <summary>Sürücü yaşı (genç sürücü eşik kontrolü için).</summary>
    public int? SurucuYas { get; set; }
    /// <summary>Tahmini toplam KM (KM aşım hesabı için).</summary>
    public int? TahminiKm { get; set; }
    /// <summary>Seçilen sigorta/ek hizmet ürün kodları.</summary>
    public IReadOnlyList<string> SigortaUrunKodlari { get; set; } = [];
}

/// <summary>Teklif kalemi (sigorta/ek hizmet).</summary>
public sealed record QuoteLine(string Kod, string Ad, CoverageProductType Tur, decimal BirimUcret, int Gun, decimal Tutar);

/// <summary>Fiyat motoru v1 teklif sonucu. Tüm tutarlar 2 ondalık (kuruş) yuvarlı.</summary>
public sealed class QuoteResult
{
    public int Gun { get; init; }
    public int HediyeGun { get; init; }
    public int FaturalananGun { get; init; }
    public decimal GunlukUcret { get; init; }
    public decimal BazTutar { get; init; }
    public decimal KmAsimTutar { get; init; }
    public decimal SigortaToplam { get; init; }
    public decimal AraToplam { get; init; }
    public decimal IskontoOran { get; init; }
    public decimal IskontoTutar { get; init; }
    public decimal GenelToplam { get; init; }
    /// <summary>Teklif para birimi (tarife matrisinden; varsayılan TRY). Tüm tutarlar bu birimdedir.</summary>
    public string ParaBirimi { get; init; } = "TRY";
    /// <summary>Bilgi amaçlı bloke (deftere yazılmaz).</summary>
    public decimal Provizyon { get; init; }
    public decimal Muafiyet { get; init; }
    public bool GencSurucu { get; init; }
    public IReadOnlyList<QuoteLine> SigortaKalemleri { get; init; } = [];
    public IReadOnlyList<string> Notlar { get; init; } = [];
}
