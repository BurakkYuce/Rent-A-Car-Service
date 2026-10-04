using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.Users;

/// <summary>
/// Güvenlik F1 (savunma derinliği) — kullanıcı yönetimi yazımlarında aktörün yetkisi ÇEREZDEN değil DB'deki güncel
/// satırdan doğrulanır: satır pasifse ya da rolü oturumdakinden farklıysa (örn. rolü düşürülmüş Admin'in eski oturumu)
/// 403. Oturum damgası (çerez doğrulaması) bunu zaten keser; bu kontrol damga önbelleği bayatken ya da başka bir
/// kimlik yolunda (harici JWT) da Admin işlemlerini kapatır.
/// <para>Satır bulunamazsa (sistem/test kimliği) oturumdaki rol geçerlidir — gerçek oturumun satırı her zaman vardır
/// (kullanıcılar silinmez, pasifleştirilir).</para>
/// </summary>
internal static class FreshActor
{
    public const string StaleMessage = "Oturumunuzdaki yetki güncel değil; lütfen yeniden giriş yapın.";

    public static async Task RequireAsync(IUserRepository users, ICurrentUser currentUser, CancellationToken ct)
    {
        if (currentUser.UserId is not { } id) return;
        if (await users.FindAsync(id, ct) is not { } row) return;
        if (!row.IsActive || row.Rol != currentUser.Role) throw new NoPermissionException(StaleMessage);
    }
}
