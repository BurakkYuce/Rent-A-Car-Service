using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç satışı. Tenant-owned + auditable + DB-immutable (mali belge). Satış kesilince
/// alıcı cari BORÇLANIR: Borç Cari (brüt) / Alacak Gelir (net) / Alacak KDV (kdv) — dengeli.
/// Araç durumu Satildi'ye geçer (filodan çıkar). Düzeltme = ters kayıt (bu PR'da kapsam dışı).
/// </summary>
public class VehicleSale : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (ST-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid VehicleId { get; set; }
    public Guid AliciCariId { get; set; }   // satın alan müşteri (cari)

    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? NoterNo { get; set; }

    public decimal SatisNet { get; set; }
    public decimal KdvOrani { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string Currency { get; set; } = "TRY";
    public decimal Kur { get; set; } = 1m;

    public string? Aciklama { get; set; }
    public SatisDurum Durum { get; set; } = SatisDurum.Tamamlandi;

    // roadmap G2 (additive — bilgilendirme; deftere yansımaz)
    public decimal? HedefFiyat { get; set; }
    public int? SatisKm { get; set; }
    public string? SatisKanali { get; set; }
    public string? Devir { get; set; }


    // ---- FAZ-28: ihale/noter bilgileri (bilgi alanları; satış tutarına etkisi yok) ----
    public DateTimeOffset? IhaleTarihi { get; set; }
    public string? IhaleFirmasi { get; set; }
    public DateTimeOffset? NoterSatisTarihi { get; set; }

    // ---- FAZ-18: canlı arac_satis.aspx alan derinliği ----
    // KARARLAR.md GENEL POLİTİKA: bu alanların TAMAMI **BİLGİDİR — muhasebe defterine YAZMAZ**.
    // Deftere yazılan tutar YALNIZ SatisNet/KdvTutar/GenelToplam'dır (VehicleSaleService.BuildEntries);
    // Araç Karnesi / Karlılık / Filo Analiz P&L'i yalnız AccountLedgerEntry'den okur. Buradaki bir
    // tutarı rapora toplamak ÇİFT SAYIM olur (CLAUDE.md §6 "P&L yalnız defterden").
    // NOT: VehicleSales DB-immutable (vehiclesales_immutable trigger) → bu alanlar yalnız KAYIT
    // ANINDA yazılır; sonradan güncellenemez (SatisiVerildi dâhil — bilinçli).

    /// <summary>İlan sürecinde araç kiraya veriliyor muydu (canlı Kiraya_Verme kutusu). Bilgi.</summary>
    public bool KirayaVerme { get; set; }

    /// <summary>İlan anındaki km — <see cref="SatisKm"/>'den AYRI (satış anındaki km). Bilgi.</summary>
    public int? IlanKm { get; set; }

    /// <summary><see cref="HedefFiyat"/> (liste fiyatı) para birimi. Bilgi; kur/KDV hesabına GİRMEZ.</summary>
    public string? ListeDoviz { get; set; }

    /// <summary>Satış noktası (galeri/şube/plaza adı — canlı Satis_Noktasi). Bilgi.</summary>
    public string? SatisNoktasi { get; set; }

    /// <summary>Uygulanan kampanya adı/kodu. Bilgi — fiyata etki ETMEZ, SatisNet çağırandan gelir.</summary>
    public string? UygulananKampanya { get; set; }

    /// <summary>İhale sayı/karar numarası (İhale bloğu: Firma + Tarih + Sayı). Bilgi.</summary>
    public string? IhaleSayisi { get; set; }

    /// <summary>
    /// Devir durumu bayrağı (canlı Satisi_Verildi) — <see cref="Devir"/> serbest metninden AYRI.
    /// Yalnız DURUM bilgisidir: defteri, <see cref="Durum"/>'u ve araç durumunu ETKİLEMEZ.
    /// </summary>
    public bool SatisiVerildi { get; set; }

    /// <summary>Muhasebe yevmiye numarası (dış muhasebe programı referansı). Bilgi — defter yazmaz.</summary>
    public string? YevmiyeNumarasi { get; set; }

    /// <summary>İkinci açıklama satırı (canlı Aciklama2).</summary>
    public string? Aciklama2 { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
