namespace RentACar.Application.Common;

/// <summary>PR-A logo değerlendirmesi. <paramref name="Uyari"/> null değilse kullanıcıya gösterilir
/// ama yükleme ENGELLENMEZ; <paramref name="BaskiyaUygun"/> false ise PDF logoyu YOK SAYAR.</summary>
public sealed record LogoDegerlendirme(int Bayt, int? Genislik, int? Yukseklik, bool BaskiyaUygun, string? Uyari);

/// <summary>
/// PR-A — logo kabul kuralları TEK yerde. Hem tenant yolu (`/ayarlar/logo`) hem platform yolu
/// (`/platform/tenants/{id}/logo`) buradan geçer; iki kopya kural olursa iki panel farklı davranır.
///
/// <para><b>Neden PNG-only:</b> logo sözleşmenin beyaz zeminine oturuyor — şeffaflık gerekiyor.
/// JPEG teknik olarak çalışıyordu, bu bir <b>kısıtlama</b> (düzeltme değil); mevcut tek yüklü logo
/// PNG olduğu için kimseyi kırmıyor.</para>
///
/// <para><b>Neden "absürt küçük" YOK SAYILIYOR:</b> logo baytı var olduğu sürece PDF'in metin
/// fallback'i (firma adı) devreye girmez — 1×1 piksellik bir dosya sözleşme başlığına gerilmiş bir
/// leke basar ve bu çıktı MÜŞTERİYE gider. Uyarı yetmez, çünkü uyarıyı gören personel, lekeyi gören
/// müşteri. Bilinmeyen-kötü yerine bilinen-iyi davranışa dönülür.</para>
/// </summary>
public static class LogoValidationRules
{
    /// <summary>Bu genişliğin altındaki logo hiç basılmaz (metin fallback'i devralır).</summary>
    public const int IgnoredWidth = 50;
    /// <summary>Bunun altında basılır ama baskıda bulanık olacağı uyarısı verilir.</summary>
    public const int RecommendedWidth = 600;
    public const int MaxWidth = 2000;
    /// <summary>Yükseklik sınırı. Yalnız genişlik denetlenirken 2000×1.000.000 boyutlu, 1-bit gri, ~244 KB'lık
    /// bir PNG kabul ediliyordu — çözülünce ~1,4 GB bellek tutan bir dekompresyon bombası. İki kenar da
    /// sınırlı olunca çözülmüş görüntü en fazla 2000×2000×4 = 16 MB.</summary>
    public const int MaxHeight = 2000;
    public const int MaxBytes = 1_048_576; // 1 MB — mevcut SetLogoAsync sınırı korunuyor

    /// <summary>Reddedilmesi gerekiyorsa hata mesajı, uygunsa null. Tür + boyut + ölçü kapısı.
    /// Ölçüsü OKUNAMAYAN dosya reddedilir: boyutu bilinmeyen bir görüntüyü çözmenin maliyeti de bilinmez
    /// (imzadan sonra çöp taşıyan dosya da bu yoldan düşer).</summary>
    public static string? Reject(byte[] bytes)
    {
        if (bytes.Length == 0) return null; // boş = "logoyu kaldır", çağıran ayrı ele alır
        if (bytes.Length > MaxBytes) return "Logo en fazla 1 MB olabilir.";
        if (ImageValidation.Detect(bytes) != ImageKind.Png)
            return "Logo yalnız PNG olabilir (şeffaf zemin gerekiyor).";
        if (PngSize.Read(bytes) is not { } b)
            return "Logo ölçüleri okunamadı; dosya bozuk görünüyor. Dosyayı yeniden kaydedip deneyin.";
        if (b.Genislik > MaxWidth)
            return $"Logo genişliği en fazla {MaxWidth} piksel olabilir (yüklenen: {b.Genislik}).";
        if (b.Yukseklik > MaxHeight)
            return $"Logo yüksekliği en fazla {MaxHeight} piksel olabilir (yüklenen: {b.Yukseklik}).";
        return null;
    }

    /// <summary>Kabul edilmiş logonun ekranda gösterilecek değerlendirmesi.</summary>
    public static LogoDegerlendirme Evaluate(byte[] bytes)
    {
        var b = PngSize.Read(bytes);
        if (b is null)
            // Boyut okunamadı (bozuk IHDR). Basmayı DENEMEYİZ — QuestPDF'te patlarsa sözleşme hiç basılmaz.
            return new LogoDegerlendirme(bytes.Length, null, null, false,
                "Logo ölçüleri okunamadı; PDF'te firma adı basılacak. Dosyayı yeniden kaydedip deneyin.");

        var (g, y) = b.Value;
        // Kural sıkılaşmadan ÖNCE kaydedilmiş dev ölçülü logo: PDF motoru onu tam çözerdi (bellek bombası).
        // Yükleme kapısı artık reddediyor; eski kayıt için PDF yolu da basmaz, metin fallback'i devralır.
        if (g > MaxWidth || y > MaxHeight)
            return new LogoDegerlendirme(bytes.Length, g, y, false,
                $"Logo çok büyük ({g}×{y} px) — PDF'te YOK SAYILACAK. En fazla {MaxWidth}×{MaxHeight} px.");

        if (g < IgnoredWidth)
            return new LogoDegerlendirme(bytes.Length, g, y, false,
                $"Logo çok küçük ({g}×{y} px) — PDF'te YOK SAYILACAK, yerine firma adı basılacak. " +
                $"En az {RecommendedWidth} px genişlik önerilir.");

        if (g < RecommendedWidth)
            return new LogoDegerlendirme(bytes.Length, g, y, true,
                $"Logo {g}×{y} px — baskıda bulanık görünebilir. En az {RecommendedWidth} px önerilir.");

        return new LogoDegerlendirme(bytes.Length, g, y, true, null);
    }

    /// <summary>PDF üretim yolunun kapısı: bu logo basılabilir mi? <c>false</c> → metin fallback.</summary>
    public static bool IsPrintable(byte[]? bytes)
        => bytes is { Length: > 0 } && Evaluate(bytes).BaskiyaUygun;
}
