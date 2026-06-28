using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>Kira listesi filtreleri. Sube, servis tarafından rol bazlı şube kapsamına ayarlanır.</summary>
public sealed class RentalFilter
{
    /// <summary>Sözleşme no / müşteri adı / plaka içeren arama (case-insensitive).</summary>
    public string? Query { get; set; }
    public RentalStatus? Durum { get; set; }
    /// <summary>true → faturalı; false → faturasız; null → tümü.</summary>
    public bool? Faturali { get; set; }
    public DateTimeOffset? BaslangicMin { get; set; }
    public DateTimeOffset? BaslangicMax { get; set; }
    /// <summary>Çıkış veya dönüş ofisi eşleşmesi.</summary>
    public string? Ofis { get; set; }
    public string? Sube { get; set; }
}
