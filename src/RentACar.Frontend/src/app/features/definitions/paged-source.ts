import { inject } from '@angular/core';
import { type Observable, EMPTY, expand, map, reduce } from 'rxjs';

import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import {
  type RestDefinitionOptions,
  type DefinitionSource,
  type DefinitionRow,
  restDefinitionSource,
} from '@shared/form/tanim-crud/definition-source';

/** Sayfalı liste yanıtı (`Sayfa<T>`: kayitlar/toplam/sayfaNo/boyut). */
interface DefinitionPage {
  readonly kayitlar: readonly DefinitionRow[];
  readonly toplam: number;
}

/** Sunucunun sayfa boyutu tavanı (`ListeIstegi`: 1..200). */
export const PAGE_SIZE = 200;

/**
 * F11.1b tanım uçları (`/sigorta-sirketleri`, `/kdv-oranlari`, `/ceza-turleri`, `/arac-gruplari`,
 * `/belge-sablonlari`) listeyi SAYFALI döner ve satırda `surum` yoktur (null). Tanım CRUD'u tüm listeyi ister: sayfalar
 * `toplam`a kadar sırayla okunur (tanım tabloları küçük; çoğunda tek istek). Sürümsüz satırın düzenlemesi açılırken
 * kayıt tekil okunur (`read` → güncel `surum`), 409 birleştirmesi de aynı yoldan.
 */
export function pagedDefinitionSource(
  root: ApiPath,
  sort = 'kod',
  options: RestDefinitionOptions = {},
): DefinitionSource {
  const api = inject(ApiIstemcisi);
  const toRow = options.toRow ?? ((raw: Readonly<Record<string, unknown>>) => raw as DefinitionRow);
  const page = (no: number): Observable<DefinitionPage> =>
    api
      .get<DefinitionPage>(root, {
        parametreler: { sayfa: no, boyut: PAGE_SIZE, sirala: sort },
      })
      .pipe(map((p) => ({ ...p, kayitlar: p.kayitlar.map(toRow) })));
  return {
    ...restDefinitionSource(root, options),
    listele: () =>
      page(1).pipe(
        map((p) => ({ p, no: 1 })),
        expand(({ p, no }) =>
          no * PAGE_SIZE < p.toplam && p.kayitlar.length > 0
            ? page(no + 1).pipe(map((next) => ({ p: next, no: no + 1 })))
            : EMPTY,
        ),
        reduce<{ p: DefinitionPage }, DefinitionRow[]>((all, { p }) => [...all, ...p.kayitlar], []),
      ),
  };
}
