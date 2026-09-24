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

    /// <summary>
    /// F11.1b güvenlik M2 — ekleme + DENETİM kaydı (AuditLog) tek işlemde. Users IAuditable değil; kullanıcı yönetimi
    /// eylemleri açıkça denetlenir. Parola/hash denetime YAZILMAZ.
    /// </summary>
    Task CreateAsync(User user, UserAuditEntry audit, CancellationToken ct = default);

    /// <summary>
    /// F11.1b güvenlik M1/M2 — kiracı başına advisory kilit ALTINDA güncelleme + denetim kaydı. Uygulamadan sonra
    /// kiracıda aktif Admin kalmayacaksa <see cref="Common.ValidationException"/> ve hiçbir şey yazılmaz (eşzamanlı
    /// iki pasifleştirme sayımı ayrı ayrı geçemez). Tenant dışıysa false.
    /// </summary>
    Task<bool> UpdateAuditedAsync(Guid id, Action<User> apply, UserAuditEntry audit, CancellationToken ct = default);
}

/// <summary>F11.1b — kullanıcı yönetimi denetim satırının içeriği (işlem adı + parola içermeyen ayrıntı).</summary>
public sealed record UserAuditEntry(string Operation, string? Detail = null);
