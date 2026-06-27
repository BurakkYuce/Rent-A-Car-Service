using RentACar.Domain.Entities;

namespace RentACar.Application.Users;

/// <summary>
/// Kullanıcı kalıcılığı — geçerli tenant'a kapsamlı. Users platform tablosu olduğundan
/// (global query filter yok) tenant filtresi BURADA açıkça uygulanır; DB tarafında RLS
/// yazma politikası ikinci savunma katmanıdır.
/// </summary>
public interface IUserRepository
{
    Task<IReadOnlyList<User>> ListAsync(CancellationToken ct = default);
    Task<User?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default);

    /// <summary>Kullanıcıyı geçerli tenant'a ekler (TenantId repo'da damgalanır).</summary>
    Task CreateAsync(User user, CancellationToken ct = default);

    /// <summary>Geçerli tenant'taki kullanıcıyı günceller (durum/parola). Tenant dışıysa false.</summary>
    Task<bool> UpdateAsync(Guid id, Action<User> apply, CancellationToken ct = default);
}
