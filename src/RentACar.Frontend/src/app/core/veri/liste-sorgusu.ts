import { SorguParametreleri } from '@core/api/api-istemcisi';
import { EN_FAZLA_BOYUT, ListeIstegi, VARSAYILAN_BOYUT } from '@core/api/sayfa';

/**
 * Tipli liste sorgusu (saf fonksiyonlar). Revlo'nun POST gövdeli list-query'si yerine backend
 * `ListeIstegi` sözleşmesi: GET sorgu parametreleri `sayfa`, `boyut`, `sirala` + uç başına filtreler.
 * URL ile API AYNI parametre adlarını kullanır; URL'deki her değer katalogdan geçerek okunur.
 *
 * **Sağlam ayrıştırma:** bozuk/elle yazılmış URL asla 400 üretmez — geçersiz değer varsayılana düşer
 * (sayfa 1, boyut varsayılan, sıralama varsayılan, filtre yok). Özellikle `sirala` beyaz listeden
 * geçer: sunucu bilinmeyen alanı sessizce yok saymaz, 400 döner.
 */

/** Filtre türleri. Değerler URL'de ve API'de invariant biçimde (nokta ondalık, `yyyy-MM-dd`). */
export type FiltreTanimi =
  /** Serbest metin; kırpılır, NFC'ye normalize edilir, `enFazla` karakterde (varsayılan 200) kesilir. */
  | { readonly tur: 'metin'; readonly enFazla?: number }
  | { readonly tur: 'tamsayi'; readonly enAz?: number; readonly enFazla?: number }
  | { readonly tur: 'ondalik' }
  /** Takvim günü `yyyy-MM-dd` (var olmayan gün, ör. 2026-02-30, reddedilir). */
  | { readonly tur: 'tarih' }
  | { readonly tur: 'secim'; readonly degerler: readonly string[] }
  | { readonly tur: 'bayrak' }
  /** Guid. */
  | { readonly tur: 'kimlik' };

export type FiltreKatalogu = Readonly<Record<string, FiltreTanimi>>;

type FiltreDegeri<D extends FiltreTanimi> = D extends {
  readonly tur: 'secim';
  readonly degerler: readonly (infer S extends string)[];
}
  ? S
  : D extends { readonly tur: 'tamsayi' | 'ondalik' }
    ? number
    : D extends { readonly tur: 'bayrak' }
      ? boolean
      : string;

/** Etkin filtreler; olmayan anahtar = filtre yok. */
export type Filtreler<K extends FiltreKatalogu> = {
  readonly [A in keyof K]?: FiltreDegeri<K[A]>;
};

export interface ListeTanimi<K extends FiltreKatalogu> {
  readonly filtreler: K;
  /** Sunucunun `SiralamaHaritasi` beyaz listesindeki alanlar (azalan için başına `-`). */
  readonly siralanabilir: readonly string[];
  readonly varsayilanSirala: string | null;
  readonly varsayilanBoyut: number;
}

export interface ListeSorgusu<K extends FiltreKatalogu> extends ListeIstegi {
  readonly filtreler: Filtreler<K>;
}

/** Sayfa/boyut/sıralama/filtre değişikliği. Filtreyi kaldırmak için değeri `undefined` verin. */
export interface SorguDegisikligi<K extends FiltreKatalogu> {
  readonly sayfa?: number;
  readonly boyut?: number;
  readonly sirala?: string | null;
  readonly filtreler?: Filtreler<K>;
}

/** `ParamMap` ile uyumlu en küçük okuma arayüzü (router'a bağımlılık yok). */
export interface ParametreOkuyucu {
  get(ad: string): string | null;
}

export type ParametreKaynagi =
  ParametreOkuyucu | Readonly<Record<string, string | readonly string[] | null | undefined>>;

const AYRILMIS_ADLAR: readonly string[] = ['sayfa', 'boyut', 'sirala'];
const INT32_EN_FAZLA = 2_147_483_647;
const METIN_VARSAYILAN_EN_FAZLA = 200;
const TAMSAYI = /^[+-]?\d+$/;
const ONDALIK = /^-?\d+(\.\d+)?(e[+-]?\d+)?$/i;
const TARIH = /^(\d{4})-(\d{2})-(\d{2})$/;
const KIMLIK = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Liste tanımı kurar ve TANIM ANINDA doğrular (yanlış katalog modül yüklenirken patlar, kullanıcıda
 * değil). `const` tip parametresi `secim` değerlerini literal birleşim olarak korur.
 */
export function listeTanimi<const K extends FiltreKatalogu>(tanim: {
  readonly filtreler: K;
  readonly siralanabilir?: readonly string[];
  readonly varsayilanSirala?: string | null;
  readonly varsayilanBoyut?: number;
}): ListeTanimi<K> {
  for (const ad of Object.keys(tanim.filtreler)) {
    if (AYRILMIS_ADLAR.includes(ad) || ad.trim() === '') {
      throw new Error(`Filtre adı "${ad}" ayrılmış (sayfa/boyut/sirala) ya da boş.`);
    }
  }
  const sonuc: ListeTanimi<K> = {
    filtreler: tanim.filtreler,
    siralanabilir: tanim.siralanabilir ?? [],
    varsayilanSirala: tanim.varsayilanSirala ?? null,
    varsayilanBoyut: tanim.varsayilanBoyut ?? VARSAYILAN_BOYUT,
  };
  if (sonuc.varsayilanSirala !== null && siralaCoz(sonuc, sonuc.varsayilanSirala) === null) {
    throw new Error(`Varsayılan sıralama "${sonuc.varsayilanSirala}" sıralanabilir listede yok.`);
  }
  if (boyutKirp(sonuc.varsayilanBoyut) !== sonuc.varsayilanBoyut) {
    throw new Error(`Varsayılan boyut 1..${EN_FAZLA_BOYUT} aralığında olmalı.`);
  }
  return sonuc;
}

/** URL (ya da herhangi bir parametre kaynağı) → sorgu. Hiçbir girdi için fırlatmaz. */
export function sorguyuCoz<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  kaynak: ParametreKaynagi,
): ListeSorgusu<K> {
  const filtreler: Record<string, unknown> = {};
  for (const [ad, filtre] of Object.entries(tanim.filtreler)) {
    const deger = filtreCoz(filtre, ilkDeger(kaynak, ad));
    if (deger !== undefined) filtreler[ad] = deger;
  }
  return {
    sayfa: sayfaCoz(ilkDeger(kaynak, 'sayfa')),
    boyut: boyutCoz(ilkDeger(kaynak, 'boyut'), tanim.varsayilanBoyut),
    sirala: siralaCoz(tanim, ilkDeger(kaynak, 'sirala')) ?? tanim.varsayilanSirala,
    filtreler: filtreler as Filtreler<K>,
  };
}

/**
 * Sorgu → URL sorgu parametreleri. YÖNETİLEN her anahtar döner; varsayılan/boş olanlar `null`
 * (router `queryParamsHandling: 'merge'` ile onları URL'den siler → temiz URL). Yazılan her değer
 * `sorguyuCoz` ile aynı değere geri okunur (gidiş-dönüş testi kilitli).
 */
export function urlParametreleri<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  sorgu: ListeSorgusu<K>,
): Record<string, string | null> {
  const sayfa = sayfaCoz(String(sorgu.sayfa));
  const boyut = boyutCoz(String(sorgu.boyut), tanim.varsayilanBoyut);
  const sirala = siralaCoz(tanim, sorgu.sirala) ?? tanim.varsayilanSirala;
  const sonuc: Record<string, string | null> = {
    sayfa: sayfa === 1 ? null : String(sayfa),
    boyut: boyut === tanim.varsayilanBoyut ? null : String(boyut),
    sirala: sirala === tanim.varsayilanSirala ? null : sirala,
  };
  const filtreler = sorgu.filtreler as Readonly<Record<string, unknown>>;
  for (const [ad, filtre] of Object.entries(tanim.filtreler)) {
    sonuc[ad] = filtreYaz(filtre, filtreler[ad]);
  }
  return sonuc;
}

/** Sorgu → API sorgu parametreleri (`ListeIstegi` + dolu filtreler; sayfa ve boyut her zaman gider). */
export function apiParametreleri<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  sorgu: ListeSorgusu<K>,
): SorguParametreleri {
  const url = urlParametreleri(tanim, sorgu);
  const sonuc: Record<string, string | number> = {
    sayfa: sayfaCoz(String(sorgu.sayfa)),
    boyut: boyutCoz(String(sorgu.boyut), tanim.varsayilanBoyut),
  };
  const sirala = siralaCoz(tanim, sorgu.sirala) ?? tanim.varsayilanSirala;
  if (sirala !== null) sonuc['sirala'] = sirala;
  for (const ad of Object.keys(tanim.filtreler)) {
    const deger = url[ad];
    if (deger !== null && deger !== undefined) sonuc[ad] = deger;
  }
  return sonuc;
}

/**
 * Değişikliği uygular. Filtre, sıralama ya da boyut değişip sayfa açıkça verilmediyse sayfa 1'e
 * döner (5. sayfadayken filtre daralınca boş sayfa görülmesin).
 */
export function sorguyuDegistir<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  mevcut: ListeSorgusu<K>,
  degisiklik: SorguDegisikligi<K>,
): ListeSorgusu<K> {
  const aday: ListeSorgusu<K> = {
    sayfa: degisiklik.sayfa ?? mevcut.sayfa,
    boyut: degisiklik.boyut ?? mevcut.boyut,
    sirala: degisiklik.sirala === undefined ? mevcut.sirala : degisiklik.sirala,
    filtreler:
      degisiklik.filtreler === undefined
        ? mevcut.filtreler
        : { ...mevcut.filtreler, ...degisiklik.filtreler },
  };
  // Normalize: URL'e yazılıp geri okunmuş hâli (geçersiz değerler burada düşer).
  const yeni = sorguyuCoz(tanim, urlParametreleri(tanim, aday));
  const sayfaSifirlanir =
    degisiklik.sayfa === undefined &&
    (yeni.boyut !== mevcut.boyut ||
      yeni.sirala !== mevcut.sirala ||
      filtreAnahtari(tanim, yeni) !== filtreAnahtari(tanim, mevcut));
  return sayfaSifirlanir ? { ...yeni, sayfa: 1 } : yeni;
}

/** Tüm sorgunun kararlı anahtarı (eşitlik: aynı anahtar = aynı istek). */
export function sorguAnahtari<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  sorgu: ListeSorgusu<K>,
): string {
  return JSON.stringify(urlParametreleri(tanim, sorgu));
}

/** Etkin filtre sayısı (katlanır filtre başlığında gösterilir). */
export function etkinFiltreSayisi<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  sorgu: ListeSorgusu<K>,
): number {
  const url = urlParametreleri(tanim, sorgu);
  return Object.keys(tanim.filtreler).filter((ad) => url[ad] !== null).length;
}

function filtreAnahtari<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  sorgu: ListeSorgusu<K>,
): string {
  const url = urlParametreleri(tanim, sorgu);
  return JSON.stringify(Object.keys(tanim.filtreler).map((ad) => url[ad]));
}

function ilkDeger(kaynak: ParametreKaynagi, ad: string): string | null {
  if (okuyucuMu(kaynak)) return kaynak.get(ad);
  const deger = Object.prototype.hasOwnProperty.call(kaynak, ad) ? kaynak[ad] : undefined;
  if (typeof deger === 'string') return deger;
  if (Array.isArray(deger)) {
    const ilk: unknown = deger[0];
    return typeof ilk === 'string' ? ilk : null;
  }
  return null;
}

function okuyucuMu(kaynak: ParametreKaynagi): kaynak is ParametreOkuyucu {
  return typeof (kaynak as Partial<ParametreOkuyucu>).get === 'function';
}

function tamsayiCoz(ham: string | null): number | null {
  if (ham === null) return null;
  const metin = ham.trim();
  if (!TAMSAYI.test(metin)) return null;
  const sayi = Number(metin);
  return Number.isNaN(sayi) ? null : sayi;
}

/** Sunucu kuralı: < 1 → 1. Ek olarak int32 dışı (sunucuda bağlanamaz → 400) → 1. */
function sayfaCoz(ham: string | null): number {
  const sayi = tamsayiCoz(ham);
  if (sayi === null || sayi < 1 || sayi > INT32_EN_FAZLA) return 1;
  return sayi;
}

/** Sunucu kuralı: tamsayı → 1..200'e kırpılır; tamsayı değilse varsayılan. */
function boyutCoz(ham: string | null, varsayilan: number): number {
  const sayi = tamsayiCoz(ham);
  return sayi === null ? varsayilan : boyutKirp(sayi);
}

function boyutKirp(sayi: number): number {
  return Math.min(EN_FAZLA_BOYUT, Math.max(1, Math.trunc(sayi)));
}

/** `"alan"` / `"-alan"`; alan beyaz listede değilse `null`. */
function siralaCoz<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
  ham: string | null,
): string | null {
  if (ham === null) return null;
  const metin = ham.trim();
  const alan = metin.startsWith('-') ? metin.slice(1) : metin;
  return alan !== '' && tanim.siralanabilir.includes(alan) ? metin : null;
}

function filtreCoz(
  filtre: FiltreTanimi,
  ham: string | null,
): string | number | boolean | undefined {
  if (ham === null) return undefined;
  switch (filtre.tur) {
    case 'metin': {
      const metin = Array.from(ham.normalize('NFC').trim())
        .slice(0, filtre.enFazla ?? METIN_VARSAYILAN_EN_FAZLA)
        .join('')
        .trim();
      return metin === '' ? undefined : metin;
    }
    case 'tamsayi': {
      const sayi = tamsayiCoz(ham);
      if (sayi === null || !Number.isSafeInteger(sayi)) return undefined;
      if (filtre.enAz !== undefined && sayi < filtre.enAz) return undefined;
      if (filtre.enFazla !== undefined && sayi > filtre.enFazla) return undefined;
      return sayi;
    }
    case 'ondalik': {
      const metin = ham.trim();
      const sayi = ONDALIK.test(metin) ? Number(metin) : Number.NaN;
      return Number.isFinite(sayi) ? sayi : undefined;
    }
    case 'tarih':
      return gecerliTarihMi(ham.trim()) ? ham.trim() : undefined;
    case 'secim':
      return filtre.degerler.includes(ham) ? ham : undefined;
    case 'bayrak':
      if (ham === 'true' || ham === '1') return true;
      if (ham === 'false' || ham === '0') return false;
      return undefined;
    case 'kimlik':
      return KIMLIK.test(ham.trim()) ? ham.trim() : undefined;
  }
}

/** Değer → URL metni; geçersizse `null`. Yazılan metin `filtreCoz` ile aynı değere döner. */
function filtreYaz(filtre: FiltreTanimi, deger: unknown): string | null {
  if (deger === undefined || deger === null) return null;
  if (typeof deger !== 'string' && typeof deger !== 'number' && typeof deger !== 'boolean') {
    return null;
  }
  const cozulen = filtreCoz(filtre, String(deger));
  return cozulen === undefined ? null : String(cozulen);
}

function gecerliTarihMi(metin: string): boolean {
  const eslesme = TARIH.exec(metin);
  if (eslesme === null) return false;
  const [, yilMetni, ayMetni, gunMetni] = eslesme;
  const yil = Number(yilMetni);
  const ay = Number(ayMetni);
  const gun = Number(gunMetni);
  const tarih = new Date(Date.UTC(yil, ay - 1, gun));
  return (
    tarih.getUTCFullYear() === yil && tarih.getUTCMonth() === ay - 1 && tarih.getUTCDate() === gun
  );
}
