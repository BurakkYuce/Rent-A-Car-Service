using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Araç fotoğraf galerisi (PR-3, halka açık site). Vehicle'ın İLK child collection'ı — nav property
/// EKLENMEZ (VehicleKmLog deseni: repository VehicleId ile doğrudan sorgular). Kapak fotoğrafı ayrı bir
/// alan DEĞİL — en küçük <see cref="Sira"/> olan satır kapak sayılır.
/// </summary>
public class VehiclePhoto : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid VehicleId { get; set; }

    /// <summary>Görüntüleme sırası. Guid PK insertion order garanti etmediği için ayrı kolon (CustomerContact
    /// deseni). Silme sonrası boşluk kabul edilir; MoveAsync komşuyu DEĞERLE bulur (index değil).</summary>
    public int Sira { get; set; }

    public byte[] Bytes { get; set; } = [];

    /// <summary>"image/png" | "image/jpeg" | "image/webp" — sunucu magic-byte'tan belirler, client
    /// Content-Type header'ına GÜVENİLMEZ.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Küçük resim (uzun kenar 400px, JPEG q80, EXIF-döndürülmüş, EXIF'siz re-encode — GPS gibi
    /// EXIF meta-veri sızmaz). Üretim başarısızsa null — serve ucu tam boy görsele düşer, upload ASLA
    /// bloklanmaz.</summary>
    public byte[]? ThumbBytes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
