using RentACar.Application.Crm;
using RentACar.Application.Periods;
using RentACar.Application.Regulation;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Notifications;

/// <summary>Bildirim merkezi agrega (roadmap G6): vade uyarıları + açık şikayet sayısı/listesi + dönem durumu.</summary>
public sealed record BildirimDto(
    int VadeGecmis,
    int VadeYakin,
    IReadOnlyList<VadeItem> Vadeler,
    int AcikSikayet,
    IReadOnlyList<Sikayet> Sikayetler,
    DateTimeOffset? DonemKapanis);

/// <summary>
/// Bildirim merkezi (roadmap G6): MEVCUT servislerden salt-okur derleme — yeni tablo/entity YOK.
/// Vade uyarıları (sigorta/muayene/MTV), açık şikayetler, dönem kapanış durumu tek yerde toplanır.
/// </summary>
public sealed class InAppNotificationService(
    DueService due, ComplaintService complaint, PeriodLockService period, INotificationRepository notifications)
{
    private readonly DueService _due = due;
    private readonly ComplaintService _complaint = complaint;
    private readonly PeriodLockService _period = period;
    private readonly INotificationRepository _notifications = notifications;

    public async Task<BildirimDto> GetAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var warnings = await _due.GetWarningsAsync(now, ct);
        var history = warnings.Count(w => w.Bucket == DueBucket.Gecmis);
        var yakin = warnings.Count(w => w.Bucket != DueBucket.Gecmis);

        var open = (await _complaint.ListAsync(ct)).Where(s => s.Durum == ComplaintStatus.Acik).ToList();
        var closing = await _period.GetClosingDateAsync(ct);

        return new BildirimDto(history, yakin, warnings, open.Count, open, closing);
    }

    // ---- Kalıcı uygulama-içi bildirimler (scheduler üretir; kullanıcı okur/işaretler) ----
    public Task<IReadOnlyList<Bildirim>> ListPersistedAsync(bool? isRead = null, CancellationToken ct = default)
        => _notifications.ListAsync(isRead, ct);
    public Task<int> UnreadCountAsync(CancellationToken ct = default)
        => _notifications.UnreadCountAsync(ct);
    public Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default)
        => _notifications.MarkReadAsync(id, ct);
    public Task<int> MarkAllReadAsync(CancellationToken ct = default)
        => _notifications.MarkAllReadAsync(ct);
}
