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

export type OndalikCozumu =
  { readonly gecerli: true; readonly deger: string | null } | { readonly gecerli: false };

const GECERSIZ: OndalikCozumu = { gecerli: false };
const BOS: OndalikCozumu = { gecerli: true, deger: null };

/** Türkçe binlik gruplu tam kısım: `1.234`, `12.345.678`. Baştaki grup sıfırla başlamaz. */
const TR_BINLIK = /^[1-9]\d{0,2}(\.\d{3})+$/;
const RAKAM = /^\d+$/;
const INVARIANT = /^(-?)(\d+)(?:\.(\d+))?$/;

/** Kullanıcı metni → invariant ondalık metin. Boş → `null`; biçimsiz → `gecerli: false`. */
export function ondalikCoz(metin: string, secenek: OndalikSecenekleri): OndalikCozumu {
  // `\s` bölünmez boşlukları (U+00A0, U+202F) da kapsar.
  let s = metin.replace(/[\s₺]/g, '');
  if (s === '') return BOS;

  let negatif = false;
  if (s.startsWith('-') || s.startsWith('−')) {
    negatif = true;
    s = s.slice(1);
  } else if (s.startsWith('+')) {
    s = s.slice(1);
  }

  let tam: string;
  let kesir: string;
  if (s.includes(',')) {
    const parcalar = s.split(',');
    if (parcalar.length !== 2) return GECERSIZ;
    [tam = '', kesir = ''] = parcalar;
    if (tam.includes('.')) {
      if (!TR_BINLIK.test(tam)) return GECERSIZ;
      tam = tam.replace(/\./g, '');
    }
  } else if (s.includes('.')) {
    if (TR_BINLIK.test(s)) {
      tam = s.replace(/\./g, '');
      kesir = '';
    } else {
      const parcalar = s.split('.');
      if (parcalar.length !== 2) return GECERSIZ;
      [tam = '', kesir = ''] = parcalar;
    }
  } else {
    tam = s;
    kesir = '';
  }

  if (tam === '' && kesir === '') return GECERSIZ;
  if ((tam !== '' && !RAKAM.test(tam)) || (kesir !== '' && !RAKAM.test(kesir))) return GECERSIZ;

  return normalize(negatif, tam === '' ? '0' : tam, kesir, secenek);
}

/**
 * Sunucudan/koddan gelen değer (JSON sayısı ya da invariant metin) → kanonik invariant metin.
 * Kullanıcı metni DEĞİLDİR: `"1.234"` burada bir virgül bin iki yüz otuz dört binde birdir, 1234 değil.
 * Biçimsiz girdi `null` döner (sessiz sıfır değil).
 */
export function invariantOndalik(
  deger: number | string | null | undefined,
  secenek: OndalikSecenekleri,
): string | null {
  if (deger === null || deger === undefined || deger === '') return null;
  let metin: string;
  if (typeof deger === 'number') {
    if (!Number.isFinite(deger)) return null;
    // Kısa gösterim (`String`) 15 anlamlı haneye kadar JSON metnini birebir geri verir; üslü gösterim
    // yalnız çok küçük/büyük sayılarda çıkar, onlar sabit gösterime çevrilir.
    metin = /e/i.test(String(deger)) ? deger.toFixed(20) : String(deger);
  } else {
    metin = deger.trim();
  }
  const eslesme = INVARIANT.exec(metin);
  if (!eslesme) return null;
  const sonuc = normalize(eslesme[1] === '-', eslesme[2] ?? '0', eslesme[3] ?? '', {
    ...secenek,
    negatif: true,
    fazlaHane: 'yuvarla',
    azamiTamHane: Number.MAX_SAFE_INTEGER,
  });
  return sonuc.gecerli ? sonuc.deger : null;
}

/** Invariant metin → Türkçe yazım (`"1234.5"` → `"1.234,50"`). Değer yoksa boş metin. */
export function ondalikBicimle(deger: string | null | undefined, kesir: number): string {
  if (deger === null || deger === undefined || deger === '') return '';
  const eslesme = INVARIANT.exec(deger);
  if (!eslesme) return '';
  const [, isaret = '', tam = '0', kesirMetni = ''] = eslesme;
  const gruplu = tam.replace(/^0+(?=\d)/, '').replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  const k = kesirMetni.padEnd(kesir, '0').slice(0, kesir);
  return `${isaret}${gruplu}${kesir > 0 ? `,${k}` : ''}`;
}

/** Düzenleme sırasında gösterilen yazım: gruplamasız (`"1234,56"`), imleç kaymasın diye. */
export function ondalikDuzenlemeMetni(deger: string | null | undefined, kesir: number): string {
  return ondalikBicimle(deger, kesir).replace(/\./g, '');
}

function normalize(
  negatif: boolean,
  tam: string,
  kesir: string,
  secenek: OndalikSecenekleri,
): OndalikCozumu {
  const hane = secenek.kesir;
  let tamRakam = tam.replace(/^0+(?=\d)/, '');
  let kesirRakam = kesir;

  if (kesirRakam.length > hane) {
    const atilan = kesirRakam.slice(hane);
    if ((secenek.fazlaHane ?? 'yuvarla') === 'reddet' && /[1-9]/.test(atilan)) return GECERSIZ;
    kesirRakam = kesirRakam.slice(0, hane);
    // Büyüklük üstünde yukarı yuvarlamak = sıfırdan uzağa (işaret sonra eklenir).
    if ((atilan[0] ?? '0') >= '5') [tamRakam, kesirRakam] = birEkle(tamRakam, kesirRakam);
  }
  kesirRakam = kesirRakam.padEnd(hane, '0');

  if (tamRakam.length > (secenek.azamiTamHane ?? 15)) return GECERSIZ;
  const sifir = /^0+$/.test(tamRakam + kesirRakam);
  if (negatif && !sifir && !secenek.negatif) return GECERSIZ;

  const isaret = negatif && !sifir ? '-' : '';
  return { gecerli: true, deger: `${isaret}${tamRakam}${hane > 0 ? `.${kesirRakam}` : ''}` };
}

/** `tam.kesir` rakam dizisine son haneden 1 ekler (taşma tam kısma geçer). */
function birEkle(tam: string, kesir: string): [string, string] {
  const rakamlar = (tam + kesir).split('').map(Number);
  let i = rakamlar.length - 1;
  while (i >= 0) {
    const r = (rakamlar[i] ?? 0) + 1;
    if (r < 10) {
      rakamlar[i] = r;
      break;
    }
    rakamlar[i] = 0;
    i--;
  }
  let birlesik = rakamlar.join('');
  if (i < 0) birlesik = `1${birlesik}`;
  const tamUzunluk = birlesik.length - kesir.length;
  return [birlesik.slice(0, tamUzunluk), birlesik.slice(tamUzunluk)];
}
