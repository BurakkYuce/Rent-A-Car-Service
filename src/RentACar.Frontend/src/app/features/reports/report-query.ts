import type { SorguParametreleri } from '@core/api/api-istemcisi';
import { paraBicimle, sayiBicimle, tarihBicimle, tarihSaatBicimle } from '@core/bicim/bicim';
import {
  type FiltreKatalogu,
  type FiltreTanimi,
  type ListeSorgusu,
  type ListeTanimi,
  listeTanimi,
} from '@core/veri/liste-sorgusu';
import type { DisaAktarma, DisaAktarmaBicimi, DisaAktarmaYolu } from '@shared/tablo/disa-aktarma';

import type { ReportDefinition, ReportFilter, ReportView, ValueKind } from './report-model';

/**
 * Ortak rapor şablonunun saf kuralları: URL kataloğu, API parametreleri, export bağlantısı, değer biçimi.
 * URL tek doğruluk kaynağıdır (F3.4): raporun TÜM görünümlerinin süzgeçleri tek katalogda, görünüm `gorunum`
 * anahtarında; her istek yalnız etkin görünümün süzgeçlerini taşır.
 */

/** Görünüm seçimi URL anahtarı. */
export const VIEW_KEY = 'gorunum';
/** Rapor sayfa boyutu (sunucu tavanı 200). */
export const REPORT_PAGE_SIZE = 50;

/** Süzgecin URL'de kapladığı anahtarlar (`donem` → `bas` + `bit`). */
export function filterKeys(f: ReportFilter<unknown>): readonly string[] {
  return f.tur === 'donem' ? ['bas', 'bit'] : [f.ad];
}

function catalogEntry(f: ReportFilter<unknown>): FiltreTanimi {
  switch (f.tur) {
    case 'donem':
    case 'gun':
      return { tur: 'tarih' };
    case 'metin':
      return { tur: 'metin', enFazla: f.enFazla ?? 100 };
    case 'sube':
      return { tur: 'metin', enFazla: 100 };
    case 'secim':
      return { tur: 'secim', degerler: f.secenekler.map((s) => s.deger) };
    case 'bayrak':
      return { tur: 'bayrak' };
    case 'sayi':
      return f.tamsayi ? { tur: 'tamsayi', enAz: f.enAz, enFazla: f.enFazla } : { tur: 'ondalik' };
    case 'arama':
    case 'liste':
      return { tur: 'kimlik' };
  }
}

/** Raporun URL kataloğu (tüm görünümler). Aynı URL anahtarı iki farklı türle tanımlanamaz. */
export function reportListDefinition(def: ReportDefinition): ListeTanimi<FiltreKatalogu> {
  const filters: Record<string, FiltreTanimi> = {};
  const sortable = new Set<string>();
  for (const view of def.gorunumler) {
    for (const f of view.filtreler) {
      const entry = catalogEntry(f);
      for (const key of filterKeys(f)) {
        const prev = filters[key];
        if (prev && prev.tur !== entry.tur) {
          throw new Error(`Rapor ${def.kod}: "${key}" süzgeci iki farklı türle tanımlı.`);
        }
        filters[key] = entry;
      }
    }
    for (const s of view.satirlar?.siralanabilir ?? []) sortable.add(s);
  }
  if (def.gorunumler.length > 1) {
    filters[VIEW_KEY] = { tur: 'secim', degerler: def.gorunumler.map((v) => v.kod) };
  }
  return listeTanimi({
    filtreler: filters,
    siralanabilir: [...sortable],
    varsayilanSirala: null,
    varsayilanBoyut: REPORT_PAGE_SIZE,
  });
}

/** Etkin görünüm: URL'deki kod ya da ilk görünüm. */
export function activeView(
  def: ReportDefinition,
  filters: Readonly<Record<string, unknown>>,
): ReportView {
  const code = filters[VIEW_KEY];
  return def.gorunumler.find((v) => v.kod === code) ?? def.gorunumler[0]!;
}

/** Uç adresi; `{id}` yol kimliğiyle (kaçışlanarak) dolar. */
export function viewUrl(view: ReportView, id: string | null): `/api/ui/v1/${string}` {
  return view.uc.replace('{id}', encodeURIComponent(id ?? '')) as `/api/ui/v1/${string}`;
}

/**
 * Etkin görünümün API parametreleri: yalnız o görünümün süzgeçleri (URL adı → API adı), sabitler, sayfalı
 * görünümde sayfa/boyut/sirala (sirala görünümün beyaz listesinde değilse DÜŞER — görünüm değişiminde sunucu 400
 * vermesin). Şube kapsamlı kullanıcının şube süzgeci GÖNDERİLMEZ: sunucu kendi şubesine zorlar.
 */
export function viewParams(
  view: ReportView,
  query: ListeSorgusu<FiltreKatalogu>,
  scopedBranch: boolean,
): SorguParametreleri {
  const values = query.filtreler as Readonly<Record<string, unknown>>;
  const out: Record<string, string | number | boolean | null> = { ...view.sabit };
  for (const f of view.filtreler) {
    if (f.tur === 'donem') {
      out['bas'] = str(values['bas']);
      out['bit'] = str(values['bit']);
      continue;
    }
    if (f.tur === 'sube' && scopedBranch) continue;
    const v = values[f.ad];
    const name = f.param ?? f.ad;
    if (f.tur === 'bayrak') out[name] = v === true ? true : null;
    else out[name] = v === undefined || v === null || v === '' ? null : (v as string | number);
  }
  if (view.satirlar) {
    out['sayfa'] = query.sayfa;
    out['boyut'] = query.boyut;
    out['sirala'] =
      query.sirala && view.satirlar.siralanabilir.includes(query.sirala.replace(/^-/, ''))
        ? query.sirala
        : null;
  }
  return Object.fromEntries(Object.entries(out).filter(([, v]) => v !== null && v !== undefined));
}

function str(v: unknown): string | null {
  return typeof v === 'string' && v !== '' ? v : null;
}

/** Sunucunun export bağlantıları → tablo motorunun dışa aktarma girdisi. Bağlantı yoksa `null` (düğme yok). */
export function exportFromLinks(
  links:
    | { readonly excel: string; readonly csv: string; readonly pdf?: string | null }
    | null
    | undefined,
): DisaAktarma | null {
  if (!links) return null;
  const parse = (href: string) => {
    const [path, qs = ''] = href.split('?', 2);
    return { path: path ?? '', params: new URLSearchParams(qs) };
  };
  const excel = parse(links.excel);
  if (!/^\/raporlar\/export\/[a-z0-9-]+$/.test(excel.path)) return null;
  const parametreler: Record<string, string | readonly string[]> = {};
  for (const key of new Set(excel.params.keys())) {
    if (key === 'format') continue;
    const all = excel.params.getAll(key);
    parametreler[key] = all.length === 1 ? all[0]! : all;
  }
  const bicimler: DisaAktarmaBicimi[] = ['excel', 'csv'];
  if (links.pdf) bicimler.push('pdf');
  return { yol: excel.path as DisaAktarmaYolu, parametreler, bicimler };
}

/** `number | string` (decimal JSON) → sayı; biçimsiz → `null`. */
export function asNumber(value: unknown): number | null {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

/** Tek biçim kuralı (kart, özet tablosu; satır tablosu motorun kendi biçimini kullanır). */
export function formatValue(
  kind: ValueKind,
  value: unknown,
  labels: { readonly evet: string; readonly hayir: string },
  currency?: string | null,
): string {
  if (value === null || value === undefined || value === '') return '—';
  switch (kind) {
    case 'para':
      return paraBicimle(asNumber(value), currency || 'TRY') || '—';
    case 'sayi':
      return sayiBicimle(asNumber(value)) || '—';
    case 'tamsayi':
      return sayiBicimle(asNumber(value), '1.0-0') || '—';
    case 'yuzde': {
      const n = asNumber(value);
      return n === null ? '—' : `%${sayiBicimle(n, '1.0-1')}`;
    }
    case 'tarih':
      return tarihBicimle(value as string) || '—';
    case 'tarihSaat':
      return tarihSaatBicimle(value as string) || '—';
    case 'bayrak':
      return value === true ? labels.evet : labels.hayir;
    case 'metin':
      return String(value);
  }
}

/** Negatif mi (işaretli kart/hücre rengi). */
export function isNegative(value: unknown): boolean {
  const n = asNumber(value);
  return n !== null && n < 0;
}
