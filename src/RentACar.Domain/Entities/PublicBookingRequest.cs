using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

public enum PublicBookingRequestDurum
{
    Yeni = 0,
    Donustu = 1,
    Reddedildi = 2
}

/// <summary>
/// Halka açık siteden gelen rezervasyon TALEBİ (lead) — PR-8. GERÇEK rezervasyon DEĞİLDİR:
/// <see cref="Reservation"/> `PermissionGuard.Require(OperationsWrite)` + var olan `MusteriId` ister,
/// anonim ziyaretçi bunları sağlayamaz. Personel talebi sonradan mevcut Cari+Rezervasyon akışına
/// DÖNÜŞTÜRÜR (bkz. PublicBookingRequestService.DonusturAsync).
///
/// PII sınırı (bilinçli): TcKimlik/EhliyetNo TOPLANMAZ — yalnız Ad Soyad/Telefon/E-posta
/// (`CustomerContact` ile aynı hassasiyet sınıfı) → F2 şifreleme/blind-index GEREKMEZ.
/// </summary>
public class PublicBookingRequest : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string AdSoyad { get; set; } = string.Empty;
    public string Telefon { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>PR-7 aramasından "Teklif İste" ile gelindiyse dolu (grup KODU).
    /// PR-14: ARTIK YAZILMIYOR — vitrin ilan bazlı oldu, yerini <see cref="IlanId"/> aldı.
    /// Kolon eski talepler için duruyor (veri silinmez).</summary>
    public string? AracGrupKod { get; set; }

    /// <summary>PR-14: talebin geldiği ilan. Personel dönüştürürken müşterinin HANGİ aracı
    /// gördüğünü ve fiyatını bilsin.</summary>
    public Guid? IlanId { get; set; }

    /// <summary>İlan başlığının o ANKİ hâli. İlan sonradan yeniden adlandırılsa/silinse bile
    /// personel müşterinin ne gördüğünü görebilmeli (snapshot).</summary>
    public string? IlanBaslik { get; set; }

    public DateTimeOffset BasTar { get; set; }
    public DateTimeOffset BitTar { get; set; }
    public string? Sube { get; set; }
    public string? Not { get; set; }

    /// <summary>Ziyaretçinin arama sonucunda GÖRDÜĞÜ günlük fiyat. Doğrudan forma gelen taleplerde null.
    ///
    /// <para>PR-14 UYARI: kolon adı "KdvDahil" diyor ama ARTIK HER ZAMAN ÖYLE DEĞİL — ilan başına
    /// KDV işareti var, bu rakam <see cref="GosterilenKdvDahil"/> ile birlikte okunmalıdır. Ad
    /// tarihsel; kolon yeniden adlandırılmadı (mevcut veriyi ve migration zincirini kırmamak için).</para></summary>
    public decimal? GosterilenGunlukUcretKdvDahil { get; set; }

    /// <summary>PR-14: <see cref="GosterilenGunlukUcretKdvDahil"/> KDV dahil miydi. Personelin
    /// rezervasyona NET fiyat yazabilmesi için şart (ERP zincirinin tamamı NET çalışır).</summary>
    public bool? GosterilenKdvDahil { get; set; }

    public PublicBookingRequestDurum Durum { get; set; }

    /// <summary>Dönüştürme sonucu oluşan rezervasyon. `Durum=Donustu` ama bu NULL ise: dönüştürme
    /// yarıda kalmış demektir (bkz. servisin claim/release notu) — staff ekranı bunu uyarı olarak gösterir.</summary>
    public Guid? DonusenReservationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
