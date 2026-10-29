using System.Globalization;
using RentACar.Application.Common;
using RentACar.Application.Fleet;

namespace RentACar.PublicSite;

/// <summary>
/// "WhatsApp'tan sor" bağlantısı: <c>wa.me</c> adresi + ziyaretçinin baktığı araç ve tarihlerle HAZIR mesaj. Sunucuda
/// kurulur (JS yok); numara normalizasyonu <see cref="PhoneLink"/>'te. Numara: firmanın WhatsApp numarası, yoksa
/// mobil telefonu (diğer WhatsApp düğmeleriyle aynı kural). Numara yoksa null → düğme basılmaz (T2).
/// </summary>
public static class WhatsAppAsk
{
    public static string? Link(FleetBranding? branding, string? vehicle,
        DateOnly? from = null, DateOnly? to = null, TimeOnly? fromTime = null, TimeOnly? toTime = null)
        => branding is null ? null
            : PhoneLink.Wa(string.IsNullOrWhiteSpace(branding.WhatsApp) ? branding.MobilTel : branding.WhatsApp,
                Message(vehicle, from, to, fromTime, toTime));

    /// <summary>
    /// "Merhaba, 12.11.2026 10:00 - 15.11.2026 10:00 tarihleri için Fiat Egea fiyatını öğrenmek istiyorum." Tarih
    /// yalnız İKİSİ de varsa yazılır, saat yalnız ikisi de varsa; araç adı yoksa "müsait araç ve fiyat" sorulur.
    /// </summary>
    public static string Message(string? vehicle, DateOnly? from = null, DateOnly? to = null,
        TimeOnly? fromTime = null, TimeOnly? toTime = null)
    {
        var name = string.IsNullOrWhiteSpace(vehicle) ? null : vehicle.Trim();
        if (from is not { } f || to is not { } t)
            return name is null
                ? "Merhaba, araç kiralama hakkında bilgi almak istiyorum."
                : $"Merhaba, {name} için fiyat bilgisi almak istiyorum.";

        var withTime = fromTime is not null && toTime is not null;
        var range = $"{Format(f, withTime ? fromTime : null)} - {Format(t, withTime ? toTime : null)}";
        return name is null
            ? $"Merhaba, {range} tarihleri için müsait araç ve fiyat bilgisi almak istiyorum."
            : $"Merhaba, {range} tarihleri için {name} fiyatını öğrenmek istiyorum.";
    }

    private static string Format(DateOnly day, TimeOnly? time)
        => day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
           + (time is { } t ? " " + t.ToString("HH:mm", CultureInfo.InvariantCulture) : "");
}
