using RentACar.Domain.Common;

namespace RentACar.Application.Common;

/// <summary>
/// Kod+Ad(+Aktif) master tanım repo sözleşmesi — birebir-kopya IXRepository ailesinin ortak tabanı.
/// Somut arayüzler (IBrandRepository…) boş gövdeyle bundan türer; DI kayıtları ve tüketici yüzeyi değişmez.
/// </summary>
public interface IMasterTanimRepository<T> : IVersionedRepository<T> where T : class, IMasterTanim
{
    Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<T>> ListActiveAsync(CancellationToken ct = default);
    Task<T?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(T entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<T> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
