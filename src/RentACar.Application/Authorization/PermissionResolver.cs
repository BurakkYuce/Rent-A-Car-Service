using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Yetki çözümü (roadmap E3): rol-izin matrisi (FLOOR) ∧ ekran override (deny-by-default). SAF fonksiyon —
/// tek doğruluk kaynağı. Kurallar:
///   1. Matris izni (floor) ŞART — yoksa her durumda RED (override floor'u GENİŞLETEMEZ; yalnız sıkılaştırır).
///   2. Override YOKSA (null) → floor geçerli (mevcut davranış birebir korunur).
///   3. Override VARSA → rol listede olmalı (deny-by-default; listede değilse RED).
/// </summary>
public static class PermissionResolver
{
    public static bool IsAllowed(UserRole? role, Permission permission, IReadOnlyCollection<UserRole>? overrideRoles)
    {
        if (!RolePermissions.Has(role, permission)) return false; // floor şart
        if (overrideRoles is null) return true;                   // override yok → floor
        return role is { } r && overrideRoles.Contains(r);        // override var → deny-by-default
    }
}
