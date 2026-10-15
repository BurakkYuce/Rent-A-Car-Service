using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Integrations;
using RentACar.Application.Kabis;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence.Repositories;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// KABİS kuyruğunun arka plan adımı: vadesi gelmiş bekleyen bildirimleri gönderir ve iptal edilmiş kiraları uzlaştırır.
///
/// <para>Üretici deseni (repo kuralı): servis DEĞİL, <c>RunAsync(db, tenant, now, …)</c> — yetki yüzeyi büyümez; mantık
/// <see cref="KabisNotificationService"/>'te TEK yerde, burada job'ın tenant'a kapsanmış context'i üzerine kurulur
/// (<see cref="CustomerNotificationGenerator"/> ile aynı yol).</para>
///
/// <para><b>PII:</b> bu adım log YAZMAZ; servis hata metnini maskeleyip satıra yazar. Adımın kendisi bir istisna
/// fırlatırsa (DB arızası) koşu günlüğüne giden metin DB hatasıdır — kuyruk tablosunda kimlik verisi bulunmadığı için
/// orada da PII olamaz.</para>
/// </summary>
public static class KabisNotificationGenerator
{
    public static Task<int> RunAsync(
        AppDbContext db, Guid tenantId, DateTimeOffset now, IKabisService port, ISecretProtector secrets,
        CancellationToken ct = default)
        => Service(db, tenantId, port, secrets).RunDueAsync(now, ct);

    /// <summary>Ucuz ön kontrol: kapı açık ve yapılacak iş var mı (koşu günlüğü yalnız iş varken yazılsın).</summary>
    public static Task<bool> HasWorkAsync(AppDbContext db, DateTimeOffset now, CancellationToken ct = default)
        => KabisQueries.HasWorkAsync(db, now, ct);

    public static KabisNotificationService Service(AppDbContext db, Guid tenantId, IKabisService port, ISecretProtector secrets)
        => new(new DirectKabisRepository(db, tenantId), new DirectSettingsRepository(db), port, secrets, NullCurrentUser.Instance);
}

/// <summary>Job yolunda KABİS deposu: elindeki (tenant'a kapsanmış) context üzerinden ortak sorguları çağırır.</summary>
public sealed class DirectKabisRepository(AppDbContext db, Guid tenantId) : IKabisRepository
{
    public Task<KabisRentalSource?> LoadSourceAsync(Guid rentalId, CancellationToken ct = default)
        => KabisQueries.LoadSourceAsync(db, rentalId, ct);

    public Task<KabisNotification?> FindAsync(Guid rentalId, KabisNoticeType type, CancellationToken ct = default)
        => KabisQueries.FindAsync(db, rentalId, type, ct);

    public Task<KabisNotification?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => KabisQueries.FindByIdAsync(db, id, ct);

    public Task<IReadOnlyList<KabisNotification>> ListForRentalAsync(Guid rentalId, CancellationToken ct = default)
        => KabisQueries.ListForRentalAsync(db, rentalId, ct);

    public async Task<bool> AddAsync(KabisNotification notification, CancellationToken ct = default)
    {
        notification.TenantId = tenantId; // interceptor'sız job yolu → açık damga (DirectMessageRepository deseni)
        var added = await KabisQueries.AddAsync(db, notification, ct);
        db.ChangeTracker.Clear();
        return added;
    }

    public Task<bool> TryClaimAsync(Guid id, DateTimeOffset now, TimeSpan lease, bool force, CancellationToken ct = default)
        => KabisQueries.TryClaimAsync(db, id, now, lease, force, ct);

    public async Task UpdateAsync(Guid id, Action<KabisNotification> apply, CancellationToken ct = default)
    {
        await KabisQueries.UpdateAsync(db, id, apply, ct);
        // Sahiplenme ExecuteUpdate ile (izleyici dışında) yazılıyor; izlenen bayat kopya bir sonraki güncellemede
        // geri okunmasın diye izleyici boşaltılır.
        db.ChangeTracker.Clear();
    }

    public Task<IReadOnlyList<Guid>> DueAsync(DateTimeOffset now, int max, CancellationToken ct = default)
        => KabisQueries.DueAsync(db, now, max, ct);

    public Task<IReadOnlyList<(Guid KiraId, KabisNoticeStatus KiralamaDurum, bool IptalVar)>> CancelledRentalCandidatesAsync(
        CancellationToken ct = default)
        => KabisQueries.CancelledCandidatesAsync(db, ct);

    public Task<IReadOnlyList<KabisProblemRow>> ListProblemsAsync(BranchScope.BranchFilter scope, int max, CancellationToken ct = default)
        => KabisQueries.ListProblemsAsync(db, scope, max, ct);

    public Task<int> CountProblemRentalsAsync(BranchScope.BranchFilter scope, CancellationToken ct = default)
        => KabisQueries.CountProblemRentalsAsync(db, scope, ct);
}
