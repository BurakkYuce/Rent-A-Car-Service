using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Rezervasyon. Tenant-owned + auditable. Bir araç + tarih aralığı için ön kayıt;
/// Tasfiye ile kira sözleşmesine dönüşür. (PR #3: fiyat manuel günlük ücret; fiyat
/// motoru ertelendi.)
/// </summary>
public class Reservation : ITenantOwned, IAuditable, IOfficeScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz sıra (örn. RZ-000001).</summary>
    public string ReservationNo { get; set; } = string.Empty;

    public ReservationStatus Durum { get; set; } = ReservationStatus.Rezerv;

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }

    public string? CikisOfisi { get; set; }
    /// <summary>Türetilmiş çıkış-şube FK'sı (FAZ 5-C4; CikisOfisi→Location→SubeId; interceptor doldurur).</summary>
    public Guid? CikisSubeId { get; set; }
    string? RentACar.Domain.Common.IOfficeScoped.OfisAdi => CikisOfisi;
    Guid? RentACar.Domain.Common.IOfficeScoped.OfisSubeFk { get => CikisSubeId; set => CikisSubeId = value; }
    public string? DonusOfisi { get; set; }

    public int Gun { get; set; }
    public decimal GunlukUcret { get; set; }
    public decimal Tutar { get; set; }

    // Tam teklif dökümü (fiyat motoru "Otomatik" — BİLGİ; kiraya taşınır, sözleşmede gösterilir; PR-F2).
    public int? HediyeGun { get; set; }
    public int? FaturalananGun { get; set; }
    public decimal? IskontoTutar { get; set; }
    public decimal? HaftaSonuFark { get; set; }

    // FAZ 4.5 — OTA/web kanal bileşen fiyatları (bilgi; kanal entegrasyonu/REST doldurur; deftere yansımaz).
    public decimal? OtaKiraBedeli { get; set; }
    public decimal? OtaDropBedeli { get; set; }
    public decimal? OtaBebekKoltugu { get; set; }
    public decimal? OtaNavigasyon { get; set; }
    public decimal? OtaLcf { get; set; }
    public decimal? OtaCdw { get; set; }
    public decimal? OtaScdw { get; set; }
    public decimal? OtaEkSurucu { get; set; }

    // Anlaşılan aşım koşulları (kiraya çevrilirken sözleşmeye taşınır).
    public int KmLimit { get; set; }
    public decimal FazlaKmUcret { get; set; }
    public decimal YakitBirimUcret { get; set; }

    // ---- Ödeme-derinlik alanları (roadmap A2; additive, NULLABLE; deftere YANSIMAZ — bilgi amaçlı) ----
    public decimal? Provizyon { get; set; }
    public decimal? Depozito { get; set; }
    public decimal? KomisyonOran { get; set; }   // %
    public decimal? KomisyonTutar { get; set; }
    public decimal? DropUcreti { get; set; }
    public decimal? SonraOdeOran { get; set; }   // %

    public string? Aciklama { get; set; }
    public string? Kaynak { get; set; } // roadmap H2: rezervasyon kaynağı (ReservationSource kodu/adı)

    // ---- FAZ-48 — talep/organizasyon bilgisi (BİLGİ ALANI; deftere/hesaba GİRMEZ) ----
    // Aynı adlı alanlar RentalContract'ta zaten vardı; rezervasyon aşamasında toplanıp
    // "Kiraya Çevir"de sözleşmeye AYNEN taşınır (tek veri kaynağı; kullanıcı iki kez girmez).
    /// <summary>Talebin türü (ör. "Kurumsal", "Bireysel", "Sigorta İkame"). Bilgi.</summary>
    public string? TalepTuru { get; set; }
    /// <summary>Talebin geldiği birim/kanal (ör. "Çağrı Merkezi", "Şube"). Bilgi.</summary>
    public string? GeldigiBirim { get; set; }
    /// <summary>Müşteri/kurum onay kodu (ör. sigorta dosya onayı). Bilgi.</summary>
    public string? OnayKodu { get; set; }
    /// <summary>Proje/organizasyon adı (kurumsal filo işlerinde). Bilgi.</summary>
    public string? ProjeAdi { get; set; }

    /// <summary>Promosyon/kampanya kodu (FAZ 3.A5) — girildiyse fiyat kodlu kuralla çözülür (REPLACE;
    /// kapsam tutmazsa gürültülü red). Reprice'ta yeniden doğrulanır (süresi dolan kod alanı temizletir).</summary>
    public string? KampanyaKodu { get; set; }

    /// <summary>Fiyat türü (FAZ 3.A6 adversarial B2): net-mod niyeti dönüşümde kiraya taşınsın diye
    /// PERSIST edilir (önceden yalnız fiyatlamada kullanılıp düşürülüyordu → kira brüt-mod muamelesi
    /// görür, KDV matrahı tenant oranı değişince sessizce sapardı).</summary>
    public string? FiyatTuru { get; set; }
    /// <summary>NET modda gross-up ANINDA kullanılan KDV oranı — dönüşümde kiraya kopyalanır (A6-B2).</summary>
    public decimal? KdvOranSnapshot { get; set; }

    /// <summary>Tasfiye sonrası oluşan kira sözleşmesi.</summary>
    public Guid? RentalContractId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
