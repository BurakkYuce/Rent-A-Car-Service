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
export function hexNormalize(value: string | null | undefined): string | null {
  const match = HEX.exec((value ?? '').trim());
  if (!match) return null;
  const h = match[1] ?? '';
  const full = h.length === 3 ? [...h].map((c) => c + c).join('') : h;
  const rgb = hexRgb(`#${full}`);
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
  const channel = (v: number) =>
    Math.max(0, Math.min(255, Math.round(v)))
      .toString(16)
      .padStart(2, '0');
  return `#${channel(r)}${channel(g)}${channel(b)}`;
}

function isValidHex(value: string): string {
  const hex = hexNormalize(value);
  if (!hex) throw new Error(`Geçersiz renk: ${value}`);
  return hex;
}

/** WCAG 2.x göreli parlaklık (0 = siyah, 1 = beyaz). */
export function goreliParlaklik(renk: string): number {
  const { r, g, b } = hexRgb(isValidHex(renk));
  const channel = (v: number) => {
    const c = v / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** WCAG kontrast oranı, 1 ile 21 arası. Sıra önemsiz. */
export function contrastRatio(a: string, b: string): number {
  const la = goreliParlaklik(a);
  const lb = goreliParlaklik(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** `a`'dan `b`'ye doğrusal sRGB karışımı; `oran` 0 → a, 1 → b. */
export function mix(a: string, b: string, rate: number): string {
  const x = hexRgb(isValidHex(a));
  const y = hexRgb(isValidHex(b));
  const t = Math.max(0, Math.min(1, rate));
  return rgbHex({
    r: x.r + (y.r - x.r) * t,
    g: x.g + (y.g - x.g) * t,
    b: x.b + (y.b - x.b) * t,
  });
}

export const WHITE = '#ffffff';
export const BLACK = '#000000';

/**
 * Verilen dolgu renklerinin HEPSİNİN üstünde en okunur olan metin rengi: beyaz ya da siyah.
 * Saf siyah/beyaz seçildiği için tek dolguda oran her zaman ≥ 4.58 olur (WCAG AA).
 */
export function textOn(...fills: string[]): string {
  const worst = (text: string) => Math.min(...fills.map((d) => contrastRatio(text, d)));
  return worst(WHITE) >= worst(BLACK) ? WHITE : BLACK;
}

/**
 * `renk`'i, verilen tüm zeminlerde `esik` kontrastı sağlayana kadar `yon`'e (siyah ya da beyaz)
 * doğru adım adım karıştırır. Zaten yeterliyse aynen döner. Saf siyah/beyaz sınırında durur.
 */
export function readableTone(
  renk: string,
  backgrounds: readonly string[],
  threshold: number,
): string {
  const source = isValidHex(renk);
  const sufficient = (candidate: string) =>
    backgrounds.every((z) => contrastRatio(candidate, z) >= threshold);
  if (sufficient(source)) return source;

  const average = backgrounds.reduce((t, z) => t + goreliParlaklik(z), 0) / backgrounds.length;
  const yon = average > 0.18 ? BLACK : WHITE;
  for (let step = 1; step <= 20; step++) {
    const candidate = mix(source, yon, step * 0.05);
    if (sufficient(candidate)) return candidate;
  }
  return yon;
}
