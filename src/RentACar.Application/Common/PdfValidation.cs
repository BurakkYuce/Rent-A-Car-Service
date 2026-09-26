namespace RentACar.Application.Common;

/// <summary>
/// PR-B — PDF magic-byte tespiti. <see cref="ImageValidation"/> ile aynı ilke: <b>client'ın
/// Content-Type header'ına GÜVENİLMEZ</b>, sunucu ham baytlardan karar verir. Uzantı da yeterli
/// değil — <c>.pdf</c> adıyla yüklenen bir yürütülebilir dosya tenant'lara dağıtılırdı.
/// </summary>
public static class PdfValidation
{
    /// <summary>PDF üst sınırı — kullanıcı kararı 3 MB (2026-08-17; önceki plan kararı 10 MB'tı).
    /// Gerekçe: belgeler bytea olarak DB'de yaşıyor ve yedek kapsamında — 10 yuva × 10 MB tenant
    /// başına 100 MB'a kadar şişebiliyordu; metin ağırlıklı sözleşme/çıktı PDF'leri için 3 MB bol.
    /// Sınır tarama kalitesi yüzünden dar gelirse tek sabit burada büyütülür (tüm tüketiciler
    /// — firma dokümanı, platform belgesi, uç kapıları — bu sabitten türetir).</summary>
    public const long MaxBytes = 3L * 1024 * 1024;

    /// <summary>PDF imzası: <c>%PDF-</c>. Sürüm numarası (1.4/1.7/2.0) kontrol EDİLMEZ — QuestPDF
    /// üretmiyoruz, yalnız saklayıp geri veriyoruz; tarayıcı hangi sürümü açacağını kendi bilir.</summary>
    public static bool IsValidPdf(byte[] b)
        => b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D;

    /// <summary>Reddedilmesi gerekiyorsa hata mesajı, uygunsa null.</summary>
    public static string? Reject(byte[] bytes)
    {
        if (bytes.Length == 0) return "Dosya boş.";
        if (bytes.Length > MaxBytes) return $"Dosya en fazla {MaxBytes / (1024 * 1024)} MB olabilir.";
        if (!IsValidPdf(bytes)) return "Yalnız PDF yüklenebilir.";
        return null;
    }

    /// <summary>Dosya adını ASCII'ye indirger — `Content-Disposition` başlığı ham ASCII bekler,
    /// Türkçe karakter bozulur. RFC 5987 encode etmek yerine adı sadeleştirmek daha az yer kırar.</summary>
    public static string SafeFileName(string name)
    {
        var floor = TurkishText.Slugify(Path.GetFileNameWithoutExtension(name));
        if (floor.Length == 0) floor = "belge";
        return floor.Length > 80 ? floor[..80] + ".pdf" : floor + ".pdf";
    }
}
