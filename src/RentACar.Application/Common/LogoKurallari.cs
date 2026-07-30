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
public static class LogoKurallari
{
    /// <summary>Bu genişliğin altındaki logo hiç basılmaz (metin fallback'i devralır).</summary>
    public const int YokSayilanGenislik = 50;
    /// <summary>Bunun altında basılır ama baskıda bulanık olacağı uyarısı verilir.</summary>
    public const int OnerilenGenislik = 600;
    public const int MaxGenislik = 2000;
    public const int MaxBayt = 1_048_576; // 1 MB — mevcut SetLogoAsync sınırı korunuyor

    /// <summary>Reddedilmesi gerekiyorsa hata mesajı, uygunsa null. Tür + boyut + ölçü kapısı.</summary>
    public static string? Reddet(byte[] bytes)
    {
        if (bytes.Length == 0) return null; // boş = "logoyu kaldır", çağıran ayrı ele alır
        if (bytes.Length > MaxBayt) return "Logo en fazla 1 MB olabilir.";
        if (ImageValidation.Detect(bytes) != ImageKind.Png)
            return "Logo yalnız PNG olabilir (şeffaf zemin gerekiyor).";
        if (PngBoyut.Oku(bytes) is { } b && b.Genislik > MaxGenislik)
            return $"Logo genişliği en fazla {MaxGenislik} piksel olabilir (yüklenen: {b.Genislik}).";
        return null;
    }

    /// <summary>Kabul edilmiş logonun ekranda gösterilecek değerlendirmesi.</summary>
    public static LogoDegerlendirme Degerlendir(byte[] bytes)
    {
        var b = PngBoyut.Oku(bytes);
        if (b is null)
            // Boyut okunamadı (bozuk IHDR). Basmayı DENEMEYİZ — QuestPDF'te patlarsa sözleşme hiç basılmaz.
            return new LogoDegerlendirme(bytes.Length, null, null, false,
                "Logo ölçüleri okunamadı; PDF'te firma adı basılacak. Dosyayı yeniden kaydedip deneyin.");

        var (g, y) = b.Value;
        if (g < YokSayilanGenislik)
            return new LogoDegerlendirme(bytes.Length, g, y, false,
                $"Logo çok küçük ({g}×{y} px) — PDF'te YOK SAYILACAK, yerine firma adı basılacak. " +
                $"En az {OnerilenGenislik} px genişlik önerilir.");

        if (g < OnerilenGenislik)
            return new LogoDegerlendirme(bytes.Length, g, y, true,
                $"Logo {g}×{y} px — baskıda bulanık görünebilir. En az {OnerilenGenislik} px önerilir.");

        return new LogoDegerlendirme(bytes.Length, g, y, true, null);
    }

    /// <summary>PDF üretim yolunun kapısı: bu logo basılabilir mi? <c>false</c> → metin fallback.</summary>
    public static bool BasilabilirMi(byte[]? bytes)
        => bytes is { Length: > 0 } && Degerlendir(bytes).BaskiyaUygun;
}
