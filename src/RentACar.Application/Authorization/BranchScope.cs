using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Rol bazlı şube kapsamı. Operatör YALNIZ atanmış şubesinin (Sube metni) kayıtlarını görür;
/// Admin/Yönetici/Muhasebe ve şubesi atanmamış kullanıcılar tüm şubeleri görür.
/// </summary>
public static class BranchScope
{
    /// <summary>Etkin şube filtresi: null = tüm şubeler; aksi halde bu Sube metnine sınırla.</summary>
    public static string? Effective(ICurrentUser user)
        => user.Role == UserRole.Operator && !string.IsNullOrWhiteSpace(user.AssignedBranch)
            ? user.AssignedBranch!.Trim()
            : null;
}
