using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Anket cevabı (FAZ-42) — anketin child satırı.
///
/// <para><b>NEDEN AYRI TABLO:</b> 8 soru düz kolon olarak eklenseydi soru metni ya da sayısı
/// değiştiğinde migration gerekirdi ve GEÇMİŞ anketlerin soruları da değişmiş görünürdü.
/// Soru metni cevapla BİRLİKTE saklanıyor (snapshot): anket formu güncellense de eski anket
/// hangi soruya ne cevap verildiğini olduğu gibi taşır.</para>
/// </summary>
public class AnketCevap : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid AnketId { get; set; }

    /// <summary>Soru sırası (1..N) — form sırasını korur.</summary>
    public int SoruNo { get; set; }

    /// <summary>Soru metni — SNAPSHOT (form değişse de geçmiş anket bozulmasın).</summary>
    public string Soru { get; set; } = string.Empty;
    public string? Cevap { get; set; }
    public string? Aciklama { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
