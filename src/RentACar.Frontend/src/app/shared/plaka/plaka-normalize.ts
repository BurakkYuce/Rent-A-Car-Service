/**
 * Türkiye plakası normalizasyonu (Yol v2 Ek A). SAF fonksiyon: hata fırlatmaz, geçersiz girdi `gecerli: false`
 * döner ve olduğu gibi (büyük harfle) gösterilir.
 *
 * Harf dönüşümü BİLEREK Türkçe değildir: plakada "İ" yok, "i" ve "ı" ikisi de "I" okunur. Türkçe yerel ayarlı büyük
 * harf ("i" → "İ") bu yüzden YASAK (desen İ'yi reddeder, "34 ibc 123" geçersiz sayılırdı; stil denetimi yakalar).
 * Kök (İngilizce) eşlemesi kullanılır; TR klavyede Shift+i ile yazılan "İ" de ayrıca "I"ya indirilir.
 */
export interface NormalizedPlate {
  /** Kullanıcının verdiği metin, dokunulmadan. */
  readonly ham: string;
  /** Boşluksuz büyük harf: "34ABC123" (arama/karşılaştırma anahtarı). Geçersizde de boşluk/tiresiz büyük harf. */
  readonly kanonik: string;
  /** Görüntü: geçerliyse "34 ABC 123", değilse kırpılmış büyük harf ham metin. */
  readonly gosterim: string;
  readonly gecerli: boolean;
}

/** İl kodu 01–81; harfler Türk plaka alfabesi (Ç Ğ İ Ö Ş Ü Q W X yok); 1 harf → 4–5 rakam, 2 → 3–4, 3 → 2–3. */
const PLATE_PATTERN = /^(0[1-9]|[1-7]\d|8[01])([ABCDEFGHIJKLMNOPRSTUVYZ]{1,3})(\d{2,5})$/;

const DIGIT_RANGE: Readonly<Record<number, readonly [number, number]>> = {
  1: [4, 5],
  2: [3, 4],
  3: [2, 3],
};

function upperRoot(text: string): string {
  // Kök eşleme: "i" → "I", "ı" → "I" (Türkçe değil — bkz. dosya başı).
  return text.toLocaleUpperCase('en-US').replaceAll('İ', 'I');
}

export function normalizePlate(input: string | null | undefined): NormalizedPlate {
  const ham = input ?? '';
  const upper = upperRoot(ham.trim());
  const kanonik = upper.replace(/[\s-]+/g, '');
  const match = PLATE_PATTERN.exec(kanonik);
  if (match) {
    const [, il, harfler, rakamlar] = match as unknown as [string, string, string, string];
    const [min, max] = DIGIT_RANGE[harfler.length] ?? [0, -1];
    if (rakamlar.length >= min && rakamlar.length <= max) {
      return { ham, kanonik, gosterim: `${il} ${harfler} ${rakamlar}`, gecerli: true };
    }
  }
  return { ham, kanonik, gosterim: upper, gecerli: false };
}
