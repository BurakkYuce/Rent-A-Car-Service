using RentACar.Domain.Entities;

namespace RentACar.Application.Availability;

/// <summary>
/// Müsaitlik sorgusu: verilen tarih aralığında kiralanabilir araçlar. Çakışan aktif kira
/// (Kirada) ve açık rezervasyon (Rezerv/Onayli) olan araçlar hariç tutulur. Tenant izolasyonu
/// alt katmanda (RLS) otomatik.
/// </summary>
public interface IAvailabilityRepository
{
    Task<IReadOnlyList<Vehicle>> GetAvailableAsync(
        DateTimeOffset from, DateTimeOffset to, string? grup, string? sube, CancellationToken ct = default);
}
