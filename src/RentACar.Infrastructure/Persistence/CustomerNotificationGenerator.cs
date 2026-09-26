using Microsoft.EntityFrameworkCore;
using RentACar.Application.Notifications;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Müşteriye giden hatırlatmaları üretir + kuyrukta kalan mesajları yeniden dener (scheduler işi).
///
/// <para>Üretici deseni (repo kuralı): servis DEĞİL, <c>RunAsync(db, tenant, now, …)</c> — yetki
/// yüzeyi büyümez, job doğrudan-context yolundan tenant'a kapsanmış <c>db</c> ile çağırır.</para>
///
/// <para><b>Zaman dilimi:</b> "yarın çıkacaklar" ve "bugün dönecekler" TAKVİM günüdür ve müşterinin
/// takvimi yereldir — UTC gününe göre hesaplamak, akşam 21:00'den sonraki çıkışları bir gün kaydırırdı.
/// Bu yüzden gün sınırları İstanbul saatinde bulunup UTC'ye çevrilir — sorguya giden sınır DAİMA
/// Offset=0'dır (Npgsql timestamptz'ye başka ofset yazmaz; bkz. <c>VadeBildirimJobSaatDilimiTests</c>).</para>
///
/// <para><b>İdempotency:</b> anahtar GÜN bileşeni taşır (<c>iade-hatirlatma:{id}:2026-08-17</c>).
/// Gün olmadan, 3 günlük bir kiralamanın hatırlatması ilk gün gönderilip sonraki günler "zaten var"
/// diye atlanırdı; günle birlikte her takvim günü için en fazla bir mesaj çıkar.</para>
/// </summary>
public static class CustomerNotificationGenerator
{
    /// <summary>Kaç mesaj kuyruğa alındı/gönderildi (yeni + yeniden denenen).</summary>
    public static async Task<int> RunAsync(
        AppDbContext db, Guid tenantId, DateTimeOffset now, TimeZoneInfo tz,
        RentACar.Application.Common.ISecretProtector secrets,
        RentACar.Application.Integrations.IEmailSender email,
        RentACar.Application.Integrations.ISmsService sms,
        CancellationToken ct = default)
    {
        // Servis job'ın elindeki context üzerine kurulur (bkz. DogrudanMesajRepolari): şablon çözümü,
        // idempotency, deneme sayacı ve izin kuralı TEK yerde kalır — kopyalanmaz.
        var notification = new CustomerNotificationService(
            new DirectMessageRepository(db, tenantId),
            new RentACar.Application.Integrations.NotificationChannelService(
                new DirectSettingsRepository(db), secrets, email, sms),
            NullCurrentUser.Instance);

        // Gün sınırı İSTANBUL'da hesaplanır, DB'ye giden an UTC'dir. Npgsql 6+ timestamptz
        // parametresine yalnız Offset=0 yazar; eskiden sınırlar yerel ofsetle (+03:00) kurulup
        // sorguya veriliyordu → her koşuda ArgumentException, hatırlatmalar hiç üretilmedi.
        now = now.ToUniversalTime();
        var today = TenantDay.Day(now, tz);
        var todayStart = DayStartUtc(today, tz);
        var todayEnd = DayStartUtc(today.AddDays(1), tz);
        var tomorrowStart = todayEnd;
        var tomorrowEnd = DayStartUtc(today.AddDays(2), tz);
        var dayLabel = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var counter = 0;
        counter += await DeliveryReminderAsync(db, notification, tomorrowStart, tomorrowEnd, dayLabel, ct);
        counter += await ReturnReminderAsync(db, notification, todayStart, todayEnd, dayLabel, ct);
        counter += await RetryQueuedAsync(db, notification, now, ct);
        return counter;
    }

    /// <summary>Yerel takvim gününün 00:00'ı → UTC an (Offset=0). Ofset o GECE YARISI için çözülür
    /// (<c>OperasyonOzetUretici</c> ile aynı yol); dilimde yaz saati olsa bile gün sınırı kaymaz.</summary>
    private static DateTimeOffset DayStartUtc(DateOnly day, TimeZoneInfo tz)
    {
        var localStart = day.ToDateTime(TimeOnly.MinValue); // Kind=Unspecified → tz'nin duvar saati
        return new DateTimeOffset(localStart, tz.GetUtcOffset(localStart)).ToUniversalTime();
    }

    /// <summary>Yarın aracını teslim alacak müşterilere hatırlatma (rezervasyonlar).</summary>
    private static async Task<int> DeliveryReminderAsync(
        AppDbContext db, CustomerNotificationService notification,
        DateTimeOffset start, DateTimeOffset bit, string day, CancellationToken ct)
    {
        var candidates = await (
            from r in db.Reservations.AsNoTracking()
            where r.Durum == ReservationStatus.Rezerv && r.BasTar >= start && r.BasTar < bit
            join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id
            join v in db.Vehicles.AsNoTracking() on r.VehicleId equals v.Id into vs
            from v in vs.DefaultIfEmpty()
            select new Aday(
                r.Id, r.ReservationNo, r.BasTar, r.BitTar,
                c.Ad, c.Unvan, c.Email, c.CepTel, c.MailIzin, c.SmsIzin,
                v != null ? v.Plaka : null, v != null ? (v.Marka ?? "") + " " + (v.Tip ?? "") : null))
            .ToListAsync(ct);

        return await GonderAsync(notification, candidates, MessageType.TeslimHatirlatma,
            keyPrefix: "teslim-hatirlatma", day, sourceType: "Rezervasyon", ct);
    }

    /// <summary>Bugün aracını iade edecek müşterilere hatırlatma (açık kira sözleşmeleri).</summary>
    private static async Task<int> ReturnReminderAsync(
        AppDbContext db, CustomerNotificationService notification,
        DateTimeOffset start, DateTimeOffset bit, string day, CancellationToken ct)
    {
        var candidates = await (
            from k in db.Rentals.AsNoTracking()
            where k.Durum == RentalStatus.Kirada && k.BitTar >= start && k.BitTar < bit
            join c in db.Customers.AsNoTracking() on k.MusteriId equals c.Id
            join v in db.Vehicles.AsNoTracking() on k.VehicleId equals v.Id into vs
            from v in vs.DefaultIfEmpty()
            select new Aday(
                k.Id, k.SozlesmeNo, k.BasTar, k.BitTar,
                c.Ad, c.Unvan, c.Email, c.CepTel, c.MailIzin, c.SmsIzin,
                v != null ? v.Plaka : null, v != null ? (v.Marka ?? "") + " " + (v.Tip ?? "") : null))
            .ToListAsync(ct);

        return await GonderAsync(notification, candidates, MessageType.IadeHatirlatma,
            keyPrefix: "iade-hatirlatma", day, sourceType: "Kira", ct);
    }

    private static async Task<int> GonderAsync(
        CustomerNotificationService notification, IReadOnlyList<Aday> candidates, MessageType type,
        string keyPrefix, string day, string sourceType, CancellationToken ct)
    {
        var counter = 0;
        foreach (var a in candidates)
        {
            var values = a.Values();

            if (!string.IsNullOrWhiteSpace(a.Email))
            {
                await notification.GonderAsync(new MesajIstegi(
                    type, MessageChannel.Eposta, a.Email!, $"{keyPrefix}:{a.Id:N}:{day}",
                    values, sourceType, a.Id),
                    // KVKK: hatırlatma pazarlama DEĞİL, kurulmuş sözleşmenin ifasına ilişkin bilgidir.
                    // Yine de müşteri o kanalı AÇIKÇA kapattıysa (MailIzin=false) saygı gösterilir;
                    // "belirtilmemiş" (null) kapalı sayılmaz — sözleşme ilişkisi zaten var.
                    hasPermission: a.MailIzin != false, ct);
                counter++;
            }

            if (!string.IsNullOrWhiteSpace(a.Telefon))
            {
                await notification.GonderAsync(new MesajIstegi(
                    type, MessageChannel.Sms, a.Telefon!, $"{keyPrefix}-sms:{a.Id:N}:{day}",
                    values, sourceType, a.Id),
                    hasPermission: a.SmsIzin != false, ct);
                counter++;
            }
        }
        return counter;
    }

    /// <summary>Kuyrukta kalanları yeniden dener (geçici SMTP/SMS hatası, sonradan tanımlanan şablon).</summary>
    private static async Task<int> RetryQueuedAsync(
        AppDbContext db, CustomerNotificationService notification, DateTimeOffset now, CancellationToken ct)
    {
        var pending = await Repositories.MessageRepository.QueuedAsync(
            db, now, CustomerNotificationService.MaxAttempts, ct);
        foreach (var m in pending) await notification.TryAsync(m, ct);
        return pending.Count;
    }

    /// <summary>Sorgu projeksiyonu — müşteri/araç bilgisi mesaj yer tutucularına dönüşür.</summary>
    private sealed record Aday(
        Guid Id, string No, DateTimeOffset BasTar, DateTimeOffset BitTar,
        string? Ad, string? Unvan, string? Email, string? Telefon,
        bool? MailIzin, bool? SmsIzin, string? Plaka, string? Arac)
    {
        public Dictionary<string, string?> Values() => new()
        {
            ["MusteriAd"] = !string.IsNullOrWhiteSpace(Unvan) ? Unvan : Ad,
            ["No"] = No,
            ["Plaka"] = Plaka,
            ["Arac"] = Arac?.Trim(),
            ["CikisTarih"] = BasTar.ToString("dd.MM.yyyy HH:mm"),
            ["DonusTarih"] = BitTar.ToString("dd.MM.yyyy HH:mm"),
        };
    }
}
