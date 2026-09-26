using RentACar.Application.Common;

namespace RentACar.Web.Calendar;

/// <summary>iCal feed token'ı: 32-byte CSPRNG → Base64Url (tahmin edilemez; feed müşteri adı/plaka içerir).
/// PR-C: üretim kuralı <see cref="SecureToken"/>'a taşındı — sözleşme paylaşım linki AYNI üreticiyi
/// kullanıyor ve kopyalanan kripto, birinde düzeltilip diğerinde kalan zafiyet üretir.</summary>
public static class CalendarTokenUtil
{
    public static string New() => SecureToken.Generate();
}
