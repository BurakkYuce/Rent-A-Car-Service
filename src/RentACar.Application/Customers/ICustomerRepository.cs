using RentACar.Domain.Entities;

namespace RentACar.Application.Customers;

public interface ICustomerRepository
{
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct = default);

    /// <summary>Arama (ad/ünvan/TC/vergi) + sayfalama (liste ekranı).</summary>
    Task<Common.PagedResult<Customer>> SearchAsync(CustomerFilter filter, CancellationToken ct = default);

    /// <summary>Arama/filtre + sayfalama + kira agregaları (adet/ciro/son kira) — CRM liste ekranı.</summary>
    Task<Common.PagedResult<CustomerRow>> SearchRowsAsync(CustomerFilter filter, CancellationToken ct = default);

    Task<Customer?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>TC blind-index özeti tenant içinde başka kayıtta var mı? (KVKK/F2 — düz metin yok.)</summary>
    Task<bool> TcKimlikHashExistsAsync(string tcHash, Guid? excludeId = null, CancellationToken ct = default);

    Task<bool> VergiNoExistsAsync(string vergiNo, Guid? excludeId = null, CancellationToken ct = default);

    Task CreateAsync(Customer customer, CancellationToken ct = default);

    Task<bool> UpdateAsync(Guid id, Action<Customer> apply, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
