using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// BAF — personele araç tahsis kaydı (roadmap L5): çıkış (km/yakıt/şube) → dönüş (km/yakıt). Bir personele
/// şirket aracı zimmetleme/teslim. NOT: clone'daki DamageFile (hasar dosyası) ile karıştırılmamalı — bu
/// personel araç tahsisidir. Tenant-owned + auditable; full-CRUD. DEFTER POSTLAMAZ (zimmet kaydı).
/// </summary>
public class Baf : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Tenant-başına boşluksuz no (BAF-000001).</summary>
    public string No { get; set; } = string.Empty;

    public Guid PersonelId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTimeOffset CikisTarihi { get; set; } = DateTimeOffset.UtcNow;
    public int CikisKm { get; set; }
    public int? CikisYakit { get; set; }

    public DateTimeOffset? DonusTarihi { get; set; }
    public int? DonusKm { get; set; }
    public int? DonusYakit { get; set; }

    /// <summary>Çıkış şubesi (metin). Şube kapsamı bu alandan işler (C3 — Baf'ta SubeId FK'si yok).</summary>
    public string? Sube { get; set; }
    public BafDurum Durum { get; set; } = BafDurum.Acik;
    public string? Aciklama { get; set; }

    // ---- FAZ-18: canlı baf_islemleri.aspx alan derinliği (hepsi BİLGİ — defter postlamaz) ----

    /// <summary>Tahsisin amacı (11 seçenek). Bilgi/rapor alanı; iş kuralı işletmez.</summary>
    public BafKullanimAmaci? KullanimAmaci { get; set; }

    /// <summary>Tahsisi onaylayan personel (Personel.Id). Bilgi — yetki kontrolü DEĞİL.</summary>
    public Guid? Onaylayan { get; set; }

    /// <summary>Araç bu tahsis sırasında kiraya verilebilir mi (canlı Kiraya_Ver kutusu). Bilgi.</summary>
    public bool KirayaVer { get; set; }

    /// <summary>Dönüş (teslim alınan) şube — çıkış <see cref="Sube"/>'sinden AYRI. Kapsam kuralına GİRMEZ.</summary>
    public string? DonusSube { get; set; }

    /// <summary>Çıkış saati (tarihten ayrı granül — canlı Cikis_Saat).</summary>
    public TimeOnly? CikisSaat { get; set; }

    /// <summary>Dönüş saati (canlı Donus_Saat).</summary>
    public TimeOnly? DonusSaat { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
