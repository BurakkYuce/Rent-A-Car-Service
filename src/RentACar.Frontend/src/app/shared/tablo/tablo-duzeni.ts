import {
  SELECTION_COLUMN,
  SELECTION_COLUMN_WIDTH,
  TABLE_LIMITS,
  DEFAULT_MIN_WIDTH,
  DEFAULT_COLUMN_WIDTH,
  type TabloDuzeni,
  type TabloSiralamaDuzeni,
  type TabloSutunDuzeni,
  type TabloSutunu,
} from './tablo-modeli';
import { sortField } from './tablo-siralama';

/**
 * Sütun düzeni (saf). Kullanıcının kayıtlı düzeni tanımlarla HER ZAMAN uzlaştırılır: sunucudaki
 * düzen eski bir sürümden gelebilir (sütun silinmiş/eklenmiş/sabitlenmiş) — bilinmeyen kod düşer,
 * yeni sütun tanımdaki yerine girer, sabit sütun en solda ve görünür kalır. Böylece bozuk ya da
 * bayat bir kayıt ekranı asla kıramaz.
 */

type Columns = readonly TabloSutunu<never>[];

/** Sabit ve gizlenemez sütunlar kullanıcı tarafından gizlenemez. */
export function isHideable(column: TabloSutunu<never>): boolean {
  return !column.sabit && !column.gizlenemez;
}

export function minWidth(column: TabloSutunu<never>): number {
  return Math.max(TABLE_LIMITS.enAzGenislik, column.enAzGenislik ?? DEFAULT_MIN_WIDTH);
}

/** Genişliği sütunun alt sınırı ile sunucu üst sınırı arasına kırpar (tamsayı px). */
export function clampWidth(column: TabloSutunu<never>, px: number): number {
  const enAz = minWidth(column);
  if (!Number.isFinite(px)) return Math.max(enAz, column.genislik ?? DEFAULT_COLUMN_WIDTH);
  return Math.min(TABLE_LIMITS.enFazlaGenislik, Math.max(enAz, Math.round(px)));
}

/** Tanımdan varsayılan düzen: tanım sırası, `gizli` olanlar kapalı, genişlik tanımdan. */
export function defaultLayout(columns: Columns): TabloDuzeni {
  const pinned = columns.filter((s) => s.sabit);
  const others = columns.filter((s) => !s.sabit);
  return {
    sutunlar: [...pinned, ...others].map((s) => ({
      kod: s.kod,
      gorunur: !isHideable(s) || !s.gizli,
      genislik: null,
    })),
    siralama: [],
  };
}

/**
 * Kayıtlı düzeni güncel tanımlarla uzlaştırır. `kayitli` null/bozuksa varsayılan döner.
 * Kural: sabitler tanım sırasıyla en solda + görünür; kayıttaki bilinmeyen/yinelenen kod atılır;
 * kayıtta olmayan yeni sütun tanımdaki önceli (kayıtta varsa) hemen ardına, yoksa sabitlerin ardına
 * girer ve tanımdaki `gizli` değerini alır; genişlik kırpılır; sıralama yalnız sıralanabilir sütunda.
 */
export function mergeLayout(columns: Columns, saved: TabloDuzeni | null): TabloDuzeni {
  const defaultValue = defaultLayout(columns);
  if (saved === null || !Array.isArray(saved.sutunlar)) return defaultValue;

  const definition = new Map(columns.map((s) => [s.kod, s] as const));
  const seen = new Set<string>();
  const fromRecord: TabloSutunDuzeni[] = [];
  for (const k of saved.sutunlar) {
    const s = definition.get(k?.kod);
    if (s === undefined || s.sabit || seen.has(s.kod)) continue;
    seen.add(s.kod);
    fromRecord.push({
      kod: s.kod,
      gorunur: !isHideable(s) || k.gorunur !== false,
      genislik:
        typeof k.genislik === 'number' && Number.isFinite(k.genislik)
          ? clampWidth(s, k.genislik)
          : null,
    });
  }

  // Kayıtta olmayan (yeni eklenmiş) sabit olmayan sütunlar tanımdaki yerlerine.
  const unpinned = columns.filter((s) => !s.sabit);
  unpinned.forEach((s, i) => {
    if (seen.has(s.kod)) return;
    const previous = unpinned
      .slice(0, i)
      .reverse()
      .find((o) => seen.has(o.kod));
    const place =
      previous === undefined ? 0 : fromRecord.findIndex((k) => k.kod === previous.kod) + 1;
    fromRecord.splice(place, 0, {
      kod: s.kod,
      gorunur: !isHideable(s) || !s.gizli,
      genislik: null,
    });
    seen.add(s.kod);
  });

  const pinned = defaultValue.sutunlar.filter((d) => definition.get(d.kod)?.sabit);
  const fixedWidth = new Map(
    (saved.sutunlar ?? [])
      .filter((k) => definition.get(k?.kod)?.sabit && typeof k.genislik === 'number')
      .map((k) => [k.kod, k.genislik] as const),
  );
  const fixedLayout = pinned.map((d) => {
    const px = fixedWidth.get(d.kod);
    const s = definition.get(d.kod);
    return {
      ...d,
      genislik: px === undefined || px === null || s === undefined ? null : clampWidth(s, px),
    };
  });

  const result = [...fixedLayout, ...fromRecord];
  // En az bir görünür sütun (tümü gizlenmiş bir kayıt boş tablo çizmesin).
  if (!result.some((d) => d.gorunur) && result.length > 0) {
    result[0] = { ...result[0], gorunur: true };
  }
  return { sutunlar: result, siralama: filterSort(columns, saved.siralama) };
}

function filterSort(columns: Columns, sort: unknown): TabloSiralamaDuzeni[] {
  if (!Array.isArray(sort)) return [];
  const result: TabloSiralamaDuzeni[] = [];
  for (const oge of sort as readonly Partial<TabloSiralamaDuzeni>[]) {
    const s = columns.find((x) => x.kod === oge?.kod);
    if (s === undefined || sortField(s) === null || result.some((x) => x.kod === s.kod)) {
      continue;
    }
    result.push({ kod: s.kod, azalan: oge.azalan === true });
    if (result.length === TABLE_LIMITS.enFazlaSiralama) break;
  }
  return result;
}

/** Eşitlik anahtarı: aynı anahtar = sunucuya yeniden yazmaya gerek yok. */
export function layoutKey(layout: TabloDuzeni): string {
  return JSON.stringify(layout);
}

export function setVisibility(
  columns: Columns,
  layout: TabloDuzeni,
  code: string,
  visible: boolean,
): TabloDuzeni {
  const s = columns.find((x) => x.kod === code);
  if (s === undefined || (!visible && !isHideable(s))) return layout;
  const newItem = layout.sutunlar.map((d) => (d.kod === code ? { ...d, gorunur: visible } : d));
  if (!newItem.some((d) => d.gorunur)) return layout; // son görünür sütun gizlenemez
  return { ...layout, sutunlar: newItem };
}

export function setWidth(
  columns: Columns,
  layout: TabloDuzeni,
  code: string,
  px: number,
): TabloDuzeni {
  const s = columns.find((x) => x.kod === code);
  if (s === undefined) return layout;
  const width = clampWidth(s, px);
  return {
    ...layout,
    sutunlar: layout.sutunlar.map((d) => (d.kod === code ? { ...d, genislik: width } : d)),
  };
}

/** Sabit olmayan sütunu bir adım sola/sağa taşır (sabitlerin önüne geçemez). */
export function scrollColumn(
  columns: Columns,
  layout: TabloDuzeni,
  code: string,
  yon: -1 | 1,
): TabloDuzeni {
  const fixedValue = new Set(columns.filter((s) => s.sabit).map((s) => s.kod));
  const list = [...layout.sutunlar];
  const i = list.findIndex((d) => d.kod === code);
  const j = i + yon;
  if (i < 0 || fixedValue.has(code) || j < 0 || j >= list.length || fixedValue.has(list[j].kod)) {
    return layout;
  }
  [list[i], list[j]] = [list[j], list[i]];
  return { ...layout, sutunlar: list };
}

/** Sürükle-bırak: `kod`'u `hedef`'in önüne ya da ardına koyar (ikisi de sabit olmamalı). */
export function placeColumn(
  columns: Columns,
  layout: TabloDuzeni,
  code: string,
  target: string,
  location: 'once' | 'sonra',
): TabloDuzeni {
  const fixedValue = new Set(columns.filter((s) => s.sabit).map((s) => s.kod));
  if (code === target || fixedValue.has(code) || fixedValue.has(target)) return layout;
  const moved = layout.sutunlar.find((d) => d.kod === code);
  if (moved === undefined) return layout;
  const list = layout.sutunlar.filter((d) => d.kod !== code);
  const h = list.findIndex((d) => d.kod === target);
  if (h < 0) return layout;
  list.splice(location === 'once' ? h : h + 1, 0, moved);
  return { ...layout, sutunlar: list };
}

export function setSort(layout: TabloDuzeni, sort: TabloSiralamaDuzeni | null): TabloDuzeni {
  return { ...layout, siralama: sort === null ? [] : [sort] };
}

/** TanStack durumu (sütun sırası/görünürlük/genişlik/sabitleme). Seçim sütunu en solda sabit. */
export interface TanstackSutunDurumu {
  readonly columnOrder: string[];
  readonly columnVisibility: Record<string, boolean>;
  readonly columnSizing: Record<string, number>;
  readonly columnPinning: { left: string[]; right: string[] };
}

export function tanstackState(
  columns: Columns,
  layout: TabloDuzeni,
  selectable: boolean,
): TanstackSutunDurumu {
  const definition = new Map(columns.map((s) => [s.kod, s] as const));
  const columnVisibility: Record<string, boolean> = {};
  const columnSizing: Record<string, number> = {};
  for (const d of layout.sutunlar) {
    const s = definition.get(d.kod);
    if (s === undefined) continue;
    columnVisibility[d.kod] = d.gorunur;
    columnSizing[d.kod] = d.genislik ?? clampWidth(s, s.genislik ?? DEFAULT_COLUMN_WIDTH);
  }
  const selection = selectable ? [SELECTION_COLUMN] : [];
  if (selectable) columnSizing[SELECTION_COLUMN] = SELECTION_COLUMN_WIDTH;
  return {
    columnOrder: [...selection, ...layout.sutunlar.map((d) => d.kod)],
    columnVisibility,
    columnSizing,
    columnPinning: {
      left: [...selection, ...columns.filter((s) => s.sabit).map((s) => s.kod)],
      right: [],
    },
  };
}
