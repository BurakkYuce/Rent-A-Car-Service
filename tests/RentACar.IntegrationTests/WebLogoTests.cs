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

    /// <summary>Gerçek, çözülebilir 1-bit gri PNG'yi ELLE kurar (IHDR + tek IDAT + IEND, CRC'li). Tüm satırlar
    /// sıfır → deflate çok iyi sıkıştırır: 2000×1.000.000 ~250 MB ham veri birkaç yüz KB'a iner.</summary>
    internal static byte[] GrayPng(int width, int height)
    {
        var rowBytes = 1 + (width + 7) / 8; // filtre baytı + 1-bit piksel satırı
        var row = new byte[rowBytes];
        using var idat = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionLevel.Optimal, true))
            for (var i = 0; i < height; i++) z.Write(row);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 1; // bit derinliği 1, renk tipi 0 (gri), sıkıştırma/filtre/interlace 0
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", idat.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeAndData = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
        data.CopyTo(typeAndData, 4);
        s.Write(typeAndData);
        var crc = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        s.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }

    [Fact]
    public void Elle_kurulan_PNG_gercekten_cozulebilir()
    {
        // Yardımcının kendisi: küçük örneği Skia çözebilmeli (aksi halde bomba testi boşa geçer).
        var small = GrayPng(700, 300);
        using var codec = SKCodec.Create(new MemoryStream(small));
        Assert.NotNull(codec);
        Assert.Equal(700, codec.Info.Width);
        using var decoded = SKBitmap.Decode(small);
        Assert.NotNull(decoded);
        Assert.NotNull(ImageProcessing.TryCreateWebLogo(small)); // bütçe içinde → küçültülür
    }

    /// <summary>M1: 2000×1.000.000 gri PNG (~250 KB) — tam çözülse ~1,4 GB. Başlıktan ölçü okunup çözmeden
    /// reddedilmeli.</summary>
    [Fact]
    public void Dekompresyon_bombasi_cozulmeden_reddedilir()
    {
        var bomb = GrayPng(2000, 1_000_000);
        Assert.True(bomb.Length < LogoValidationRules.MaxBytes); // 1 MB kapısından GEÇEN boyutta (senaryo)

        Assert.False(ImageProcessing.FitsWebLogoBudget(bomb));
        Assert.Null(ImageProcessing.TryCreateWebLogo(bomb));
        Assert.NotNull(LogoValidationRules.Reject(bomb)); // yükleme kapısı da reddediyor
    }

    [Theory]
    [InlineData(2000, 2000, true)]
    [InlineData(2001, 10, false)]
    [InlineData(10, 2001, false)]
    [InlineData(0, 10, false)]
    public void Web_logo_piksel_butcesi(int w, int h, bool expected)
        => Assert.Equal(expected, ImageProcessing.FitsWebLogoBudget(w, h));

    [Fact]
    public void Kucuk_logo_buyutulmez_ve_bozuk_dosya_patlamaz()
    {
        Assert.Null(ImageProcessing.TryCreateWebLogo(HalfTransparentPng(300, 80))); // zaten sınır içinde
        Assert.Null(ImageProcessing.TryCreateWebLogo([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]));
        Assert.Null(ImageProcessing.TryCreateWebLogo([]));
    }
}
