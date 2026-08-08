using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Rezervasyon kaynağı tanımı (master sözlük): rezervasyon/müşteri kaynaklarının adlandırılmış
/// listesi (ör. "Web", "Telefon", "Bayi", "Tavsiye"). Tenant-owned + auditable. Rezervasyon ve
/// cari formlarındaki Kaynak açılır listesini besler (additive).
/// </summary>
public class ReservationSource : ITenantOwned, IAuditable, IMasterTanim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    /// <summary>Kaynağın arkasındaki tedarikçi/acente adı (ör. "Rentalcars", "Booking").</summary>
    public string? Tedarikci { get; set; }

    // FAZ-24 — ORAN ALANLARI YÜZDEDİR (12,5 = %12,5), oransal katsayı DEĞİL. Bu faz alanları
    // yalnız KAYDEDER: hiçbir fiyat/komisyon/karlılık hesabı bunları OKUMAZ. "Hangi hesaba,
    // ne zaman girecek" ayrı bir para incelemesidir (bkz. docs/roadmap/FAZ-24…). Bir tüketici
    // eklenmeden önce yüzde/katsayı birimi ve geçmişe etki sorusu cevaplanmalıdır.

    /// <summary>Kira bedeli üzerinden tedarikçi oranı — YÜZDE (12,5 = %12,5). Hesaba girmez.</summary>
    public decimal? KiraOrani { get; set; }

    /// <summary>Ek hizmet bedeli üzerinden tedarikçi oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? HizmetOrani { get; set; }

    /// <summary>Drop (tek yön) bedeli üzerinden tedarikçi oranı — YÜZDE. Hesaba girmez.</summary>
    public decimal? DropOrani { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
