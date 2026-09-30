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

    /// <summary>
    /// Halka açık site logosu: şeffaflığı KORUYAN küçültülmüş PNG. Ayarlardan yüklenen logo 2000 px / 1 MB'a
    /// kadar olabiliyor (PDF baskısı için); site başlığında ~40 px yükseklikte duruyor. Ölçek yalnız KÜÇÜLTÜR
    /// (büyütme yok). Başarısızsa null — çağıran özgün dosyaya düşer.
    /// </summary>
    public static byte[]? TryCreateWebLogo(byte[] source, int maxWidth = 640, int maxHeight = 160)
    {
        try
        {
            using var data = SKData.CreateCopy(source);
            using var codec = SKCodec.Create(data);
            if (codec is null) return null;
            // KAPI ÇÖZMEDEN ÖNCE: codec yalnız başlığı okur. Ölçüler piksel bütçesini aşıyorsa hiç çözülmez —
            // aksi halde 244 KB'lık bir PNG (2000×1.000.000) ~1,4 GB bellek tutuyordu (anonim uç!).
            var info = codec.Info;
            if (!FitsWebLogoBudget(info.Width, info.Height)) return null;

            // Tam boy çözme — yukarıdaki bütçeyle (en fazla 2000×2000, RGBA'da ≤16 MB) sınırlı.
            using var decoded = SKBitmap.Decode(codec,
                new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (decoded is null) return null;

            // ŞEFFAF KENAR KIRPMA: logoların çoğu PDF için geniş şeffaf boşlukla yükleniyor; site başlığında
            // yükseklik sabit olduğu için boşluk logoyu okunmaz küçüklüğe indiriyordu (ölçüldü: 470×246 PNG'de
            // yazı başlıkta ~70px genişlikte kalıyordu). Görünür piksellerin sınır kutusuna kırpılır.
            var box = OpaqueBounds(decoded);
            if (box.IsEmpty) return null; // tamamen şeffaf — özgün dosya
            var trimmed = box.Width < info.Width || box.Height < info.Height;

            var s = Math.Min(1f, Math.Min((float)maxWidth / box.Width, (float)maxHeight / box.Height));
            if (s >= 1f && !trimmed) return null; // zaten küçük ve kenarsız — özgün dosya servis edilir

            using var cropped = new SKBitmap();
            if (!decoded.ExtractSubset(cropped, box)) return null;
            int tw = Math.Max(1, (int)MathF.Round(box.Width * s));
            int th = Math.Max(1, (int)MathF.Round(box.Height * s));
            using var resized = cropped.Resize(new SKImageInfo(tw, th, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (resized is null) return null;
            using var image = SKImage.FromBitmap(resized);
            using var enc = image.Encode(SKEncodedImageFormat.Png, 100);
            return enc?.ToArray();
        }
        catch { return null; }
    }

    /// <summary>
    /// İÇERİĞİN sınır kutusu; hiç içerik yoksa boş kutu. Arka plan sayılan pikseller: (a) neredeyse şeffaf
    /// (alfa ≤ 8 — kenar yumuşatma artığı kutuyu büyütmesin), (b) dört köşe AYNI düz renkteyse o renge yakın
    /// opak pikseller (kanal farkı ≤ 16). (b) ölçüldü: canlıdaki logo şeffaf değil BEYAZ zeminli, 470×246
    /// tuvalde yazı ortada küçük — yalnız şeffaflık kırpması onu hiç küçültmüyordu.
    /// </summary>
    private static SKRectI OpaqueBounds(SKBitmap bmp)
    {
        const byte alphaThreshold = 8;
        const int colorTolerance = 16;
        var px = bmp.GetPixelSpan(); // RGBA8888 (premul): R, G, B, A
        int w = bmp.Width, h = bmp.Height, rowBytes = bmp.RowBytes;

        int At(int x, int y) => y * rowBytes + x * 4;
        var corner = At(0, 0);
        var solidBackground = px[corner + 3] > 255 - colorTolerance
            && Near(px, corner, At(w - 1, 0), colorTolerance) && Near(px, corner, At(0, h - 1), colorTolerance)
            && Near(px, corner, At(w - 1, h - 1), colorTolerance);

        int left = w, top = h, right = -1, bottom = -1;
        for (var y = 0; y < h; y++)
        {
            var row = y * rowBytes;
            for (var x = 0; x < w; x++)
            {
                var i = row + x * 4;
                if (px[i + 3] <= alphaThreshold) continue;
                if (solidBackground && Near(px, i, corner, colorTolerance)) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                bottom = y;
            }
        }
        static bool Near(ReadOnlySpan<byte> p, int a, int b, int tol)
            => Math.Abs(p[a] - p[b]) <= tol && Math.Abs(p[a + 1] - p[b + 1]) <= tol
               && Math.Abs(p[a + 2] - p[b + 2]) <= tol && Math.Abs(p[a + 3] - p[b + 3]) <= tol;

        // Tek düz renk (içerik yok gibi görünen) opak görüntü zemin DEĞİL, logonun kendisidir: tamamı kalır.
        if (right < 0) return solidBackground ? new SKRectI(0, 0, w, h) : SKRectI.Empty;
        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    /// <summary>Web logosu için çözülebilecek en büyük görüntü: logo yükleme kuralının iki kenar sınırı
    /// (<see cref="LogoValidationRules.MaxWidth"/>×<see cref="LogoValidationRules.MaxHeight"/>) — çözülmüş
    /// hali RGBA'da en fazla 16 MB.</summary>
    public const long MaxWebLogoPixels = (long)LogoValidationRules.MaxWidth * LogoValidationRules.MaxHeight;

    /// <summary>Ölçüler web logosu çözme bütçesinde mi? Her kenar yükleme sınırında VE toplam piksel tavanda.</summary>
    public static bool FitsWebLogoBudget(int width, int height)
        => width > 0 && height > 0
           && width <= LogoValidationRules.MaxWidth && height <= LogoValidationRules.MaxHeight
           && (long)width * height <= MaxWebLogoPixels;

    /// <summary>Görüntüyü ÇÖZMEDEN (yalnız başlık) web logosu bütçesine sığıp sığmadığını söyler. Okunamayan
    /// dosya bütçe dışı sayılır. Kural sıkılaşmadan önce kaydedilmiş dev ölçülü logoları ayıklamak için.</summary>
    public static bool FitsWebLogoBudget(byte[] source)
    {
        try
        {
            using var data = SKData.CreateCopy(source);
            using var codec = SKCodec.Create(data);
            return codec is not null && FitsWebLogoBudget(codec.Info.Width, codec.Info.Height);
        }
        catch { return false; }
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
