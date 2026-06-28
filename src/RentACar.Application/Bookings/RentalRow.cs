using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>Kira listesi satırı: sözleşme + müşteri/araç adı + fatura durumu (salt-okunur projeksiyon).</summary>
public sealed class RentalRow
{
    public Guid Id { get; init; }
    public string SozlesmeNo { get; init; } = string.Empty;
    public string MusteriAd { get; init; } = string.Empty;
    public string Plaka { get; init; } = string.Empty;
    public DateTimeOffset BasTar { get; init; }
    public DateTimeOffset BitTar { get; init; }
    public int Gun { get; init; }
    public decimal Tutar { get; init; }
    public decimal Bakiye { get; init; }
    public RentalStatus Durum { get; init; }
    public bool Faturali { get; init; }
}
