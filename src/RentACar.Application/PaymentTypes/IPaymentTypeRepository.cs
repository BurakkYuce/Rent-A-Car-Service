using RentACar.Domain.Entities;

namespace RentACar.Application.PaymentTypes;

public interface IPaymentTypeRepository
{
    Task<IReadOnlyList<PaymentType>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PaymentType>> ListActiveAsync(CancellationToken ct = default);
    Task<PaymentType?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(PaymentType type, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<PaymentType> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
