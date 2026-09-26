import type { RentalListRow } from '@core/api/ui-tipleri';
import { parseMoment, addDays, type DayText } from '@core/form/tarih-girdisi';
import type { Filters } from '@core/veri/liste-sorgusu';

import { RENTAL_LIST } from './rental-list.store';

export type RentalFilters = Filters<typeof RENTAL_LIST.filtreler>;

/**
 * Kayıtlı görünümler (Yol v2 §5.1/§9): `kiralar?gorunum=…`. Kodlar kenar çubuğundaki bağlantılarla
 * (`kabuk/menu/kisayollar.ts` `GORUNUM_KODLARI`) BİREBİR aynı. Görünüm yalnız bir ÖN AYARDIR: mevcut sunucu
 * süzgeçlerine çevrilir (yeni uç/parametre yok). Tarih ön ayarları İstanbul gününe göre, gün hassasiyetinde
 * (`basMin/basMax` sunucuda gün aralığıdır).
 */
export const RENTAL_VIEW_CODES = [
  'kirada',
  'geciken',
  'bugun-cikan',
  'bugun-donecek',
  'faturasiz',
  'kapali',
] as const;
export type RentalViewCode = (typeof RENTAL_VIEW_CODES)[number];

export function isViewCode(value: string | null): value is RentalViewCode {
  return value !== null && (RENTAL_VIEW_CODES as readonly string[]).includes(value);
}

/**
 * Görünüm → süzgeçler. `gun` = İstanbul'da bugün (`bugun()`).
 * - geciken: kirada + bitiş DÜNE kadar (bugün saati geçmiş dönüş "bugün dönecek"te görünür — sunucu
 *   süzgeci gün hassasiyetinde; saat ayrımı için ayrı parametre yok).
 * - bugün çıkan: başlangıç bugün (durum fark etmez); bugün dönecek: kirada + bitiş bugün.
 */
export function viewFilters(code: RentalViewCode, day: DayText): RentalFilters {
  switch (code) {
    case 'kirada':
      return { durum: 'Kirada' };
    case 'geciken':
      return { durum: 'Kirada', tarihTuru: 'Bitis', basMax: addDays(day, -1) };
    case 'bugun-cikan':
      return { tarihTuru: 'Baslangic', basMin: day, basMax: day };
    case 'bugun-donecek':
      return { durum: 'Kirada', tarihTuru: 'Bitis', basMin: day, basMax: day };
    case 'faturasiz':
      return { fatura: false };
    case 'kapali':
      return { durum: 'Tamamlandi' };
  }
}

/**
 * `liste.degistir` süzgeçleri BİRLEŞTİRİR; ön ayar önceki süzgeçleri silmeli → katalogdaki her ad verilir
 * (verilmeyen `undefined` = URL'den kalkar).
 */
export function allFilters(f: RentalFilters): RentalFilters {
  const result: Record<string, unknown> = {};
  for (const name of Object.keys(RENTAL_LIST.filtreler)) {
    result[name] = (f as Readonly<Record<string, unknown>>)[name];
  }
  return result as RentalFilters;
}

/** Süzgeçler anlamca aynı mı (`undefined` alanlar yok sayılır). */
export function filtersEqual(a: RentalFilters, b: RentalFilters): boolean {
  const key = (f: RentalFilters) =>
    JSON.stringify(
      Object.entries(f)
        .filter(([, v]) => v !== undefined)
        .sort(([x], [y]) => x.localeCompare(y, 'en')),
    );
  return key(a) === key(b);
}

/**
 * Satırın ekran durumu (Yol v2 §1.2). YALNIZ GÖSTERİM: sunucu durumu (`durum`) değişmez; kirada ve bitişi geçmiş
 * gün → "n gün gecikti" (kırmızı), bitişi bugün → "Bugün dönüyor" (sarı + satır vurgusu).
 */
export type RowView =
  | { readonly tur: 'gecikmis'; readonly gun: number }
  | { readonly tur: 'bugunDonuyor' }
  | { readonly tur: 'durum'; readonly durum: string };

const DAY_MS = 86_400_000;
const dayMs = (day: DayText) => Date.parse(`${day}T00:00:00Z`);

export function rowView(row: RentalListRow, today: DayText): RowView {
  const end = parseMoment(row.bitTar)?.gun;
  if (row.durum === 'Kirada' && end) {
    if (end === today) return { tur: 'bugunDonuyor' };
    if (end < today)
      return { tur: 'gecikmis', gun: Math.round((dayMs(today) - dayMs(end)) / DAY_MS) };
  }
  return { tur: 'durum', durum: row.durum };
}

/** Bugünün işi (bugün çıkan ya da kirada olup bugün dönen) → `rc-satir-bugun` (krem satır vurgusu). */
export function rowClass(row: RentalListRow, today: DayText): string | null {
  const g = rowView(row, today);
  if (g.tur === 'bugunDonuyor') return 'rc-satir-bugun';
  return parseMoment(row.basTar)?.gun === today ? 'rc-satir-bugun' : null;
}

/** Durum rozeti sınıfı (§1.2): kirada yeşil, gecikmiş kırmızı, bugün sarı, kapalı nötr, iptal kırmızı. */
export function badgeClass(g: RowView): string {
  switch (g.tur) {
    case 'gecikmis':
      return 'rc-rozet rc-rozet--hata';
    case 'bugunDonuyor':
      return 'rc-rozet rc-rozet--uyari';
    case 'durum':
      return `rc-rozet ${STATUS_BADGE[g.durum] ?? ''}`.trimEnd();
  }
}

const STATUS_BADGE: Readonly<Record<string, string>> = {
  Kirada: 'rc-rozet--basari',
  Tamamlandi: 'rc-rozet--notr',
  Iptal: 'rc-rozet--hata',
};
