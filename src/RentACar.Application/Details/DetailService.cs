namespace RentACar.Application.Details;

/// <summary>Araç/cari 360° detay görünümleri (salt-okunur). DB erişimi repo'da.</summary>
public sealed class DetailService(IDetailRepository repository)
{
    private readonly IDetailRepository _repository = repository;

    public Task<VehicleDetailDto?> GetVehicleAsync(Guid vehicleId, CancellationToken ct = default)
        => _repository.GetVehicleDetailAsync(vehicleId, ct);

    public Task<CustomerDetailDto?> GetCustomerAsync(Guid customerId, CancellationToken ct = default)
        => _repository.GetCustomerDetailAsync(customerId, ct);
}
