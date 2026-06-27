using RentACar.Domain.Entities;

namespace RentACar.Application.Customers;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);

    Task<Customer?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Verilen alanda (TcKimlik/VergiNo) tenant içinde başka kayıt var mı?</summary>
    Task<bool> TcKimlikExistsAsync(string tcKimlik, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> VergiNoExistsAsync(string vergiNo, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Customer customer, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Customer> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
