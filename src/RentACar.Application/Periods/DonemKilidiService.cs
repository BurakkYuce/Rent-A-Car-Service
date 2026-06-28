using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.Periods;

/// <summary>
/// Dönem kapanışı iş mantığı (roadmap D2) + <see cref="IPeriodLockGuard"/> uygulaması. Kilitle/aç →
/// FinanceWrite (mali yetki). Guard (EnsureOpenAsync) yetki gerektirmez — postlama yollarınca çağrılır.
/// </summary>
public sealed class DonemKilidiService(
    IDonemKilidiRepository repository, ICurrentUser currentUser, Authorization.ScreenPermissionService screens) : IPeriodLockGuard
{
    private readonly IDonemKilidiRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly Authorization.ScreenPermissionService _screens = screens; // roadmap F3

    /// <summary>Geçerli kapanış tarihi (UI + guard).</summary>
    public async Task<DateTimeOffset?> GetClosingDateAsync(CancellationToken ct = default)
        => (await _repository.GetAsync(ct))?.KapanisTarihi;

    public async Task EnsureOpenAsync(DateTimeOffset entryDateUtc, CancellationToken ct = default)
        => PeriodLock.ThrowIfClosed(entryDateUtc, await GetClosingDateAsync(ct));

    /// <summary>Dönemi kapat: verilen tarih ve öncesini kilitle. İleri kaydırma serbest; geri alma "aç" ile.</summary>
    public async Task LockAsync(DateTimeOffset kapanisTarihi, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        await _screens.EnsureScreenAccessAsync("donem-kapanis", Permission.FinanceWrite, ct);
        await _repository.UpsertAsync(k =>
        {
            k.KapanisTarihi = kapanisTarihi;
            k.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Kilidi kaldır (tüm dönemler açık).</summary>
    public async Task UnlockAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        await _screens.EnsureScreenAccessAsync("donem-kapanis", Permission.FinanceWrite, ct);
        await _repository.UpsertAsync(k =>
        {
            k.KapanisTarihi = null;
            k.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
