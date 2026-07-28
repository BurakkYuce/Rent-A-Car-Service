using SkiaSharp;

namespace RentACar.Application.Common;

/// <summary>
/// Thumbnail üretimi (PR-3, SkiaSharp). Üretim ASLA upload'ı bloklamaz — hata durumunda null döner,
/// serve ucu tam boy görsele düşer.
/// </summary>
public static class ImageProcessing
{
    public static byte[]? TryCreateThumbnail(byte[] source, int maxEdge = 400, int quality = 80)
    {
        try
        {
            using var data = SKData.CreateCopy(source);
            using var codec = SKCodec.Create(data);
            if (codec is null) return null;

            // KRİTİK: 2 MB'lık telefon JPEG'i 6000x4000 olabilir → tam decode ~96 MB ham bitmap.
            // GetScaledDimensions JPEG'de native DCT ölçekli decode verir (1/2,1/4,1/8) → ~1-2 MB.
            var info = codec.Info;
            var pre = codec.GetScaledDimensions(Math.Min(1f, (float)maxEdge / Math.Max(info.Width, info.Height)));
            using var decoded = SKBitmap.Decode(codec, new SKImageInfo(pre.Width, pre.Height));
            if (decoded is null) return null;

            using var oriented = ApplyExifOrientation(decoded, codec.EncodedOrigin);
            // Hedef boyut ORIENTED üzerinden — 90/270'te en/boy takas olduğu için.
            var s = Math.Min(1f, (float)maxEdge / Math.Max(oriented.Width, oriented.Height));
            int tw = Math.Max(1, (int)MathF.Round(oriented.Width * s));
            int th = Math.Max(1, (int)MathF.Round(oriented.Height * s));
            using var resized = (tw == oriented.Width && th == oriented.Height)
                ? null : oriented.Resize(new SKImageInfo(tw, th), new SKSamplingOptions(SKCubicResampler.Mitchell));
            var src = resized ?? oriented;

            // Alfa → beyaz composite: JPEG alfa taşımaz, şeffaf PNG aksi halde SİYAH çıkar.
            using var canvasBmp = new SKBitmap(tw, th, SKColorType.Rgb888x, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(canvasBmp)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(src, 0, 0, new SKSamplingOptions()); }

            using var image = SKImage.FromBitmap(canvasBmp);
            using var enc = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return enc?.ToArray();
        }
        catch { return null; } // resize hatası upload'ı ASLA bloklamaz
    }

    // Origin 5-8'de en/boy TAKAS olur — dst'yi (h,w) açmazsan görüntü KIRPILIR.
    // TopLeft/Default'ta bile Copy() döner: çağıran tarafta tek `using`, çift dispose yok.
    private static SKBitmap ApplyExifOrientation(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft) return src.Copy();
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                           or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = swap ? src.Height : src.Width;
        int h = swap ? src.Width : src.Height;
        var dst = new SKBitmap(w, h, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        canvas.SetMatrix(origin switch
        {
            SKEncodedOrigin.TopRight    => SKMatrix.CreateScale(-1, 1).PostConcat(SKMatrix.CreateTranslation(w, 0)),
            SKEncodedOrigin.BottomRight => SKMatrix.CreateRotationDegrees(180).PostConcat(SKMatrix.CreateTranslation(w, h)),
            SKEncodedOrigin.BottomLeft  => SKMatrix.CreateScale(1, -1).PostConcat(SKMatrix.CreateTranslation(0, h)),
            SKEncodedOrigin.RightTop    => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateTranslation(w, 0)),  // telefon dikey çekim — canlıda görülecek vaka
            SKEncodedOrigin.LeftBottom  => SKMatrix.CreateRotationDegrees(270).PostConcat(SKMatrix.CreateTranslation(0, h)),
            SKEncodedOrigin.LeftTop     => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateScale(-1, 1)).PostConcat(SKMatrix.CreateTranslation(w, 0)),
            SKEncodedOrigin.RightBottom => SKMatrix.CreateRotationDegrees(270).PostConcat(SKMatrix.CreateScale(-1, 1)).PostConcat(SKMatrix.CreateTranslation(w, h)),
            _ => SKMatrix.Identity
        });
        canvas.DrawBitmap(src, 0, 0, new SKSamplingOptions());
        return dst;
    }
}
