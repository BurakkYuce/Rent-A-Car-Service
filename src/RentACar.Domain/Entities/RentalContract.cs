using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Kira sözleşmesi (sistemin kalbi — PR #3 çekirdeği). Tenant-owned + auditable.
/// Aktif (Kirada) sözleşmeler için araç+tarih çakışması DB-seviyesi exclusion
/// constraint ile engellenir (double-booking koruması). Teslim (Çıkış KM/yakıt) ve
/// dönüş (Dönüş KM/yakıt/uzatma) alanları PR #4'te doldurulur.
/// </summary>
public class RentalContract : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz sözleşme no (örn. KS-000001).</summary>
    public string SozlesmeNo { get; set; } = string.Empty;

    public RentalStatus Durum { get; set; } = RentalStatus.Kirada;

    /// <summary>Hangi rezervasyondan dönüştü (varsa).</summary>
    public Guid? ReservationId { get; set; }

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }

    public string? CikisOfisi { get; set; }
    public string? DonusOfisi { get; set; }

    // Aşım ücret parametreleri (oluştururken/teslimde girilir; 0 = ücretsiz/sınırsız).
    public int KmLimit { get; set; }            // toplam serbest km (0 = sınırsız)
    public decimal FazlaKmUcret { get; set; }   // aşım km başına
    public decimal YakitBirimUcret { get; set; } // eksik yakıt birimi başına

    // Teslim (PR #4)
    public int? CikisKm { get; set; }
    public int? CikisYakit { get; set; }

    // Dönüş (PR #4)
    public int? DonusKm { get; set; }
    public int? DonusYakit { get; set; }
    public DateTimeOffset? GercekDonusTar { get; set; }

    // Dönüşte hesaplanan ek bedeller
    public int FazlaKm { get; set; }
    public decimal FazlaKmBedeli { get; set; }
    public int EksikYakit { get; set; }
    public decimal YakitBedeli { get; set; }
    public int UzatmaGun { get; set; }
    public decimal UzatmaBedeli { get; set; }

    // ---- Dönüş ek alanları (referans sistem parite; additive) ----
    /// <summary>Aşımdan düşülen bedava km (KM Hediye) — dönüşte girilir; FazlaKm hesabında KmLimit'e eklenir.</summary>
    public int? KmHediye { get; set; }
    /// <summary>Dönüş/bitiş sebebi (Normal/Erken İade/Hasar/Arıza/Değişim/Diğer — serbest metin).</summary>
    public string? BitisSebebi { get; set; }
    /// <summary>Dönüşü teslim alan personel (gevşek referans — Personel.Id; FK yok).</summary>
    public Guid? TeslimAlanPersonelId { get; set; }

    /// <summary>2. sürücü (opsiyonel) — Customer bağı (gevşek referans; PII Customer'da şifreli, sözleşmede
    /// decrypt'li gösterilir). Oluşturma anında yakalanır (create-time otorite).</summary>
    public Guid? IkinciSurucuId { get; set; }

    // ---- Tam teklif bileşenleri (fiyat motoru "Otomatik" — BİLGİ/döküm; Tutar zaten net brütü içerir,
    // bunlar Tutar'a AYRICA katılmaz → çift-sayım yok; KURAL A) ----
    /// <summary>Hediye (bedava) gün sayısı — kiralama kuralından. FaturalananGun = Gun − HediyeGun.</summary>
    public int? HediyeGun { get; set; }
    /// <summary>Faturalanan gün (Gun − HediyeGun) — motordan.</summary>
    public int? FaturalananGun { get; set; }
    /// <summary>Uygulanan iskonto tutarı (brüt) — kural iskonto oranından hesaplandı; Tutar'a zaten yansıdı.</summary>
    public decimal? IskontoTutar { get; set; }
    /// <summary>Hafta sonu farkı (brüt) — Cmt/Pzr günlerine ek; Tutar'a zaten yansıdı.</summary>
    public decimal? HaftaSonuFark { get; set; }

    public int Gun { get; set; }
    public decimal GunlukUcret { get; set; }
    public decimal Tutar { get; set; }          // baz kira tutarı
    public decimal GenelToplam { get; set; }    // Tutar + ek bedeller
    public decimal Tahsilat { get; set; }
    public decimal Bakiye { get; set; }          // GenelToplam - Tahsilat

    // ---- Ödeme-derinlik alanları (roadmap A2; additive, NULLABLE) ----
    // BİLGİ AMAÇLI: deftere/bakiyeye YANSIMAZ (GenelToplam/Tahsilat/Bakiye'yi etkilemez). Provizyon/depozito
    // bloke tutarlar; komisyon acente/kaynak; drop farklı yere teslim; sonra-öde peşin-olmayan oran.
    public decimal? Provizyon { get; set; }
    public decimal? Depozito { get; set; }
    public decimal? KomisyonOran { get; set; }   // %
    public decimal? KomisyonTutar { get; set; }
    public decimal? DropUcreti { get; set; }
    public decimal? SonraOdeOran { get; set; }   // %

    public string? Aciklama { get; set; }

    // ---- referans sistem parite (additive metadata; mevcut para hesabını etkilemez) ----
    public string? KiralamaTuru { get; set; }   // Kısa/Uzun/İkame/Aylık
    public string? FaturalamaTipi { get; set; } // Müşteri Ödemeli/Full Credit/Extralar Müşteriye/Drop Dahil/Diğer
    public string? FiyatTuru { get; set; }       // Otomatik/KDV Dahil Günlük/Günlük/KDV Dahil Toplam/Toplam
    public string? Doviz { get; set; }           // TL/EURO/USD

    /// <summary>Kira dövizinin OLUŞTURMA anındaki TL kuru (TRY=1) — YALNIZ RAPORLAMA (CRM ciro/segment TL-baz;
    /// denetim O5: FX+TL ciroları düz toplanıyordu). Defter/fatura BUNU KULLANMAZ (fatura kuru fatura anında
    /// yakalanır). Retroaktif değişmez (snapshot).</summary>
    public decimal KurSnapshot { get; set; } = 1m;

    /// <summary>Kira ek hizmet kalemleri (bebek koltuğu, GPS…). GenelToplam'a brüt olarak girer.</summary>
    public List<RentalAddOn> EkHizmetler { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
