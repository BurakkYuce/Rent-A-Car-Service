using System.Security.Cryptography;

namespace RentACar.Web.Calendar;

/// <summary>iCal feed token'ı: 32-byte CSPRNG → Base64Url (tahmin edilemez; feed müşteri adı/plaka içerir).</summary>
public static class CalendarTokenUtil
{
    public static string New()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
