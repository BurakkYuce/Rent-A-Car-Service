using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tarife matrisi (canlı TürevRent "XML Tarife" / tarifeler_xml karşılığı): asıl günlük kira fiyat
/// listesi. Kanal (Rez Kaynağı) + şube + lokasyon + araç grubu bazında, geçerlilik tarihli,
/// onay iş akışlı, gün-kademesi (1..7) başına günlük fiyat matrisi + dinamik indirim oranı.
/// Tenant-owned + auditable. Saf fiyat-tanım tablosu — deftere kayıt POSTLAMAZ; fiyat motorunun
/// (parite #7) birincil girdisidir. <see cref="Kod"/> tenant içinde benzersiz (matris satırı kimliği).
/// </summary>
public class RateMatrix : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Matris satırı kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    /// <summary>Tarife adı.</summary>
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    // Kapsam (hangi kanal/şube/lokasyon/grup için)
    /// <summary>Kanal = Rez Kaynağı (ör. WEB, ACENTA, ÇAĞRI).</summary>
    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public Guid? SubeId { get; set; } // Branch FK (roadmap F1; metin korunur)
    public string? Lokasyon { get; set; }
    /// <summary>Araç grubu kodu (RateMatrix grup bazlıdır; VehicleGroup.Kod'a serbest metin referans).</summary>
    public string? AracGrupKod { get; set; }
    /// <summary>Para birimi (ör. TRY, EUR). Boş → tenant varsayılanı.</summary>
    public string? ParaBirimi { get; set; }

    // Geçerlilik
    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }

    // Gün-kademesi başına günlük fiyat (Gün 1..7)
    public decimal? Gun1 { get; set; }
    public decimal? Gun2 { get; set; }
    public decimal? Gun3 { get; set; }
    public decimal? Gun4 { get; set; }
    public decimal? Gun5 { get; set; }
    public decimal? Gun6 { get; set; }
    public decimal? Gun7 { get; set; }

    /// <summary>Karşılaştırma sistemi (rakip) dinamik indirim oranı % (canlı Max_Esneklik).</summary>
    public decimal? MaxEsneklik { get; set; }

    // Onay iş akışı
    public TarifeOnayDurumu OnayDurumu { get; set; } = TarifeOnayDurumu.Bekliyor;
    public string? Onaylayan { get; set; }
    public DateTimeOffset? OnayZaman { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
