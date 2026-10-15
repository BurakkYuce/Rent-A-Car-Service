using System.Text.RegularExpressions;

namespace RentACar.Application.Kabis;

/// <summary>
/// KABİS hata metninin kalıcılaştırılmadan / gösterilmeden önceki maskesi (saf, testli).
///
/// <para><b>Neden gerekli:</b> karşı sistem ya da HTTP katmanı hata cümlesinde gönderilen değeri geri yankılayabilir
/// ("12345678901 numaralı kişi bulunamadı"). Bu metin <c>KabisBildirimleri.SonHata</c>'ya, koşu günlüğüne ve
/// ekrana gider — oralarda kimlik numarası OLMAMALI (KVKK; denetim maskesi kuralının bu tablodaki karşılığı).</para>
///
/// <para>İki katman: (1) bu gönderimin yükündeki bilinen değerler (TC, pasaport, ehliyet no, şifre) birebir
/// <c>***</c> olur; (2) yedi ve daha uzun rakam dizileri (TC 11, telefon 10–11, pasaport gövdesi) genel olarak
/// maskelenir. Kısa hata kodları (ör. 1000, 5057) korunur.</para>
/// </summary>
public static partial class KabisErrorText
{
    public const int MaxLength = 512;
    public const string Mask = "***";

    [GeneratedRegex(@"\d{7,}")]
    private static partial Regex LongDigits();

    public static string? Sanitize(string? text, IEnumerable<string?>? knownSecrets = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text;
        if (knownSecrets is not null)
            foreach (var secret in knownSecrets
                         .Where(x => !string.IsNullOrWhiteSpace(x) && x!.Trim().Length >= 4)
                         .Select(x => x!.Trim())
                         .Distinct()
                         .OrderByDescending(x => x.Length)) // uzun önce: biri diğerinin parçasıysa artık kalmasın
                s = s.Replace(secret, Mask, StringComparison.OrdinalIgnoreCase);
        s = LongDigits().Replace(s, Mask);
        s = s.Trim();
        return s.Length <= MaxLength ? s : s[..(MaxLength - 3)] + "...";
    }
}
