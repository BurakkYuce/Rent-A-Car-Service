using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Uzun-dönem (filo) kiralama sözleşmesi (roadmap L1): aylık ücretli, çok-aylık süreli operasyonel kiralama.
/// Günlük <see cref="RentalContract"/>'tan AYRI. Bu kayıt sözleşme şartları + taksit planının kaynağıdır;
/// DEFTER POSTLAMAZ — çok-aylık gelir peşin tanınmaz, gelir aylık faturalama (manuel fatura) ile tanınır.
/// Tenant-owned + auditable; full-CRUD (mali değişmez belge DEĞİL).
/// </summary>
public class FiloKiralama : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (FK-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid MusteriId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset BasTar { get; set; } = DateTimeOffset.UtcNow;
    public int SureAy { get; set; }
    public decimal AylikUcret { get; set; }
    public decimal KdvOrani { get; set; } = 0.20m; // kesir (0.20 = %20)
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public int? ToplamKmLimiti { get; set; }
    public decimal? DamgaVergisi { get; set; }

    // ---- FAZ-21 sözleşme meta-alanları (canlı filo_arac_kiralama.aspx) ----
    // Hiçbiri para hesabına GİRMEZ: taksit planı yalnız AylikUcret/SureAy/KdvOrani/Kur/DamgaVergisi
    // ile kurulur. Bunlar sözleşme künyesi/izleme alanlarıdır.
    /// <summary>Sözleşmeyi yapan satış temsilcisi (serbest metin — Personel FK'sı bilinçli yok).</summary>
    public string? SatisTemsilcisi { get; set; }
    /// <summary>Faturalama biçimi etiketi ("Dönem"/"Kırık"). Serbest metin + öneri listesi.</summary>
    public string? FaturaTuru { get; set; }
    /// <summary>Sözleşmenin düzenlendiği tarih — <see cref="BasTar"/>'dan AYRI (kiralama başlangıcı
    /// sözleşme tarihinden sonra olabilir).</summary>
    public DateTimeOffset? SozlesmeTarihi { get; set; }
    /// <summary>İmza tarihi (sözleşme tarihinden de ayrı olabilir).</summary>
    public DateTimeOffset? ImzaTarih { get; set; }
    public string? MakbuzNo { get; set; }
    public string? DosyaNo { get; set; }
    /// <summary>Firmanın kendi sözleşme numarası — sistemin ürettiği <see cref="No"/>'dan AYRI.</summary>
    public string? SozlesmeNo { get; set; }
    /// <summary>Fatura vade günü (bilgi amaçlı; tahsilat akışına bağlı değil).</summary>
    public int? VadeGun { get; set; }
    /// <summary>Fiyatlandırma biçimi etiketi ("Aylık"/"30 Gün Aylık"/"KDV Dahil"/"30 Gün Dahil").
    /// BİLGİ amaçlıdır — taksit planını DEĞİŞTİRMEZ (plan AylikUcret/KdvOrani'ndan kurulur).</summary>
    public string? FiyatTuru { get; set; }
    /// <summary>Sözleşmenin geldiği kanal/kaynak.</summary>
    public string? Kaynak { get; set; }
    /// <summary>Teslim anındaki kilometre.</summary>
    public int? CikisKm { get; set; }
    /// <summary>Dönüşte okunan FİİLİ toplam kilometre. <see cref="ToplamKmLimiti"/> ile
    /// KARIŞTIRILMAMALI: "Limiti" sözleşmedeki üst sınır, bu ise gerçekleşen değerdir.</summary>
    public int? ToplamKm { get; set; }

    public FleetRentalStatus Durum { get; set; } = FleetRentalStatus.Aktif;
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
