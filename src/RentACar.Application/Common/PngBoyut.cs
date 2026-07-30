namespace RentACar.Application.Common;

/// <summary>
/// PR-A: PNG genişlik/yükseklik — bağımlılık YOK. PNG başlığındaki IHDR chunk'ı sabit konumdadır:
/// 8 bayt imza + 4 bayt uzunluk + 4 bayt "IHDR" tipi → 16. bayttan itibaren genişlik ve yükseklik,
/// her biri 4 baytlık BIG-ENDIAN tamsayı (PNG spec §11.2.2).
///
/// <para>Neden görüntü kütüphanesi kullanılmıyor: yalnız iki sayı gerekiyor, dosya diske yazılmıyor,
/// decode edilmiyor. `ImageProcessing`in (SkiaSharp) yolunu açmak logo yükleme için gereksiz ağırlık
/// ve mevcut thumbnail davranışını buraya sızdırma riski (bkz. PR planı T6).</para>
///
/// <para>Yalnız PNG okur — logo yolu PNG'ye daraltıldığı için başka türe ihtiyaç yok. Tanımadığı
/// baytta <c>null</c> döner; çağıran "boyut bilinmiyor"u kendi kuralıyla yorumlar.</para>
/// </summary>
public static class PngBoyut
{
    public static (int Genislik, int Yukseklik)? Oku(byte[] png)
    {
        // IHDR yükseklik alanı 23. bayta kadar uzanır → en az 24 bayt şart.
        if (png.Length < 24) return null;
        if (ImageValidation.Detect(png) != ImageKind.Png) return null;
        // 12..15 chunk tipi "IHDR" olmalı; değilse dosya bozuk/farklı sıralı → boyut okumaya kalkışma.
        if (png[12] != 0x49 || png[13] != 0x48 || png[14] != 0x44 || png[15] != 0x52) return null;

        var g = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var y = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return g > 0 && y > 0 ? (g, y) : null;
    }
}
