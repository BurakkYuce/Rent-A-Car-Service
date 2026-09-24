import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import type { CatalogColumn, CatalogConfig, CatalogField, CatalogRow } from './catalog-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

const toNum = (v: unknown): number | null => {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
};

type ColumnKind = NonNullable<CatalogColumn['kind']>;

function kindOf(c: CatalogColumn, f: CatalogField | undefined): ColumnKind {
  if (c.kind) return c.kind;
  switch (f?.kind) {
    case 'money':
      return 'money';
    case 'int':
    case 'ratio':
      return 'number';
    case 'date':
      return 'date';
    case 'bool':
      return 'bool';
    case 'select':
      return 'select';
    default:
      return 'text';
  }
}

/** Tanım listesinin sütunları (Blazor tablo sırası) + işlem sütunu (yazma izni varsa). */
export function catalogColumns(
  c: CatalogConfig,
  t: Translate,
  withActions: boolean,
): readonly TabloSutunu<CatalogRow>[] {
  const columns = c.columns.map((col, i): TabloSutunu<CatalogRow> => {
    const f = c.fields.find((x) => x.name === col.field);
    const label = col.label ?? f?.label ?? (col.field as CeviriAnahtari);
    const kind = kindOf(col, f);
    const base = {
      kod: col.field,
      baslik: t(label),
      sirala: col.sortable === true,
      genislik: col.width ?? (i === 0 ? 130 : 120),
      ...(i === 0 ? { sabit: true, gizlenemez: true } : {}),
    };
    switch (kind) {
      case 'money':
        return {
          ...base,
          tur: 'para',
          deger: (r) => toNum(r[col.field]),
          paraBirimi: (r) => {
            const cur = col.currencyField ? r[col.currencyField] : null;
            return typeof cur === 'string' && cur.trim() !== '' ? cur.trim() : 'TRY';
          },
        };
      case 'number':
        return { ...base, tur: 'sayi', haneler: '1.0-4', deger: (r) => toNum(r[col.field]) };
      case 'date':
        return { ...base, tur: 'tarih', deger: (r) => r[col.field] ?? null };
      case 'bool':
        return {
          ...base,
          deger: (r) => t(r[col.field] === true ? 'fiyatTarife.evet' : 'fiyatTarife.hayir'),
        };
      case 'select':
        return {
          ...base,
          deger: (r) => {
            const v = r[col.field];
            if (typeof v !== 'string' || v === '') return '—';
            return f?.optionLabels ? t(`${f.optionLabels}.${v}` as CeviriAnahtari) : v;
          },
        };
      default:
        return {
          ...base,
          deger: (r) => {
            const v = r[col.field];
            return v === null || v === undefined || v === '' ? '—' : String(v);
          },
        };
    }
  });
  if (withActions)
    columns.push({
      kod: 'islemler',
      baslik: t('fiyatTarife.islemler'),
      deger: () => null,
      genislik: 170,
    });
  return columns;
}
