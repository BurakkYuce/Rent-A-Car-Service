using RentACar.Domain.Entities;

namespace RentACar.Application.Notifications;

/// <summary>Kalıcı uygulama-içi bildirim deposu (vade uyarıları). Tenant izolasyonu RLS + query filter.</summary>
public interface INotificationRepository
{
    /// <summary>Bildirimler (okundu filtresi opsiyonel), en yeni önce.</summary>
    Task<IReadOnlyList<Bildirim>> ListAsync(bool? isRead = null, CancellationToken ct = default);

    /// <summary>Okunmamış bildirim sayısı (badge).</summary>
    Task<int> UnreadCountAsync(CancellationToken ct = default);

    /// <summary>Tek bildirimi okundu işaretle.</summary>
    Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tümünü okundu işaretle; işaretlenen sayı.</summary>
    Task<int> MarkAllReadAsync(CancellationToken ct = default);
}
