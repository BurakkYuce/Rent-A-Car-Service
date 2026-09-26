import { formatDate } from '@angular/common';
import { ISTANBUL_OFFSET, LOCALE } from '../yerel/tr-yerel';

/**
 * Tarih girdisi (saf). İki ayrı değer türü, iki ayrı kural (repo dersi: "date LOCAL gün, datetime
 * UTC an" — Blazor kira formunda karıştırılınca her kayıtta hayalet +1 gün uzatma çıkmıştı):
 *
 * - **Takvim günü** (`DateOnly`): `"2026-09-22"` metni. Saat dilimine HİÇ girmez; `Date` +
 *   `toISOString()` yoluna sokulmaz (UTC'ye kayıp bir önceki güne düşer). Gün aritmetiği UTC
 *   bileşenleriyle yapılır, tarayıcının saat dilimi sonucu değiştirmez.
 * - **An** (`DateTimeOffset`): UTC ISO metni `"2026-09-22T11:30:00.000Z"`. Kullanıcı İstanbul saatiyle
 *   görür ve yazar (`tarihSaatBicimle` ile aynı ofset); gidip gelmede kayma yok.
 *
 * Revlo `date-picker.models.ts`'ten uyarlandı: dayjs, saat dilimi eklentileri ve çoklu mod atıldı;
 * ızgara (6×7, pazartesi başlangıç) ve katı `GG.AA.YYYY` ayrıştırma korundu.
 */

export type DayText = string;

export interface GunAraligi {
  readonly baslangic: DayText;
  readonly bitis: DayText;
}

/** Ayrıştırma sonucu: boş → `null`, biçimsiz → `'gecersiz'`. */
export type Resolution<T> = T | null | 'gecersiz';

const ISO_DAY = /^(\d{4})-(\d{2})-(\d{2})$/;
const TR_DAY = /^(\d{1,2})[./](\d{1,2})[./](\d{4})$/;
const EIGHT_DIGITS = /^(\d{2})(\d{2})(\d{4})$/;
const DAY_MS = 86_400_000;

/** `+0300` → 180 dakika. Tek kaynak `ISTANBUL_OFSETI` (tr-yerel). */
export const ISTANBUL_OFFSET_MINUTES = ((): number => {
  const e = /^([+-])(\d{2})(\d{2})$/.exec(ISTANBUL_OFFSET);
  if (!e) return 180;
  const minute = Number(e[2]) * 60 + Number(e[3]);
  return e[1] === '-' ? -minute : minute;
})();

function iki(n: number): string {
  return String(n).padStart(2, '0');
}

function generateDays(year: number, month: number, day: number): DayText | null {
  if (year < 1900 || year > 2199 || month < 1 || month > 12 || day < 1) return null;
  const t = new Date(Date.UTC(year, month - 1, day));
  if (t.getUTCFullYear() !== year || t.getUTCMonth() !== month - 1 || t.getUTCDate() !== day)
    return null;
  return `${year}-${iki(month)}-${iki(day)}`;
}

/** Geçerli bir ISO takvim günü mü (`2026-02-30` değil). */
export function isDay(value: unknown): value is DayText {
  if (typeof value !== 'string') return false;
  const e = ISO_DAY.exec(value);
  return !!e && generateDays(Number(e[1]), Number(e[2]), Number(e[3])) !== null;
}

/** Kullanıcı yazımı → takvim günü. `22.09.2026`, `2.9.2026`, `22/09/2026`, `22092026`. */
export function parseDay(text: string): Resolution<DayText> {
  const s = text.trim();
  if (s === '') return null;
  const e = TR_DAY.exec(s) ?? EIGHT_DIGITS.exec(s);
  if (!e) return 'gecersiz';
  return generateDays(Number(e[3]), Number(e[2]), Number(e[1])) ?? 'gecersiz';
}

/** Takvim günü → `dd.MM.yyyy`. */
export function formatDay(day: DayText | null | undefined): string {
  if (!day || !isDay(day)) return '';
  const [y, a, g] = day.split('-');
  return `${g}.${a}.${y}`;
}

/** Sunucu değeri (takvim günü ya da `2026-09-22T00:00:00` gibi saatli yazım) → takvim günü. */
export function normalizeDay(value: unknown): DayText | null {
  if (typeof value !== 'string') return null;
  const day = value.slice(0, 10);
  return isDay(day) ? day : null;
}

function dayUtcMs(day: DayText): number {
  const [y, a, g] = day.split('-').map(Number);
  return Date.UTC(y ?? 1970, (a ?? 1) - 1, g ?? 1);
}

function msDay(ms: number): DayText {
  const t = new Date(ms);
  return `${t.getUTCFullYear()}-${iki(t.getUTCMonth() + 1)}-${iki(t.getUTCDate())}`;
}

export function addDays(day: DayText, count: number): DayText {
  return msDay(dayUtcMs(day) + count * DAY_MS);
}

/** Ayın ilk günü (`yyyy-MM-01`), `adet` ay ileri/geri. */
export function monthStart(day: DayText, count = 0): DayText {
  const [y, a] = day.split('-').map(Number);
  const t = new Date(Date.UTC(y ?? 1970, (a ?? 1) - 1 + count, 1));
  return msDay(t.getTime());
}

export function monthEnd(day: DayText): DayText {
  return addDays(monthStart(day, 1), -1);
}

/** Haftanın pazartesisi (Türkiye'de hafta pazartesi başlar). */
export function weekStart(day: DayText): DayText {
  const weekday = new Date(dayUtcMs(day)).getUTCDay(); // 0 pazar
  return addDays(day, -((weekday + 6) % 7));
}

/** Aynı takvim günleri metin olarak kıyaslanabilir (`yyyy-MM-dd` sözlük sırası = zaman sırası). */
export function compareDays(a: DayText, b: DayText): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** İstanbul'da bugünün takvim günü (tarayıcı saat diliminden bağımsız). */
export function bugun(now: Date = new Date()): DayText {
  return msDay(now.getTime() + ISTANBUL_OFFSET_MINUTES * 60_000);
}

export interface TakvimHucresi {
  readonly gun: DayText;
  readonly ayDisi: boolean;
}

/** Sabit 6×7 (42 hücre) ay ızgarası, pazartesi başlangıç, komşu ay günleriyle. */
export function monthGrid(monthAnchor: DayText): readonly TakvimHucresi[] {
  const start = monthStart(monthAnchor);
  const first = weekStart(start);
  const month = start.slice(0, 7);
  return Array.from({ length: 42 }, (_, i) => {
    const day = addDays(first, i);
    return { gun: day, ayDisi: day.slice(0, 7) !== month };
  });
}

/** `Eylül 2026`. */
export function monthTitle(monthAnchor: DayText): string {
  return formatDate(dayUtcMs(monthStart(monthAnchor)), 'LLLL yyyy', LOCALE, '+0000');
}

/** Pazartesiden başlayan kısa gün adları (`Pt`, `Sa`, …). */
export function weekdayNames(): readonly string[] {
  const monday = Date.UTC(2024, 0, 1); // 1 Ocak 2024 pazartesi
  return Array.from({ length: 7 }, (_, i) =>
    formatDate(monday + i * DAY_MS, 'EEEEEE', LOCALE, '+0000'),
  );
}

/** Uzun ad (ekran okuyucu için): `22 Eylül 2026 Salı`. */
export function dayLongName(day: DayText): string {
  return formatDate(dayUtcMs(day), 'd MMMM y EEEE', LOCALE, '+0000');
}

// ─── Saat ve an ──────────────────────────────────────────────────────────────────────────────

/** `14:30`, `1430`, `9` (→ 09:00), `9:5` (→ 09:05). Aralık dışı (`25:00`) geçersiz — sessiz kırpma yok. */
export function parseHour(text: string): Resolution<string> {
  const s = text.trim();
  if (s === '') return null;
  let hour: number;
  let minute: number;
  const colon = /^(\d{1,2})[:.](\d{1,2})$/.exec(s);
  if (colon) {
    hour = Number(colon[1]);
    minute = Number(colon[2]);
  } else if (/^\d{1,4}$/.test(s)) {
    if (s.length <= 2) {
      hour = Number(s);
      minute = 0;
    } else {
      hour = Number(s.slice(0, s.length - 2));
      minute = Number(s.slice(-2));
    }
  } else {
    return 'gecersiz';
  }
  if (hour > 23 || minute > 59) return 'gecersiz';
  return `${iki(hour)}:${iki(minute)}`;
}

export interface AnParcalari {
  readonly gun: DayText;
  readonly saat: string;
}

/** An (ISO, ofsetli ya da `Z`) → İstanbul duvar saati parçaları. Biçimsiz → `null`. */
export function parseMoment(value: unknown): AnParcalari | null {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}T/.test(value)) return null;
  // Ofsetsiz ISO yazımı (`2026-09-22T11:30:00`) tarayıcıda YEREL saat sayılır; sunucu anı daima
  // ofsetli gönderir, ofsetsizse UTC kabul edilir ki tarayıcı saat dilimi sonucu değiştirmesin.
  const withOffset = /(Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`;
  const ms = Date.parse(withOffset);
  if (Number.isNaN(ms)) return null;
  const t = new Date(ms + ISTANBUL_OFFSET_MINUTES * 60_000);
  return {
    gun: `${t.getUTCFullYear()}-${iki(t.getUTCMonth() + 1)}-${iki(t.getUTCDate())}`,
    saat: `${iki(t.getUTCHours())}:${iki(t.getUTCMinutes())}`,
  };
}

/** İstanbul duvar saati → UTC ISO anı. */
export function mergeMoment(day: DayText, hour: string): string {
  const [s, d] = hour.split(':').map(Number);
  const ms = dayUtcMs(day) + ((s ?? 0) * 60 + (d ?? 0) - ISTANBUL_OFFSET_MINUTES) * 60_000;
  return new Date(ms).toISOString();
}

// ─── Hazır aralıklar ─────────────────────────────────────────────────────────────────────────

export type PresetRangeId =
  'bugun' | 'dun' | 'buHafta' | 'gecenHafta' | 'buAy' | 'gecenAy' | 'son7' | 'son30' | 'buYil';

export interface HazirAralik {
  readonly kimlik: PresetRangeId;
  readonly aralik: GunAraligi;
}

/** Revlo `buildStandardDatePresets` kısaltılmış hali; bugün İstanbul günü. */
export function presetRanges(todayDay: DayText): readonly HazirAralik[] {
  const week = weekStart(todayDay);
  const month = monthStart(todayDay);
  const lastMonth = monthStart(todayDay, -1);
  const a = (identity: PresetRangeId, start: DayText, end: DayText): HazirAralik => ({
    kimlik: identity,
    aralik: { baslangic: start, bitis: end },
  });
  return [
    a('bugun', todayDay, todayDay),
    a('dun', addDays(todayDay, -1), addDays(todayDay, -1)),
    a('buHafta', week, addDays(week, 6)),
    a('gecenHafta', addDays(week, -7), addDays(week, -1)),
    a('buAy', month, monthEnd(month)),
    a('gecenAy', lastMonth, monthEnd(lastMonth)),
    a('son7', addDays(todayDay, -6), todayDay),
    a('son30', addDays(todayDay, -29), todayDay),
    a('buYil', `${todayDay.slice(0, 4)}-01-01`, `${todayDay.slice(0, 4)}-12-31`),
  ];
}

/** Aralık metni: `22.09.2026 – 25.09.2026` (ayraç `–`, `—` ya da boşluklu `-`). */
export function parseRange(text: string): Resolution<GunAraligi> {
  const s = text.trim();
  if (s === '') return null;
  const parts = s.split(/\s*[–—]\s*|\s+-\s+/);
  if (parts.length !== 2) return 'gecersiz';
  const start = parseDay(parts[0] ?? '');
  const bit = parseDay(parts[1] ?? '');
  if (start === null || bit === null || start === 'gecersiz' || bit === 'gecersiz')
    return 'gecersiz';
  return { baslangic: start, bitis: bit };
}

export function formatRange(range: GunAraligi | null | undefined): string {
  if (!range) return '';
  return `${formatDay(range.baslangic)} – ${formatDay(range.bitis)}`;
}
