using RentACar.Domain.Enums;

namespace RentACar.Application.GelenEFaturalar;

/// <summary>
/// FAZ-55 gelen e-Fatura liste filtresi (canlı gelen_e_fatura_listesi arama paneli paritesi):
/// EFatura_Firma (ünvan/VKN), Fatura No (ETTN) ARALIĞI, Plaka (bağlı araç), Islem_Turu (durum),
/// tarih aralığı. Boş alan = daraltma yok.
/// </summary>
public sealed class GelenEFaturaFilter
{
    /// <summary>Gönderen firma: ünvan VEYA VKN içinde arar (ILike).</summary>
    public string? Firma { get; set; }
    /// <summary>ETTN aralığı başı (dahil) — sıralı ETTN/fatura no serilerinde aralık taraması.</summary>
    public string? EttnBas { get; set; }
    /// <summary>ETTN aralığı sonu (dahil).</summary>
    public string? EttnBit { get; set; }
    /// <summary>Bağlı aracın plakası (kısmi). Plaka DB'de normalize saklanır → terim de normalize edilir.</summary>
    public string? Plaka { get; set; }
    /// <summary>İşlem türü = triage durumu.</summary>
    public GelenEFaturaDurum? Durum { get; set; }
    /// <summary>Fatura tarihi aralığı başı (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }
    /// <summary>Fatura tarihi aralığı sonu (dahil).</summary>
    public DateTimeOffset? Bit { get; set; }
    /// <summary>Yalnız giderleştirilmişler (true) / giderleştirilmemişler (false).</summary>
    public bool? Giderlestirildi { get; set; }
}
