using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Rol bazlı şube kapsamı. Operatör YALNIZ atanmış şubesinin kayıtlarını görür; Admin/Yönetici/
/// Muhasebe ve şubesi atanmamış kullanıcılar tüm şubeleri görür.
/// FAZ 5-C2: kapsam FK-FARKINDALI. FAZ 5-C5 (daraltma): iki taraf da FK taşıyorsa FK TEK BAŞINA
/// karar verir — metin OR'u o vaka için düşer (F2 kapanışı: yeniden-adlandırma çakışmasında
/// metin-eşleşme sızıntısı biter). FK'sız kayıt / claim'siz eski oturum için metin yolu KALICI son
/// durumdur (interceptor tasarımı gereği — eşlenmemiş serbest-metin şube hep olabilir).
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

    /// <summary>Tekil kayıt şube-kapsamı guard'ı (adversarial M3 + C2 FK-farkındalı + C5 daraltma).
    /// Geçiş kuralı: Unrestricted → serbest; iki FK de doluysa FK eşitliği TEK BAŞINA karar verir
    /// (yeniden-adlandırma kurtarması kalır, FK-uyuşmaz∧metin-eşit sızıntısı [F2] kapanır);
    /// FK'lardan biri boşsa metin Ordinal-eşit → geç (FK'sız kayıt/claim'siz oturum); aksi red.</summary>
    public static void RequireInScope(ICurrentUser user, Guid? kayitSubeId, string? kayitSubeOfis)
    {
        if (!InScope(EffectiveFilter(user), kayitSubeId, kayitSubeOfis))
            throw new ValidationException("Bu kayıt şube kapsamınız dışında.");
    }

    /// <summary>TEK kural (C3): guard, bellek-içi liste filtresi ve SQL şablonu hep bundan türetilir.
    /// C5: iki FK de dolu → FK eşitliği tek başına; aksi halde metin-eşit (Ordinal). Unrestricted → true.
    /// Ön koşul ampirik doğrulandı (2026-07-15, dev DB): FK-uyuşmaz∧metin-eşleşir sayacı 0
    /// (interceptor/backfill FK'yı AYNI metinden türettiğinden yapısal; yalnız rename-çakışması üretir).</summary>
    public static bool InScope(BranchFilter f, Guid? kayitSubeId, string? kayitMetin)
    {
        if (f.Unrestricted) return true;
        if (f.SubeId is Guid cid && kayitSubeId is Guid kid) return cid == kid;
        return f.SubeAd is not null &&
               string.Equals(f.SubeAd, kayitMetin?.Trim(), StringComparison.Ordinal);
    }

    /// <summary>Ham User alanlarından metin kapsamı (ICS feed gibi ICurrentUser'sız yollar için —
    /// CalendarFeedService'teki el-klonunu kaldırır; kural TEK yerde).</summary>
    public static string? EffectiveText(UserRole? rol, string? atanmisSube)
        => rol == UserRole.Operator && !string.IsNullOrWhiteSpace(atanmisSube) ? atanmisSube!.Trim() : null;

    /// <summary>Eski 1-arg overload — mevcut çağrı yerleri C3/C4'te FK'lı overload'a taşınana dek delege.</summary>
    public static void RequireInScope(ICurrentUser user, string? kayitSubeOfis)
        => RequireInScope(user, kayitSubeId: null, kayitSubeOfis);
}
