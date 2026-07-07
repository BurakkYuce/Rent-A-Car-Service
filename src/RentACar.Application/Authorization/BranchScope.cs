using RentACar.Application.Common;
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

    /// <summary>Tekil kayıt şube-kapsamı guard'ı (adversarial M3): liste filtresi kapsam uyguluyordu ama
    /// GetAsync(id) + write yolları uygulamıyordu → operatör başka şubenin kaydını ID ile okuyup değiştirebiliyordu.
    /// Kapsam null (Admin/şubesiz) → serbest; aksi halde kaydın Sube/CikisOfisi metni kapsama EŞİT olmalı (liste
    /// filtresiyle birebir metin-eşleşmesi). Tenant izolasyonu AYRI (RLS — sert sınır); bu tenant-içi yatay sınır.</summary>
    public static void RequireInScope(ICurrentUser user, string? kayitSubeOfis)
    {
        var scope = Effective(user);
        if (scope is not null && !string.Equals(scope, kayitSubeOfis?.Trim(), StringComparison.Ordinal))
            throw new ValidationException("Bu kayıt şube kapsamınız dışında.");
    }
}
