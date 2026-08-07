using RentACar.Application.Authorization;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Jobs;

/// <summary>
/// Otomatik servis koşu günlüğü okuma servisi. Yalnız OKUMA — satırları arka plan işleri yazar
/// (<c>JobCalismaKaydedici</c>); buradan yazma/silme ucu bilinçli olarak YOK, günlük append-only.
/// </summary>
public sealed class JobCalismaLogService(IJobCalismaLogRepository repository, ICurrentUser currentUser)
{
    private readonly IJobCalismaLogRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<JobCalismaLog>> ListAsync(
        JobCalismaLogFilter? filter = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.ListAsync(filter, ct);
    }

    public Task<IReadOnlyList<JobCalismaLog>> SonKosularAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ViewReports);
        return _repository.SonKosularAsync(ct);
    }
}
