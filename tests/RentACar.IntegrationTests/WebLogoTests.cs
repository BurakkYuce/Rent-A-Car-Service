using RentACar.Application.Common;
using SkiaSharp;

namespace RentACar.IntegrationTests;

/// <summary>
/// Halka açık site logosu küçültmesi — saf birim testi (DB yok). BAĞIMSIZ ORACLE: beklenen ölçüler elle
/// hesaplanır (2000×500 → sınır 640×160; oran 4:1 korunur → 640×160).
/// </summary>
public sealed class WebLogoTests
{
    /// <summary>Sol yarısı opak kırmızı, sağ yarısı TAMAMEN şeffaf gerçek PNG.</summary>
    private static byte[] HalfTransparentPng(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, w / 2f, h, paint);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Buyuk_logo_orani_korunarak_kuculur_ve_seffaflik_kalir()
    {
        var result = ImageProcessing.TryCreateWebLogo(HalfTransparentPng(2000, 500));
        Assert.NotNull(result);

        using var decoded = SKBitmap.Decode(result!);
        Assert.Equal(640, decoded.Width);
        Assert.Equal(160, decoded.Height);
        Assert.Equal(0, decoded.GetPixel(600, 80).Alpha);      // sağ yarı şeffaf kaldı (beyaza basılmadı)
        Assert.Equal(255, decoded.GetPixel(40, 80).Alpha);     // sol yarı opak
        Assert.Equal(SKEncodedImageFormat.Png, SKCodec.Create(new MemoryStream(result!)).EncodedFormat);
    }

    [Fact]
    public void Yuksekligi_sinirda_olan_dar_logo_yukseklige_gore_kuculur()
    {
        // 400×400: genişlik sınırı (640) aşılmıyor, yükseklik (160) aşılıyor → 160×160.
        using var decoded = SKBitmap.Decode(ImageProcessing.TryCreateWebLogo(HalfTransparentPng(400, 400))!);
        Assert.Equal(160, decoded.Width);
        Assert.Equal(160, decoded.Height);
    }

    [Fact]
    public void Kucuk_logo_buyutulmez_ve_bozuk_dosya_patlamaz()
    {
        Assert.Null(ImageProcessing.TryCreateWebLogo(HalfTransparentPng(300, 80))); // zaten sınır içinde
        Assert.Null(ImageProcessing.TryCreateWebLogo([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]));
        Assert.Null(ImageProcessing.TryCreateWebLogo([]));
    }
}
