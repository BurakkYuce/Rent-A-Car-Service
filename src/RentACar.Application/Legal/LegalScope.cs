using RentACar.Application.Authorization;

namespace RentACar.Application.Legal;

/// <summary>
/// Kabul C-HUKUK + güvenlik F4 — hukuk dosyasının ŞUBE KAPSAMI, TEK kural (SPA ucu, liste export'u ve harici JWT API
/// aynı servis yolundan geçer). Dosyanın kendi şube kolonu yok; üst kaydı CARİDİR ve carinin işlem şubesi
/// (<c>IslemSubeId</c>) kapsamı belirler.
/// <list type="bullet">
/// <item>Kapsamsız kullanıcı (Admin/Yönetici/Muhasebe ya da şubesiz) → hepsi.</item>
/// <item>Carisiz dosya ya da işlem şubesi boş cari → şubesiz → herkes görür.</item>
/// <item>Cari kimliği var ama cari bu kiracıda yok → kapsamlıya KAPALI (hangi şubeye ait bilinmiyor;
/// <c>CrmScopeGuard.Visible</c> ilkesi).</item>
/// <item>Aksi halde <see cref="BranchScope.InScope"/>: FK'lar doluysa FK eşitliği, değilse şube ADI şube adıyla.</item>
/// </list>
/// </summary>
public static class LegalScope
{
    public static bool Visible(BranchScope.BranchFilter filter, Guid? customerId,
        IReadOnlyDictionary<Guid, LegalCustomerBranch> branches)
    {
        if (filter.Unrestricted || customerId is not { } id) return true;
        if (!branches.TryGetValue(id, out var b)) return false;
        return b.BranchId is not { } sid || BranchScope.InScope(filter, sid, b.BranchName);
    }
}
