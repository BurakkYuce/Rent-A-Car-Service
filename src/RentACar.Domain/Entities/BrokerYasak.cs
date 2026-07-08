using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Broker/kaynak satış yasağı-kısıtı (canlı TürevRent broker_yasaklari karşılığı): belirli rez kaynağı
/// (broker/kanal) × araç grubu × bölge × tarih kapsamında kiralama/satış KISITI. İki kısıt türü:
/// <see cref="MinGun"/> altı gün yasak, ve/veya <see cref="TumSatisKapali"/> (kapsamda tüm satış kapalı).
/// Tenant-owned + auditable. Saf kural-tanım — deftere kayıt POSTLAMAZ; rezervasyon/kira akışında
/// (ileride) kapsam-eşleşince engel girdisidir. <see cref="Kod"/> tenant içinde benzersiz.
/// </summary>
public class BrokerYasak : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Yasak kodu (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }

    // Kapsam (boş alan → o boyutta "tümü")
    /// <summary>Kaynak = broker/kanal (rez kaynağı). Boş → tüm kaynaklar.</summary>
    public string? Kaynak { get; set; }
    /// <summary>Araç grubu kodu. Boş → tüm gruplar.</summary>
    public string? AracGrupKod { get; set; }
    /// <summary>Bölge / şehir. Boş → tüm bölgeler.</summary>
    public string? Bolge { get; set; }

    // Kısıt
    /// <summary>Bu kapsamda izin verilen asgari kiralama günü; ALTINDA satış yasak. Boş → gün kısıtı yok.</summary>
    public int? MinGun { get; set; }
    /// <summary>true → kapsamda TÜM satış/kiralama kapalı (gün'e bakılmaz).</summary>
    public bool TumSatisKapali { get; set; }

    // Geçerlilik (boş → süresiz)
    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
