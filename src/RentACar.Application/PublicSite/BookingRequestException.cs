using RentACar.Application.Common;

namespace RentACar.Application.PublicSite;

/// <summary>
/// Site talep formunun reddi. Adrese (<c>?hata=</c>) yalnız <see cref="Code"/> yazılır: serbest metin adreste
/// taşınsaydı herkes firmanın sayfasına istediği cümleyi bastıran bir bağlantı üretebilirdi (içerik sahteciliği).
/// Sayfa metni <see cref="MessageFor"/> ile bu tablodan basar; bilinmeyen kod genel metne düşer.
/// Kodlar URL sözleşmesidir — değiştirilmez.
/// </summary>
public sealed class BookingRequestException(string code) : ValidationException(MessageFor(code))
{
    public const string NameRequired = "ad_soyad";
    public const string PhoneRequired = "telefon";
    public const string EndBeforeStart = "tarih_sirasi";
    public const string StartInPast = "tarih_gecmis";
    public const string StartTooFar = "tarih_ileri";
    public const string TooLong = "sure_uzun";
    public const string InvalidTime = "saat_bicim";
    public const string Generic = "gecersiz";

    private static readonly Dictionary<string, string> Messages = new()
    {
        [NameRequired] = "Ad soyad zorunludur.",
        [PhoneRequired] = "Telefon zorunludur.",
        [EndBeforeStart] = "İade tarihi alış tarihinden sonra olmalıdır.",
        [StartInPast] = "Alış zamanı geçmiş olamaz; bugün ya da ileri bir tarih ve saat seçin.",
        [StartTooFar] = "Alış tarihi en fazla 1 yıl ileri olabilir.",
        [TooLong] = $"Kiralama süresi en fazla {DatePolicy.MaxRentalYears} yıl olabilir.",
        [InvalidTime] = "Saat biçimi geçersiz; saati SS:dd olarak girin (ör. 10:00).",
        [Generic] = "Talebiniz gönderilemedi; bilgileri kontrol edip yeniden deneyin.",
    };

    public string Code { get; } = code;

    public static string MessageFor(string? code)
        => code is not null && Messages.TryGetValue(code, out var message) ? message : Messages[Generic];
}
