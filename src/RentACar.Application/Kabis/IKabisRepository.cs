using RentACar.Application.Authorization;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Kabis;

/// <summary>
/// KABİS kuyruk deposu. İki uygulaması var: istek yolunda <c>KabisRepository</c> (kısa ömürlü context'ler) ve
/// arka plan işinde <c>DirectKabisRepository</c> (job'ın tenant'a kapsanmış tek context'i). Sorgular ortak
/// (<c>KabisQueries</c>) — mantık kopyalanmaz.
/// </summary>
public interface IKabisRepository
{
    /// <summary>Gönderim yükünün kaynağı (kira + araç + cari/2. sürücü cipher alanları); kira yoksa null.</summary>
    Task<KabisRentalSource?> LoadSourceAsync(Guid rentalId, CancellationToken ct = default);

    Task<KabisNotification?> FindAsync(Guid rentalId, KabisNoticeType type, CancellationToken ct = default);

    Task<KabisNotification?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<KabisNotification>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default);

    /// <summary>Ekler; aynı (kira, tür) zaten varsa (benzersiz index) false döner — idempotent no-op.</summary>
    Task<bool> AddAsync(KabisNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Satırı ATOMİK olarak gönderim için sahiplenir (tek UPDATE … WHERE): yalnız <c>Bekliyor</c> ve sürmekte olan bir
    /// gönderimi olmayan (<c>SahiplenmeBitisUtc</c> boş ya da geçmiş) satırda <c>SonDenemeUtc = now</c>,
    /// <c>SahiplenmeBitisUtc = now + lease</c> yazar ve true döner. İki replika/iki istek aynı satırı aynı anda
    /// gönderemez (biri 0 satır günceller). Sonuç yazılırken sahiplenme temizlenir.
    /// </summary>
    /// <param name="force">false (iş): ayrıca vadesi gelmiş olmalı (<c>SonrakiDenemeUtc</c> null ya da ≤ now).
    /// true (operatör "şimdi dene" / engelleyici teslim): geri çekilme beklenmez.</param>
    Task<bool> TryClaimAsync(Guid id, DateTimeOffset now, TimeSpan lease, bool force, CancellationToken ct = default);

    Task UpdateAsync(Guid id, Action<KabisNotification> apply, CancellationToken ct = default);

    /// <summary>Vadesi gelmiş bekleyen kayıtların kimlikleri (en eskiler önce).</summary>
    Task<IReadOnlyList<Guid>> DueAsync(DateTimeOffset now, int max, CancellationToken ct = default);

    /// <summary>İptal edilmiş kiralarda uzlaştırma adayları: (kira, Kiralama kaydı durumu, Iptal kaydı var mı).</summary>
    Task<IReadOnlyList<(Guid KiraId, KabisNoticeStatus KiralamaDurum, bool IptalVar)>> CancelledRentalCandidatesAsync(
        CancellationToken ct = default);

    /// <summary>Bekliyor/Hata kayıtları (kira + plaka ile), şube kapsamlı.</summary>
    Task<IReadOnlyList<KabisProblemRow>> ListProblemsAsync(BranchScope.BranchFilter scope, int max, CancellationToken ct = default);

    /// <summary>Bekliyor/Hata kaydı olan DİSTİNCT kira sayısı, şube kapsamlı.</summary>
    Task<int> CountProblemRentalsAsync(BranchScope.BranchFilter scope, CancellationToken ct = default);
}
