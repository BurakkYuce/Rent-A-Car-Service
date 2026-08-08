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

    /// <summary>
    /// FAZ-19 — verilen araçlar için SON tamamlanmış kiranın efektif dönüş tarihi + müşteri adı.
    /// Yalnız GERÇEKTEN dönmüş kiralar (efektif dönüş geçmişte) sayılır; açık kira "boşta" değildir.
    /// Kayıt bulunmayan araç sonuçta YER ALMAZ (çağıran "hiç kiralanmamış" diye yorumlar).
    /// </summary>
    Task<IReadOnlyList<SonKullanimRow>> GetSonKullanimAsync(
        IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct = default);
}

/// <summary>Aracın son tamamlanmış kirası: efektif dönüş + müşteri adı (FAZ-19).</summary>
public sealed record SonKullanimRow(Guid VehicleId, DateTimeOffset SonDonus, string? MusteriAd);
