using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Tarife (rate card) — araç grubu + gün kademesi için günlük birim fiyat. Tenant-owned +
/// auditable. Şube/araç-grubu gibi serbest-metin <see cref="Grup"/> ile eşleşir (additive;
/// FK değil — mevcut Vehicle.Grup metniyle uyumlu). Rezervasyon/teklif fiyatlaması bu
/// tabloyu OKUR (lookup); şimdilik fiyat hâlâ manuel girilebilir, tarife öneri/zemin sağlar.
/// </summary>
public class RateCard : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz). Servis büyük harfe normalize eder.</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;

    /// <summary>Araç grubu / fiyat sınıfı (Vehicle.Grup ile eşleşir; serbest metin).</summary>
    public string Grup { get; set; } = string.Empty;

    /// <summary>Gün kademesi alt sınırı (dahil). Ör. 1.</summary>
    public int MinGun { get; set; } = 1;
    /// <summary>Gün kademesi üst sınırı (dahil). Ör. 3 (1–3 gün), büyük değer = üst kademe.</summary>
    public int MaxGun { get; set; } = 9999;

    public decimal GunlukUcret { get; set; }
    public string Doviz { get; set; } = "TRY";

    /// <summary>Sezon/dönem geçerlilik başlangıcı (null = sınırsız).</summary>
    public DateTimeOffset? GecerliBas { get; set; }
    /// <summary>Sezon/dönem geçerlilik bitişi (null = sınırsız).</summary>
    public DateTimeOffset? GecerliBit { get; set; }

    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>Bu tarife verilen gün sayısı + tarihi kapsıyor mu? (Grup eşleşmesi çağırana ait.)</summary>
    public bool Covers(int gun, DateTimeOffset tarih)
        => Aktif
           && gun >= MinGun && gun <= MaxGun
           && (GecerliBas is null || GecerliBas <= tarih)
           && (GecerliBit is null || GecerliBit >= tarih);
}
