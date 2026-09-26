import { inject } from '@angular/core';
import { type Observable, EMPTY, expand, map, reduce } from 'rxjs';

import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type {
  DefinitionValue,
  DefinitionSource,
  DefinitionRow,
} from '@shared/form/tanim-crud/definition-source';

/** Tanım listeleri sunucuda sayfalı; ekranda tamamı (tanım kümeleri küçüktür). */
const PAGE_SIZE = 200;

/** Aktif/Pasif (Blazor "Durum" sütunu). Yeni tanım varsayılanda Aktif — boş seçim de Aktif sayılır. */
export const ACTIVE = 'Aktif';
export const PASSIVE = 'Pasif';

interface DefinitionDto {
  readonly id: string;
  readonly aktif: boolean;
  readonly surum?: string | null;
  readonly [field: string]: unknown;
}

/**
 * `/arac-sahipleri`, `/segmentler`, `/arac-tipleri` için `rc-tanim-crud` kaynağı.
 *
 * - Liste sayfalı uçtan TÜM sayfalar okunur; `aktif` → `durum` (Aktif/Pasif) seçimine çevrilir.
 * - PUT tam değiştirmedir ve `surum` ister; liste satırı sürüm taşımayabilir. Bu durumda genel bileşen
 *   düzenlemeyi açarken kaydı `read` ile tekil okur (güncel değer + sürüm) ve PUT o sürümle gider; arada yazım
 *   olursa sunucu 409 `cakisma` verir, bileşen formu silmeden güncel kaydı birleştirir (F11.2a çekirdek eki).
 */
export function definitionSource(root: ApiPath, fields: readonly string[]): DefinitionSource {
  const api = inject(ApiIstemcisi);
  const record = (id: string): ApiPath => `${root}/${encodeURIComponent(id)}`;

  const toRow = (dto: DefinitionDto): DefinitionRow => {
    const row: Record<string, unknown> = { id: dto.id, surum: dto.surum ?? null };
    for (const f of fields)
      row[f] = f === 'durum' ? (dto.aktif ? ACTIVE : PASSIVE) : (dto[f] ?? null);
    return row as DefinitionRow;
  };

  const toBody = (value: DefinitionValue): Record<string, unknown> => {
    const body: Record<string, unknown> = {};
    for (const f of fields) {
      if (f === 'durum') continue;
      const v = value[f];
      body[f] = typeof v === 'string' ? (v.trim() === '' ? null : v.trim()) : (v ?? null);
    }
    body['aktif'] = value['durum'] !== PASSIVE;
    return body;
  };

  const page = (n: number) =>
    api.get<Sayfa<DefinitionDto>>(root, { parametreler: { sayfa: n, boyut: PAGE_SIZE } });

  return {
    listele: (): Observable<readonly DefinitionRow[]> =>
      page(1).pipe(
        expand((p) => (p.sayfaNo * p.boyut < p.toplam ? page(p.sayfaNo + 1) : EMPTY)),
        reduce((all, p) => [...all, ...p.kayitlar], [] as DefinitionDto[]),
        map((all) => all.map(toRow)),
      ),
    read: (id) => api.get<DefinitionDto>(record(id)).pipe(map(toRow)),
    olustur: (value, key) => api.post(root, toBody(value), { islemAnahtari: key }),
    guncelle: (id, value, key, version) =>
      api.put(record(id), { ...toBody(value), surum: version ?? null }, { islemAnahtari: key }),
    sil: (id, key) => api.delete(record(id), { islemAnahtari: key }),
  };
}
