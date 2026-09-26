using RentACar.Domain.Enums;

namespace RentACar.Application.Baflar;

/// <summary>
/// BAF liste "Lokasyon" filtresi (canlı baf_ara.aspx) — çıkış şubesi ile dönüş şubesinin
/// karşılaştırması. Şubelerden biri boşsa kayıt HİÇBİR kovaya girmez (bilinmeyeni "aynı" saymak
/// yanlış bilgi olurdu).
/// </summary>
public enum BafLocation
{
    /// <summary>Çıkış şubesi == dönüş şubesi (ikisi de dolu).</summary>
    AyniOfis = 1,
    /// <summary>Çıkış şubesi != dönüş şubesi (ikisi de dolu).</summary>
    FarkliOfis = 2
}

/// <summary>
/// BAF (personel araç tahsis) liste filtresi (FAZ-18 — canlı <c>baf_ara.aspx</c> paritesi).
/// Boş alan = kısıt yok. Tarih aralığı <see cref="Domain.Entities.Baf.CikisTarihi"/>'ne uygulanır.
/// <b>Şube kapsamı filtreden BAĞIMSIZDIR</b>: kullanıcının göremediği şubenin kaydı, ofis filtresi
/// o şubeye ayarlansa bile listede çıkmaz (kapsam kuralı repoda ayrıca uygulanır).
/// </summary>
public sealed class BafFilter
{
    /// <summary>Tahsis edilen personel (canlı "Kullanıcı" araması).</summary>
    public Guid? PersonelId { get; set; }

    /// <summary>Plaka PARÇA eşleşmesi (araç kolonu Baf'ta VehicleId'dir → Vehicles alt-sorgusu).</summary>
    public string? Plaka { get; set; }

    public BafStatus? Durum { get; set; }

    public BafUsagePurpose? KullanimAmaci { get; set; }

    /// <summary>Aynı ofis / farklı ofis (çıkış-dönüş şube karşılaştırması).</summary>
    public BafLocation? Lokasyon { get; set; }

    /// <summary>Çıkış şubesi (tam ad eşleşmesi).</summary>
    public string? Ofis { get; set; }

    /// <summary>Çıkış tarihi ≥ (dahil).</summary>
    public DateTimeOffset? Bas { get; set; }

    /// <summary>Çıkış tarihi ≤ (dahil — çağıran gün sonunu geçirir).</summary>
    public DateTimeOffset? Bit { get; set; }

    /// <summary>Hiçbir alan dolu değilse filtre yok demektir (liste "Temizle" bağlantısı için).</summary>
    public bool Bos => PersonelId is null && string.IsNullOrWhiteSpace(Plaka) && Durum is null
        && KullanimAmaci is null && Lokasyon is null && string.IsNullOrWhiteSpace(Ofis)
        && Bas is null && Bit is null;
}
