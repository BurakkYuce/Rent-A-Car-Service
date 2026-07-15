using RentACar.Domain.Entities;

namespace RentACar.Application.Availability;

/// <summary>
/// Müsaitlik sorgusu: verilen tarih aralığında kiralanabilir araçlar. Çakışan aktif kira
/// (Kirada) ve açık rezervasyon (Rezerv/Onayli) olan araçlar hariç tutulur. Tenant izolasyonu
/// alt katmanda (RLS) otomatik.
/// </summary>
public interface IAvailabilityRepository
{
    Task<IReadOnlyList<Vehicle>> GetAvailableAsync( // C3: kapsam FK-farkındalı, UI sube ayrı param
        DateTimeOffset from, DateTimeOffset to, string? grup, string? sube,
        Authorization.BranchScope.BranchFilter kapsam = default, CancellationToken ct = default);
}
