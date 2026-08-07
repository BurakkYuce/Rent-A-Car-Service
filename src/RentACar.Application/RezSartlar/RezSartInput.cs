namespace RentACar.Application.RezSartlar;

/// <summary>Rez şartı (müşteri özel talebi) oluştur/güncelle giriş modeli.</summary>
public sealed class RezSartInput
{
    public Guid MusteriId { get; set; }
    public string Sart { get; set; } = string.Empty;
    public string? Grup { get; set; }

    public DateTimeOffset? BasTar { get; set; }
    public DateTimeOffset? BitTar { get; set; }

    /// <summary>Boş → servis <c>UtcNow</c> kullanır (create'te); update'te mevcut değer korunur.</summary>
    public DateTimeOffset? TalepTarihi { get; set; }

    /// <summary>null = karşılanmadı.</summary>
    public DateTimeOffset? KarsilamaTarihi { get; set; }

    public string? TeslimEden { get; set; }

    public Guid? ReservationId { get; set; }
    public Guid? QuotationId { get; set; }
}

/// <summary>Liste filtresi. Tüm alanlar opsiyonel; boş filtre = tüm kayıtlar.</summary>
public sealed class RezSartFilter
{
    public Guid? MusteriId { get; set; }
    /// <summary><c>TalepTarihi</c> alt sınırı (dahil).</summary>
    public DateTimeOffset? TarihBas { get; set; }
    /// <summary><c>TalepTarihi</c> üst sınırı (dahil — servis günün sonuna genişletir).</summary>
    public DateTimeOffset? TarihBit { get; set; }
    /// <summary><c>true</c> = yalnız karşılananlar, <c>false</c> = yalnız karşılanmayanlar,
    /// <c>null</c> = hepsi.</summary>
    public bool? Karsilandi { get; set; }
}
