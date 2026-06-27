using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Auditing;

/// <summary>
/// Denetim (audit) görüntüleme — yalnız Admin (kullanıcı yönetimiyle aynı yetki sınıfı).
/// Salt-okunur: AuditLog değişmez kayıtlardır (interceptor yazar).
/// </summary>
public sealed class AuditService(IAuditRepository repository, ICurrentUser currentUser)
{
    private readonly IAuditRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<PagedResult<AuditLog>> SearchAsync(AuditFilter filter, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers); // denetim = Admin işlevi
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 200) filter.PageSize = 30;
        return _repository.SearchAsync(filter, ct);
    }
}
