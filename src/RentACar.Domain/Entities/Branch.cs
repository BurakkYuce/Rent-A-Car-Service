using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Şube (branch) master kaydı. Tenant-owned + auditable. Bu sürümde additive bir master
/// listedir: mevcut serbest-metin "Sube" alanlarını (Vehicle/Expense/User.AtanmisSube)
/// FK'ye çevirmez — yönetilen bir şube sözlüğü sağlar (geçerli şube adları + iletişim).
/// Form açılır listeleri buradan beslenir. (FK migrasyonu ayrı bir karar/PR.)
/// </summary>
public class Branch : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (ör. MERKEZ, ESB). Tenant içinde benzersiz, zorunlu doğal anahtar.</summary>
    public string Kod { get; set; } = string.Empty;

    /// <summary>Şube adı (ör. Merkez Ofis). Zorunlu.</summary>
    public string Ad { get; set; } = string.Empty;

    public string? Adres { get; set; }

    public string? Telefon { get; set; }

    // ---- Derinlik (roadmap K3; additive, nullable) ----
    public string? Eposta { get; set; }
    public string? Il { get; set; }
    public string? Ilce { get; set; }
    /// <summary>Şube yetkilisi/sorumlusu.</summary>
    public string? Yetkili { get; set; }
    /// <summary>Çalışma saatleri (serbest metin, ör. "09:00-18:00").</summary>
    public string? CalismaSaatleri { get; set; }
    /// <summary>Şube komisyon oranı (0..1).</summary>
    public decimal? KomisyonOran { get; set; }
    /// <summary>Belge/evrak no öneki (ör. "MRK-").</summary>
    public string? EvrakNoOnek { get; set; }

    /// <summary>Pasif şubeler açılır listelerde gizlenir ama kayıtlar korunur (silme yerine).</summary>

    // ---- FAZ-23 şube derinliği (canlı sube_tanimlama.aspx) ----
    /// <summary>Halka açık sitede görünen ad (kurumsal <see cref="Ad"/>'dan farklı olabilir).</summary>
    public string? WebIsim { get; set; }
    /// <summary>Sözleşme/faturada basılacak firma ünvanı (şube başka bir tüzel kişilikse).</summary>
    public string? FirmaUnvani { get; set; }
    /// <summary>Web rezervasyonunun kaç saat ÖNCEDEN yapılabileceği (kapanış eşiği).</summary>
    public int? WebRezOncesiSaat { get; set; }
    public decimal? Enlem { get; set; }
    public decimal? Boylam { get; set; }
    /// <summary>
    /// HİZMETTEN alınan komisyon oranı. <see cref="KomisyonOran"/> ile AYNI ŞEY DEĞİLDİR:
    /// o genel (satış) komisyonu, bu ek hizmet/servis üzerinden alınan pay. Formda ikisi yan yana
    /// ve etiketleri açıkça ayrı yazılı.
    /// </summary>
    public decimal? HizmetKomisyonOran { get; set; }
    /// <summary>Takvim/listelerde bu şubenin rezervasyon rengi ("#rrggbb").</summary>
    public string? RezervasyonRengi { get; set; }
    /// <summary>true → bu şube ALIŞ (çıkış) noktası olarak seçilemez; yalnız dönüş noktasıdır.</summary>
    public bool AlisSubesiDegilMi { get; set; }
    public int? WebSira { get; set; }
    public string? WebOtoparkId { get; set; }
    public string? BayiCariKod { get; set; }
    public string? BayiOfisId { get; set; }
    /// <summary>Komisyonun hangi taraftan hesaplanacağı ("Satıştan"/"Maliyetten") — bilgi alanı.</summary>
    public string? KomisyonHesabi { get; set; }
    public string? OnlineRezId { get; set; }
    /// <summary>Sözleşme numarası biçimi (bilgi; sistem numarası boşluksuz sayaçtan üretilir).</summary>
    public string? SozlesmeNoFormati { get; set; }
    /// <summary>Varsayılan nakit hesabı (FinancialAccount). Gevşek referans — FK YOK.</summary>
    public Guid? NakitHesapId { get; set; }
    /// <summary>Varsayılan banka hesabı (FinancialAccount). Gevşek referans — FK YOK.</summary>
    public Guid? BankaHesapId { get; set; }
    public string? EntegrasyonKodu { get; set; }
    /// <summary>Şube görseli için dosya YOLU. Dosya YÜKLEME bu fazda yok; yalnız metin saklanır.</summary>
    public string? ResimDosyasi { get; set; }
    /// <summary>Haftalık çalışma saatleri (serbest metin, gün başına satır). Yapılandırılmış
    /// gün-saat modeli lokasyon tarafıyla birlikte ele alınacak — burada metin olarak tutulur.</summary>
    public string? HaftalikCalismaSaatleri { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
