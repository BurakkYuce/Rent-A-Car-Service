using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Rezervasyon/teklif üzerindeki müşteri ÖZEL TALEBİ ve karşılanma takibi (canlı TürevRent
/// <c>rezsartlar.aspx</c> karşılığı). Örnek: "bebek koltuğu", "araç sabah 07:00'de teslim edilsin".
///
/// <para>Operasyonel bir NOT defteridir: para taşımaz, deftere kayıt POSTLAMAZ, fiyatı etkilemez.
/// Ücretli ek hizmet (bebek koltuğu ÜCRETİ) <see cref="RentalAddOn"/>'dur — bu tablo onun yerine
/// geçmez; ikisi birlikte kullanılır (talep burada, ücret orada).</para>
///
/// <para><see cref="ReservationId"/>/<see cref="QuotationId"/> bilinçli olarak FK DEĞİL, gevşek
/// <c>Guid?</c> bağlamadır: talep rezervasyon açılmadan önce de (telefonda) girilebilmeli ve
/// rezervasyon iptal/silinince talep kaydı düşmemeli.</para>
/// </summary>
public class RezSart : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Talebi yapan cari (Customer.Id). Zorunlu — talep her zaman bir müşteriye aittir.</summary>
    public Guid MusteriId { get; set; }

    /// <summary>Serbest gruplama etiketi (ör. "Ekipman", "Teslim", "Ödeme"). Boş → gruplanmamış.</summary>
    public string? Grup { get; set; }

    /// <summary>Talebin kendisi. Zorunlu.</summary>
    public string Sart { get; set; } = string.Empty;

    /// <summary>Talebin geçerli olduğu aralık (ör. kiralama dönemi). Boş → süresiz/belirsiz.</summary>
    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }

    /// <summary>Talebin alındığı an.</summary>
    public DateTimeOffset TalepTarihi { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Talebin karşılandığı an. <b>null = henüz karşılanmadı</b> — durum bu alandan türetilir,
    /// ayrı bir bayrak kolonu YOK (iki kaynak birbiriyle çelişemesin).</summary>
    public DateTimeOffset? KarsilamaTarihi { get; set; }

    /// <summary>Talebi karşılayan personel adı — serbest metin. PII değil (Personel FK'sı bilinçli yok:
    /// talebi sistemde kullanıcısı olmayan biri de karşılamış olabilir).</summary>
    public string? TeslimEden { get; set; }

    /// <summary>Gevşek bağlama — FK YOK (bkz. sınıf özeti).</summary>
    public Guid? ReservationId { get; set; }
    public Guid? QuotationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
