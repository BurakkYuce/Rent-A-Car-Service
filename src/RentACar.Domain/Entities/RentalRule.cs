using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kiralama kuralı / promosyon-kampanya (canlı TürevRent kiralama_kurallari + kiralama_sartlari +
/// rezsartlar karşılığı): tarih/gün/min-max gün bazlı indirim-promosyon kuralı + kira/rezervasyon şart
/// metni. Şube + kanal (rez kaynağı) + araç grubu kapsamına uygulanır. Tenant-owned + auditable. Saf
/// kural-tanım — deftere kayıt POSTLAMAZ; fiyat motorunda (parite #7) indirim/min-gün girdisidir.
/// <see cref="Kod"/> tenant içinde benzersiz.
/// </summary>
public class RentalRule : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kural kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    // Kapsam
    /// <summary>Kanal = Rez Kaynağı (boş → tümü).</summary>
    public string? Kanal { get; set; }
    public string? Sube { get; set; }
    public string? AracGrupKod { get; set; }

    // Gün kısıtları
    public int? MinGun { get; set; }
    public int? MaxGun { get; set; }

    // İndirim / promosyon
    /// <summary>İskonto oranı (%).</summary>
    public decimal? Iskonto { get; set; }
    /// <summary>"Sonra Öde" oranı (%).</summary>
    public decimal? SonraOdeOran { get; set; }
    /// <summary>Hediye gün (kampanya: N gün al, M gün öde).</summary>
    public int? HediyeGun { get; set; }
    public bool KampanyaMi { get; set; }
    public string? KampanyaKodu { get; set; }

    // Geçerlilik
    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }

    /// <summary>Kira/rezervasyon şart metni (sözleşme/rez şartları — rezsartlar).</summary>
    public string? SartMetni { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
