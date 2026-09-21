/**
 * Renk yardımcıları (saf). WCAG 2.x göreli parlaklık ve kontrast oranı; kiracı vurgusunun üstüne
 * gelecek metin rengini ve okunur metin tonunu seçmek için. Revlo `theme-palette.utils.ts`'teki
 * karıştırma fikrinden uyarlandı; ham `toLowerCase` yerine büyük/küçük harfe duyarsız desen kullanılır.
 */

interface Rgb {
  r: number;
  g: number;
  b: number;
}

const HEX = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i;

/** `#abc` / `abc` / `#aabbcc` → `#aabbcc` (küçük harf). Geçersizse `null`. */
export function hexNormalize(deger: string | null | undefined): string | null {
  const eslesme = HEX.exec((deger ?? '').trim());
  if (!eslesme) return null;
  const h = eslesme[1] ?? '';
  const tam = h.length === 3 ? [...h].map((c) => c + c).join('') : h;
  const rgb = hexRgb(`#${tam}`);
  return rgbHex(rgb);
}

function hexRgb(hex: string): Rgb {
  const h = hex.slice(1);
  return {
    r: parseInt(h.slice(0, 2), 16),
    g: parseInt(h.slice(2, 4), 16),
    b: parseInt(h.slice(4, 6), 16),
  };
}

function rgbHex({ r, g, b }: Rgb): string {
  const kanal = (v: number) =>
    Math.max(0, Math.min(255, Math.round(v)))
      .toString(16)
      .padStart(2, '0');
  return `#${kanal(r)}${kanal(g)}${kanal(b)}`;
}

function gecerliHex(deger: string): string {
  const hex = hexNormalize(deger);
  if (!hex) throw new Error(`Geçersiz renk: ${deger}`);
  return hex;
}

/** WCAG 2.x göreli parlaklık (0 = siyah, 1 = beyaz). */
export function goreliParlaklik(renk: string): number {
  const { r, g, b } = hexRgb(gecerliHex(renk));
  const kanal = (v: number) => {
    const c = v / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * kanal(r) + 0.7152 * kanal(g) + 0.0722 * kanal(b);
}

/** WCAG kontrast oranı, 1 ile 21 arası. Sıra önemsiz. */
export function kontrastOrani(a: string, b: string): number {
  const la = goreliParlaklik(a);
  const lb = goreliParlaklik(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** `a`'dan `b`'ye doğrusal sRGB karışımı; `oran` 0 → a, 1 → b. */
export function karistir(a: string, b: string, oran: number): string {
  const x = hexRgb(gecerliHex(a));
  const y = hexRgb(gecerliHex(b));
  const t = Math.max(0, Math.min(1, oran));
  return rgbHex({
    r: x.r + (y.r - x.r) * t,
    g: x.g + (y.g - x.g) * t,
    b: x.b + (y.b - x.b) * t,
  });
}

export const BEYAZ = '#ffffff';
export const SIYAH = '#000000';

/**
 * Verilen dolgu renklerinin HEPSİNİN üstünde en okunur olan metin rengi: beyaz ya da siyah.
 * Saf siyah/beyaz seçildiği için tek dolguda oran her zaman ≥ 4.58 olur (WCAG AA).
 */
export function uzerindekiMetin(...dolgular: string[]): string {
  const enKotu = (metin: string) => Math.min(...dolgular.map((d) => kontrastOrani(metin, d)));
  return enKotu(BEYAZ) >= enKotu(SIYAH) ? BEYAZ : SIYAH;
}

/**
 * `renk`'i, verilen tüm zeminlerde `esik` kontrastı sağlayana kadar `yon`'e (siyah ya da beyaz)
 * doğru adım adım karıştırır. Zaten yeterliyse aynen döner. Saf siyah/beyaz sınırında durur.
 */
export function okunurTon(renk: string, zeminler: readonly string[], esik: number): string {
  const kaynak = gecerliHex(renk);
  const yeterli = (aday: string) => zeminler.every((z) => kontrastOrani(aday, z) >= esik);
  if (yeterli(kaynak)) return kaynak;

  const ortalama = zeminler.reduce((t, z) => t + goreliParlaklik(z), 0) / zeminler.length;
  const yon = ortalama > 0.18 ? SIYAH : BEYAZ;
  for (let adim = 1; adim <= 20; adim++) {
    const aday = karistir(kaynak, yon, adim * 0.05);
    if (yeterli(aday)) return aday;
  }
  return yon;
}
