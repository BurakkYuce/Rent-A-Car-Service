using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Sabit rol → izin matrisi (tek doğruluk kaynağı). Hem servis guard'ları hem web rol-gating
/// bunu temel alır. Matris (kullanıcı kararıyla sabit roller):
///
///   İzin / Rol        Admin  Yonetici  Operator  Muhasebe
///   ManageUsers         ✓
///   OperationsWrite     ✓       ✓          ✓
///   FinanceWrite        ✓       ✓                     ✓
///   ViewReports         ✓       ✓                     ✓
/// </summary>
public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<Permission>> Map = new()
    {
        [UserRole.Admin] =
        [
            Permission.ManageUsers, Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports
        ],
        [UserRole.Yonetici] =
        [
            Permission.OperationsWrite, Permission.FinanceWrite, Permission.ViewReports
        ],
        [UserRole.Operator] =
        [
            Permission.OperationsWrite
        ],
        [UserRole.Muhasebe] =
        [
            Permission.FinanceWrite, Permission.ViewReports
        ]
    };

    public static bool Has(UserRole? role, Permission permission)
        => role is { } r && Map.TryGetValue(r, out var set) && set.Contains(permission);

    /// <summary>Bir izne sahip rollerin adları (web RequireRole gating için).</summary>
    public static string[] RolesWith(Permission permission)
        => Map.Where(kv => kv.Value.Contains(permission)).Select(kv => kv.Key.ToString()).ToArray();
}
