using RentACar.Domain.Entities;

namespace RentACar.Application.Authorization;

public interface IScreenPermissionRepository
{
    Task<IReadOnlyList<ScreenPermission>> ListAsync(CancellationToken ct = default);
    Task<ScreenPermission?> FindByCodeAsync(string screenCode, CancellationToken ct = default);
    Task UpsertAsync(string screenCode, Action<ScreenPermission> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(string screenCode, CancellationToken ct = default);
}
