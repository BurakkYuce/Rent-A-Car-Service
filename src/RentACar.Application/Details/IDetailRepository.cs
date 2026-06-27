namespace RentACar.Application.Details;

/// <summary>
/// Salt-okunur 360° detay sorguları (araç/cari). Tenant izolasyonu query filter + RLS ile
/// otomatik. Bulunamazsa null.
/// </summary>
public interface IDetailRepository
{
    Task<VehicleDetailDto?> GetVehicleDetailAsync(Guid vehicleId, CancellationToken ct = default);
    Task<CustomerDetailDto?> GetCustomerDetailAsync(Guid customerId, CancellationToken ct = default);
}
