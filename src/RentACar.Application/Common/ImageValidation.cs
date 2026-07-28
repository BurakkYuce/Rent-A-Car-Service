namespace RentACar.Application.Common;

public enum ImageKind { Png, Jpeg, WebP, Unknown }

/// <summary>
/// Paylaşılan magic-byte tespiti (PR-3) — client Content-Type header'ına GÜVENİLMEZ, sunucu ham byte'lardan
/// karar verir. Kabul edilen küme ÇAĞIRANA göre değişir: `TenantSettingsService.SetLogoAsync` yalnız
/// Png/Jpeg kabul eder (QuestPDF'in WebP desteği doğrulanamadığı için genişletilmedi — davranış korunur),
/// `VehiclePhotoService` ayrıca WebP kabul eder.
/// </summary>
public static class ImageValidation
{
    public static ImageKind Detect(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ImageKind.Png;
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ImageKind.Jpeg;
        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ImageKind.WebP;
        return ImageKind.Unknown;
    }

    public static string ContentType(ImageKind kind) => kind switch
    {
        ImageKind.Png => "image/png",
        ImageKind.Jpeg => "image/jpeg",
        ImageKind.WebP => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
