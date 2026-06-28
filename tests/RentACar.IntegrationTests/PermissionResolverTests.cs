using RentACar.Application.Authorization;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// roadmap E3 — PermissionResolver (saf). BAĞIMSIZ ORACLE truth-table: floor (matris) ŞART; override yok→
/// floor; override var→deny-by-default; override floor'u GENİŞLETEMEZ. (Operatör OperationsWrite floor'a
/// sahip, Muhasebe değil.)
/// </summary>
public sealed class PermissionResolverTests
{
    [Fact]
    public void Floor_holds_when_no_override()
    {
        Assert.True(PermissionResolver.IsAllowed(UserRole.Operator, Permission.OperationsWrite, null));
        Assert.True(PermissionResolver.IsAllowed(UserRole.Admin, Permission.OperationsWrite, null));
        Assert.False(PermissionResolver.IsAllowed(UserRole.Muhasebe, Permission.OperationsWrite, null)); // floor yok
    }

    [Fact]
    public void Override_is_deny_by_default()
    {
        // Operatör floor'a sahip ama override listesinde değil → RED (sıkılaştırma).
        Assert.False(PermissionResolver.IsAllowed(UserRole.Operator, Permission.OperationsWrite, new[] { UserRole.Admin }));
        // Listede → izin (floor da var).
        Assert.True(PermissionResolver.IsAllowed(UserRole.Operator, Permission.OperationsWrite, new[] { UserRole.Operator, UserRole.Admin }));
        // Boş override → herkese RED (floor sahibi bile).
        Assert.False(PermissionResolver.IsAllowed(UserRole.Admin, Permission.OperationsWrite, Array.Empty<UserRole>()));
    }

    [Fact]
    public void Override_cannot_exceed_floor()
    {
        // Muhasebe override'da AMA OperationsWrite floor'u yok → RED.
        Assert.False(PermissionResolver.IsAllowed(UserRole.Muhasebe, Permission.OperationsWrite, new[] { UserRole.Muhasebe }));
    }

    [Fact]
    public void Null_role_denied()
        => Assert.False(PermissionResolver.IsAllowed(null, Permission.OperationsWrite, null));
}
