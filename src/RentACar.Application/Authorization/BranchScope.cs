using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Rol bazlı şube kapsamı. Operatör YALNIZ atanmış şubesinin kayıtlarını görür; Admin/Yönetici/
/// Muhasebe ve şubesi atanmamış kullanıcılar tüm şubeleri görür.
/// FAZ 5-C2: kapsam artık FK-FARKINDALI — kilitlenme-önleyici GENİŞLETME: iki taraf da FK taşıyorsa
/// FK eşitliği YA DA metin eşitliği yeter (şube yeniden adlandırılınca metin kırılır, FK kurtarır;
/// FK'sız kayıt/eski oturum metin yoluyla aynen çalışır). Metin doğruluk-kaynağı kalır (interceptor
/// tasarımı); FK'nın metni tek başına EZMESİ C5'in koşullu işi.
/// </summary>
public static class BranchScope
{
    /// <summary>Etkin şube filtresi (C2): Unrestricted = tüm şubeler; aksi halde FK ve/veya metin sınırı.</summary>
    public readonly record struct BranchFilter(Guid? SubeId, string? SubeAd)
    {
        public bool Unrestricted => SubeId is null && SubeAd is null;
    }

    /// <summary>FK-farkındalı etkin filtre. Operatör dışı ve şubesiz (metin+FK ikisi de boş) → Unrestricted.</summary>
    public static BranchFilter EffectiveFilter(ICurrentUser user)
    {
        if (user.Role != UserRole.Operator) return default;
        var ad = string.IsNullOrWhiteSpace(user.AssignedBranch) ? null : user.AssignedBranch!.Trim();
        var id = user.AssignedBranchId;
        return ad is null && id is null ? default : new BranchFilter(id, ad);
    }

    /// <summary>Eski metin-tabanlı filtre — C3'e dek liste çağrıları bunu kullanır (delege; davranış aynı).</summary>
    public static string? Effective(ICurrentUser user) => EffectiveFilter(user).SubeAd;

    /// <summary>Tekil kayıt şube-kapsamı guard'ı (adversarial M3 + C2 FK-farkındalı).
    /// Geçiş kuralı: Unrestricted → serbest; (iki FK de dolu ∧ eşit) → geç (yeniden-adlandırma kurtarması);
    /// metin Ordinal-eşit → geç (mevcut davranış — FK'sız kayıt/claim'siz oturum); aksi red.
    /// NOT (bilinçli genişletme): FK-uyuşmaz ∧ metin-eşit de GEÇER — metin doğruluk-kaynağı; bu kombinasyon
    /// yalnız şube-yeniden-adlandırma/elle-SQL ile oluşabilir (interceptor FK'yı aynı metinden türetir).</summary>
    public static void RequireInScope(ICurrentUser user, Guid? kayitSubeId, string? kayitSubeOfis)
    {
        var scope = EffectiveFilter(user);
        if (scope.Unrestricted) return;
        if (scope.SubeId is Guid claimFk && kayitSubeId is Guid kayitFk && claimFk == kayitFk) return;
        if (scope.SubeAd is not null &&
            string.Equals(scope.SubeAd, kayitSubeOfis?.Trim(), StringComparison.Ordinal)) return;
        throw new ValidationException("Bu kayıt şube kapsamınız dışında.");
    }

    /// <summary>Eski 1-arg overload — mevcut çağrı yerleri C3/C4'te FK'lı overload'a taşınana dek delege.</summary>
    public static void RequireInScope(ICurrentUser user, string? kayitSubeOfis)
        => RequireInScope(user, kayitSubeId: null, kayitSubeOfis);
}
