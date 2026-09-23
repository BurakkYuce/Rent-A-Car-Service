import { inject } from '@angular/core';
import { type Observable, EMPTY, expand, map, reduce, switchMap, throwError } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { TanimDegeri, TanimKaynagi, TanimSatiri } from '@shared/form/tanim-crud/tanim-kaynagi';

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
 * - PUT tam değiştirmedir ve `surum` ister; liste satırı sürüm taşımaz. Kaydederken kayıt tekil uçtan okunur:
 *   düzenleme açıldığındaki (liste anındaki) değerlerden biri sunucuda DEĞİŞMİŞSE istek gitmez, 409 `cakisma`
 *   döner (form silinmez; tanım satırı yeniden okunana kadar ikinci kayıt bilinçli üzerine yazar). Değişmemişse
 *   okunan `surum` ile PUT: arada yazım olursa sunucu 409 verir.
 */
export function definitionSource(
  root: ApiYolu,
  fields: readonly string[],
  conflictMessage: string,
): TanimKaynagi {
  const api = inject(ApiIstemcisi);
  const snapshot = new Map<string, TanimSatiri>();
  const record = (id: string): ApiYolu => `${root}/${encodeURIComponent(id)}`;

  const toRow = (dto: DefinitionDto): TanimSatiri => {
    const row: Record<string, unknown> = { id: dto.id };
    for (const f of fields)
      row[f] = f === 'durum' ? (dto.aktif ? ACTIVE : PASSIVE) : (dto[f] ?? null);
    return row as TanimSatiri;
  };

  const toBody = (value: TanimDegeri): Record<string, unknown> => {
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
    listele: (): Observable<readonly TanimSatiri[]> =>
      page(1).pipe(
        expand((p) => (p.sayfaNo * p.boyut < p.toplam ? page(p.sayfaNo + 1) : EMPTY)),
        reduce((all, p) => [...all, ...p.kayitlar], [] as DefinitionDto[]),
        map((all) => {
          snapshot.clear();
          const rows = all.map(toRow);
          for (const r of rows) snapshot.set(r.id, r);
          return rows;
        }),
      ),
    olustur: (value, key) => api.post(root, toBody(value), { islemAnahtari: key }),
    guncelle: (id, value, key) =>
      api.get<DefinitionDto>(record(id)).pipe(
        switchMap((current) => {
          const before = snapshot.get(id);
          const now = toRow(current);
          const changed = before && fields.some((f) => (before[f] ?? null) !== (now[f] ?? null));
          snapshot.set(id, now);
          if (changed) {
            return throwError(
              () =>
                new ApiHatasi({
                  status: 409,
                  kod: 'cakisma',
                  detay: conflictMessage,
                }),
            );
          }
          return api.put(
            record(id),
            { ...toBody(value), surum: current.surum ?? null },
            { islemAnahtari: key },
          );
        }),
      ),
    sil: (id, key) => api.delete(record(id), { islemAnahtari: key }),
  };
}
