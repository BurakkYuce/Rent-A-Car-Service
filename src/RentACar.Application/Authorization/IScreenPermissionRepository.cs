using RentACar.Domain.Entities;

namespace RentACar.Application.Authorization;

public interface IScreenPermissionRepository
{
    Task<IReadOnlyList<ScreenPermission>> ListAsync(CancellationToken ct = default);
    Task<ScreenPermission?> FindByKodAsync(string ekranKodu, CancellationToken ct = default);
    Task UpsertAsync(string ekranKodu, Action<ScreenPermission> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(string ekranKodu, CancellationToken ct = default);
}
