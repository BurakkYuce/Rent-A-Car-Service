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


    // ---- FAZ-72: teminat/görünürlük bayrakları + tarife grubu referansı ----
    // Hepsi BİLGİ amaçlıdır: RateCard fiyat motorunda zaten DEPRECATED-fallback konumunda ve bu
    // alanlar hiçbir hesaba girmez. Ekran canlıdaki tarifeler.aspx'in doğrudan karşılığıdır.
    /// <summary>SCDW (muafiyet azaltma) fiyata dahil mi.</summary>
    public bool ScdwDahil { get; set; }
    /// <summary>Mini hasar teminatı dahil mi.</summary>
    public bool MiniHasarDahil { get; set; }
    /// <summary>Hırsızlık teminatı dahil mi.</summary>
    public bool HirsizlikDahil { get; set; }
    /// <summary>SCDW zorunlu mu (müşteri reddedemez).</summary>
    public bool ScdwZorunlu { get; set; }
    /// <summary>true → satır listelerde/tekliflerde GİZLENİR. Varsayılan false (görünür).</summary>
    public bool Gosterme { get; set; }
    /// <summary>Opsiyonel tarife grubu referansı. Grup silinirse NULL'a düşer (ON DELETE SET NULL) —
    /// tarife satırı silinmez, yalnız bağı kopar.</summary>
    public Guid? TarifeGrubuId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>Bu tarife verilen gün sayısı + tarihi kapsıyor mu? (Grup eşleşmesi çağırana ait.)</summary>
    public bool Covers(int day, DateTimeOffset date)
        => Aktif
           && day >= MinGun && day <= MaxGun
           && (GecerliBas is null || GecerliBas <= date)
           && (GecerliBit is null || GecerliBit >= date);
}
