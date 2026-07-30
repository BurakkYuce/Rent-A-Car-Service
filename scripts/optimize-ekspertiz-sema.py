#!/usr/bin/env python3
"""
PR-18 — sözleşme PDF'indeki ekspertiz şemasını küçültür (tekrarlanabilir dönüşüm).

NEDEN: kaynak varlık 2000x647 **RGBA** ve 807 KB'dı. Ölçüldü:
  - alfa kanalı TAMAMEN opak (min=max=255) → 4. kanal saf israf,
  - 1.293.619 nötr piksele karşı yalnız 381 renkli piksel → gri tonlama kayıpsız sayılır,
  - 2000 px, ~500 pt baskı genişliğinde ~288 DPI demek; çizgi çizimi için 150 DPI yeter.

Ek adım — GRİ SEVİYE NİCEMLEME (16 seviye): PDF, `UseOriginalImage()` ile gömülen görselin HAM
piksellerini Flate'liyor, PNG'nin kendi sıkıştırmasını kullanmıyor. 8-bit gri ham veri 1040x336'da
Flate sonrası ~108 KB; 16 seviyeye indirince ~34 KB (3x kazanç, ÇÖZÜNÜRLÜK KAYBI YOK — bu bir çizgi
çizimi, düz gri dolgular ve siyah çizgiler için 16 ton fazlasıyla yeter).

DİKKAT: `ImageOps.posterize` KULLANILMAZ — alt bitleri maskeliyor ve 255'i 240'a düşürüyor, yani
BEYAZ ZEMİN GRİLEŞİYOR (sözleşmede gri kutu olarak basılırdı; piksel değeri okunarak yakalandı).
Buradaki LUT uçları korur: 0 → 0, 255 → 255.

Sonuç: 1040x336, 16 seviye gri PNG (~33 KB). Çıktı QuestPDF'e `UseOriginalImage()` ile veriliyor →
yeniden kodlama YOK, metin çevresinde JPEG bulanıklığı YOK.

Kullanım:  python3 scripts/optimize-ekspertiz-sema.py <kaynak.png> <hedef.png>
Elle bir kez düzenlenmiş bir dosya bırakmamak için betik repoda tutuluyor.
"""
import sys
from PIL import Image

HEDEF_GENISLIK = 1040  # ~150 DPI @ 500 pt baskı genişliği
GRI_SEVIYE = 16        # düz dolgu + siyah çizgi için yeterli; PDF akışını 108 KB → 34 KB indiriyor


def nicemle(im: "Image.Image", seviye: int) -> "Image.Image":
    """Uçları KORUYAN gri nicemleme (0→0, 255→255). posterize bunu yapmaz."""
    lut = [round(round(v / 255 * (seviye - 1)) * 255 / (seviye - 1)) for v in range(256)]
    return im.point(lut)

def main(kaynak: str, hedef: str) -> None:
    im = Image.open(kaynak)
    if im.mode == "RGBA":
        alfa = im.getchannel("A").getextrema()
        if alfa != (255, 255):
            raise SystemExit(f"Alfa kanalı KULLANILIYOR {alfa} — gri tonlamaya çevirmek şeffaflığı bozar.")
    gri = im.convert("L")
    if gri.width > HEDEF_GENISLIK:
        yeni = (HEDEF_GENISLIK, round(gri.height * HEDEF_GENISLIK / gri.width))
        gri = gri.resize(yeni, Image.LANCZOS)
    gri = nicemle(gri, GRI_SEVIYE)
    if gri.getpixel((5, 5)) != 255:
        raise SystemExit("Köşe pikseli beyaz DEĞİL — nicemleme zemini grileştirmiş.")
    gri.save(hedef, format="PNG", optimize=True)
    print(f"{kaynak} → {hedef}: {gri.width}x{gri.height}, {GRI_SEVIYE} seviye gri")

if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
