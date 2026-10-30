/**
 * Para ve ondalık girdisi (saf). Değer DAİMA değişmez (invariant) ondalık METİN olarak taşınır:
 * `"1234.56"`, `"-0.01"`, `"0.00"`. Kayan noktaya hiç girmez; yuvarlama rakam dizisi üstünde yapılır.
 * Sunucuya da metin gider (System.Text.Json web varsayılanı sayıyı metinden okur → `decimal` birebir).
 *
 * Kullanıcı yazımı Türkçe: `1.234,56` (binlik nokta, ondalık virgül). Tek noktalı ve üç haneli
 * olmayan kesir (`1234.56`, `12.5`) Excel/İngilizce yapıştırma kabul edilir; `1.234` ise Türkçe
 * binliktir (= 1234). Yuvarlama: yarım SIFIRDAN UZAĞA (`0,005` → `0.01`, `-0,005` → `-0.01`) —
 * `paraBicimle` ile aynı kural, kullanıcının gördüğü sayı kaydedilen sayıdır.
 */

export interface OndalikSecenekleri {
  /** Kesir hanesi (para 2). */
  readonly kesir: number;
  /** Negatif kabul edilir mi (varsayılan hayır). */
  readonly negatif?: boolean;
  /** Fazla kesir hanesi: yuvarla (para) ya da reddet (adet, km). Varsayılan yuvarla. */
  readonly fazlaHane?: 'yuvarla' | 'reddet';
  /** Tam kısım en çok kaç hane (varsayılan 15 — `numeric(19,4)`). */
  readonly azamiTamHane?: number;
}

export type DecimalParseResult =
  { readonly gecerli: true; readonly deger: string | null } | { readonly gecerli: false };

const INVALID: DecimalParseResult = { gecerli: false };
const EMPTY: DecimalParseResult = { gecerli: true, deger: null };

/** Türkçe binlik gruplu tam kısım: `1.234`, `12.345.678`. Baştaki grup sıfırla başlamaz. */
const TR_THOUSANDS = /^[1-9]\d{0,2}(\.\d{3})+$/;
const DIGIT = /^\d+$/;
const INVARIANT = /^(-?)(\d+)(?:\.(\d+))?$/;

/** Kullanıcı metni → invariant ondalık metin. Boş → `null`; biçimsiz → `gecerli: false`. */
export function parseDecimal(text: string, option: OndalikSecenekleri): DecimalParseResult {
  // `\s` bölünmez boşlukları (U+00A0, U+202F) da kapsar.
  let s = text.replace(/[\s₺]/g, '');
  if (s === '') return EMPTY;

  let negative = false;
  if (s.startsWith('-') || s.startsWith('−')) {
    negative = true;
    s = s.slice(1);
  } else if (s.startsWith('+')) {
    s = s.slice(1);
  }

  let full: string;
  let fraction: string;
  if (s.includes(',')) {
    const parts = s.split(',');
    if (parts.length !== 2) return INVALID;
    [full = '', fraction = ''] = parts;
    if (full.includes('.')) {
      if (!TR_THOUSANDS.test(full)) return INVALID;
      full = full.replace(/\./g, '');
    }
  } else if (s.includes('.')) {
    if (TR_THOUSANDS.test(s)) {
      full = s.replace(/\./g, '');
      fraction = '';
    } else {
      const parts = s.split('.');
      if (parts.length !== 2) return INVALID;
      [full = '', fraction = ''] = parts;
    }
  } else {
    full = s;
    fraction = '';
  }

  if (full === '' && fraction === '') return INVALID;
  if ((full !== '' && !DIGIT.test(full)) || (fraction !== '' && !DIGIT.test(fraction)))
    return INVALID;

  return normalize(negative, full === '' ? '0' : full, fraction, option);
}

/**
 * Sunucudan/koddan gelen değer (JSON sayısı ya da invariant metin) → kanonik invariant metin.
 * Kullanıcı metni DEĞİLDİR: `"1.234"` burada bir virgül bin iki yüz otuz dört binde birdir, 1234 değil.
 * Biçimsiz girdi `null` döner (sessiz sıfır değil).
 */
export function invariantDecimal(
  value: number | string | null | undefined,
  option: OndalikSecenekleri,
): string | null {
  if (value === null || value === undefined || value === '') return null;
  let text: string;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) return null;
    // Kısa gösterim (`String`) 15 anlamlı haneye kadar JSON metnini birebir geri verir; üslü gösterim
    // yalnız çok küçük/büyük sayılarda çıkar, onlar sabit gösterime çevrilir.
    text = /e/i.test(String(value)) ? value.toFixed(20) : String(value);
  } else {
    text = value.trim();
  }
  const match = INVARIANT.exec(text);
  if (!match) return null;
  const result = normalize(match[1] === '-', match[2] ?? '0', match[3] ?? '', {
    ...option,
    negatif: true,
    fazlaHane: 'yuvarla',
    azamiTamHane: Number.MAX_SAFE_INTEGER,
  });
  return result.gecerli ? result.deger : null;
}

/** Invariant metin → Türkçe yazım (`"1234.5"` → `"1.234,50"`). Değer yoksa boş metin. */
export function formatDecimal(value: string | null | undefined, fraction: number): string {
  if (value === null || value === undefined || value === '') return '';
  const match = INVARIANT.exec(value);
  if (!match) return '';
  const [, sign = '', full = '0', fractionText = ''] = match;
  const grouped = full.replace(/^0+(?=\d)/, '').replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  const k = fractionText.padEnd(fraction, '0').slice(0, fraction);
  return `${sign}${grouped}${fraction > 0 ? `,${k}` : ''}`;
}

/** Düzenleme sırasında gösterilen yazım: gruplamasız (`"1234,56"`), imleç kaymasın diye. */
export function decimalEditText(value: string | null | undefined, fraction: number): string {
  return formatDecimal(value, fraction).replace(/\./g, '');
}

function normalize(
  negative: boolean,
  full: string,
  fraction: string,
  option: OndalikSecenekleri,
): DecimalParseResult {
  const digit = option.kesir;
  let integerDigits = full.replace(/^0+(?=\d)/, '');
  let fractionDigits = fraction;

  if (fractionDigits.length > digit) {
    const dropped = fractionDigits.slice(digit);
    if ((option.fazlaHane ?? 'yuvarla') === 'reddet' && /[1-9]/.test(dropped)) return INVALID;
    fractionDigits = fractionDigits.slice(0, digit);
    // Büyüklük üstünde yukarı yuvarlamak = sıfırdan uzağa (işaret sonra eklenir).
    if ((dropped[0] ?? '0') >= '5')
      [integerDigits, fractionDigits] = addOne(integerDigits, fractionDigits);
  }
  fractionDigits = fractionDigits.padEnd(digit, '0');

  if (integerDigits.length > (option.azamiTamHane ?? 15)) return INVALID;
  const zero = /^0+$/.test(integerDigits + fractionDigits);
  if (negative && !zero && !option.negatif) return INVALID;

  const sign = negative && !zero ? '-' : '';
  return {
    gecerli: true,
    deger: `${sign}${integerDigits}${digit > 0 ? `.${fractionDigits}` : ''}`,
  };
}

/** `tam.kesir` rakam dizisine son haneden 1 ekler (taşma tam kısma geçer). */
function addOne(full: string, fraction: string): [string, string] {
  const digits = (full + fraction).split('').map(Number);
  let i = digits.length - 1;
  while (i >= 0) {
    const r = (digits[i] ?? 0) + 1;
    if (r < 10) {
      digits[i] = r;
      break;
    }
    digits[i] = 0;
    i--;
  }
  let merged = digits.join('');
  if (i < 0) merged = `1${merged}`;
  const fullLength = merged.length - fraction.length;
  return [merged.slice(0, fullLength), merged.slice(fullLength)];
}
