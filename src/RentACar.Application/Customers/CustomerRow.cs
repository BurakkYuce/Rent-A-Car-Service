using RentACar.Domain.Enums;

namespace RentACar.Application.Customers;

/// <summary>
/// Cari liste satırı: cari kimlik + CRM bayrakları + kira agregaları (adet/ciro/son kira).
/// Salt-okunur projeksiyon — Customer + Rentals(GROUP BY MusteriId) birleşimi (İptal hariç).
/// </summary>
public sealed class CustomerRow
{
    public Guid Id { get; init; }
    public CariType Tip { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? TcKimlik { get; init; }
    public string? VergiNo { get; init; }
    public string? CepTel { get; init; }
    public string? Email { get; init; }
    public string? Il { get; init; }
    public string? Kaynak { get; init; }

    public bool KaraListe { get; init; }
    public bool Pasif { get; init; }
    public bool Uyari { get; init; }
    public bool IysIzinli { get; init; }

    /// <summary>İptal olmayan kira adedi.</summary>
    public int KiraAdet { get; init; }
    /// <summary>İptal olmayan kiraların GenelToplam toplamı (ciro).</summary>
    public decimal Ciro { get; init; }
    public DateTimeOffset? SonKira { get; init; }
}
