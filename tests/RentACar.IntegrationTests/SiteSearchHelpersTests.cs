using System.Globalization;
using RentACar.Application.Fleet;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// V0 — arama formunun saf yardımcıları (<see cref="SiteDate"/>) ve "WhatsApp'tan sor" bağlantısı
/// (<see cref="WhatsAppAsk"/>). Tarihler bugünden göreli; beklenen günler ve metinler elle kuruldu (İstanbul UTC+3,
/// 2016'dan beri yaz saati yok).
/// </summary>
public sealed class SiteSearchHelpersTests
{
    private static readonly DateOnly UtcToday = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset UtcAt(int hour, int minute)
        => new(UtcToday.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    [Fact]
    public void Form_dates_default_to_istanbul_tomorrow_plus_three_days()
    {
        // 08:00 UTC = 11:00 İstanbul, aynı gün → yarın UTC gününün ertesi.
        Assert.Equal((Iso(UtcToday.AddDays(1)), Iso(UtcToday.AddDays(4))), SiteDate.FormDates(null, null, UtcAt(8, 0)));
        // 22:30 UTC = 01:30 İstanbul, ERTESİ gün → "yarın" UTC'ye göre iki gün sonra.
        Assert.Equal((Iso(UtcToday.AddDays(2)), Iso(UtcToday.AddDays(5))), SiteDate.FormDates("", " ", UtcAt(22, 30)));
    }

    [Fact]
    public void Form_dates_keep_given_values_and_derive_missing_return()
    {
        var start = Iso(UtcToday.AddDays(20));
        var end = Iso(UtcToday.AddDays(27));
        Assert.Equal((start, end), SiteDate.FormDates(start, end, UtcAt(8, 0)));
        Assert.Equal((start, Iso(UtcToday.AddDays(23))), SiteDate.FormDates(start, null, UtcAt(8, 0)));
        Assert.Equal((Iso(UtcToday.AddDays(1)), Iso(UtcToday.AddDays(4))), SiteDate.FormDates("31.12.2099", "x", UtcAt(8, 0)));
    }

    [Theory]
    [InlineData("10:00", true)]
    [InlineData("00:30", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("9:00", false)]
    [InlineData("10:00:00", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Time_is_strict_hh_mm(string? value, bool valid) => Assert.Equal(valid, SiteDate.ParseTime(value) is not null);

    [Fact]
    public void Istanbul_wall_clock_maps_to_utc_minus_three()
    {
        var day = UtcToday.AddDays(10);
        var at = SiteDate.At(day, new TimeOnly(10, 0));
        Assert.Equal(day.ToDateTime(new TimeOnly(7, 0)), at.UtcDateTime);
        Assert.Equal(day, DateOnly.FromDateTime(at.Date)); // ekranda İstanbul günü
        Assert.Equal(day.AddDays(-1).ToDateTime(new TimeOnly(22, 30)), SiteDate.At(day, new TimeOnly(1, 30)).UtcDateTime);
    }

    [Fact]
    public void Whatsapp_message_carries_vehicle_dates_and_times()
    {
        var from = UtcToday.AddDays(12);
        var to = UtcToday.AddDays(15);
        string D(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        Assert.Equal($"Merhaba, {D(from)} 10:00 - {D(to)} 14:30 tarihleri için Fiat Egea fiyatını öğrenmek istiyorum.",
            WhatsAppAsk.Message(" Fiat Egea ", from, to, new TimeOnly(10, 0), new TimeOnly(14, 30)));
        // Tek saat yazılmaz; tarih yalnız ikisi de varsa.
        Assert.Equal($"Merhaba, {D(from)} - {D(to)} tarihleri için Fiat Egea fiyatını öğrenmek istiyorum.",
            WhatsAppAsk.Message("Fiat Egea", from, to, new TimeOnly(10, 0)));
        Assert.Equal($"Merhaba, {D(from)} - {D(to)} tarihleri için müsait araç ve fiyat bilgisi almak istiyorum.",
            WhatsAppAsk.Message(null, from, to));
        Assert.Equal("Merhaba, Fiat Egea için fiyat bilgisi almak istiyorum.", WhatsAppAsk.Message("Fiat Egea", from));
        Assert.Equal("Merhaba, araç kiralama hakkında bilgi almak istiyorum.", WhatsAppAsk.Message(""));
    }

    [Fact]
    public void Whatsapp_link_uses_whatsapp_number_then_mobile_and_is_absent_without_number()
    {
        var from = UtcToday.AddDays(3);
        var to = UtcToday.AddDays(6);
        var url = WhatsAppAsk.Link(new FleetBranding("Kordon", null, "0232 555 41 20", null, "0533 000 11 22", "0532 123 45 67"),
            "Fiat Egea", from, to);
        Assert.StartsWith("https://wa.me/905321234567?text=", url);
        Assert.Equal(WhatsAppAsk.Message("Fiat Egea", from, to), Uri.UnescapeDataString(url!.Split("?text=")[1]));

        Assert.StartsWith("https://wa.me/905330001122?text=",
            WhatsAppAsk.Link(new FleetBranding("Kordon", null, null, null, "0533 000 11 22", " "), "Fiat Egea"));
        // Ofis telefonu WhatsApp sayılmaz; numara yoksa düğme yok.
        Assert.Null(WhatsAppAsk.Link(new FleetBranding("Kordon", null, "0232 555 41 20", null), "Fiat Egea"));
        Assert.Null(WhatsAppAsk.Link(null, "Fiat Egea"));
    }
}
