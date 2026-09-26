import { Signal, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';

import { QueryParameters } from '@core/api/api-istemcisi';
import {
  FilterCatalog,
  ListeSorgusu,
  ListeTanimi,
  SorguDegisikligi,
  apiParams,
  activeFilterCount,
  queryKey,
  parseQuery,
  changeQuery,
  urlParameters,
} from './liste-sorgusu';

export interface DegisiklikSecenekleri {
  /**
   * Kullanıcı yazıyor (arama kutusu): URL `replaceUrl` ile güncellenir, her tuş vuruşu geçmişe
   * girmez. Sayfa/sıralama gibi ayrık adımlar geçmişe girer (geri tuşu önceki sayfaya döner).
   */
  readonly yaziyor?: boolean;
}

export interface ListQueryUrlSync<K extends FilterCatalog> {
  /** URL'den okunan (tek doğruluk kaynağı), normalize edilmiş sorgu. Yalnız anlamca değişince bildirir. */
  readonly sorgu: Signal<ListeSorgusu<K>>;
  /** `sorgu`'nun API parametreleri (`ApiIstemcisi` `parametreler`'ine doğrudan verilir). */
  readonly apiParametreleri: Signal<QueryParameters>;
  readonly etkinFiltreSayisi: Signal<number>;
  /** Değişikliği URL'e yazar; `sorgu` gezinme bitince güncellenir. */
  degistir(change: SorguDegisikligi<K>, option?: DegisiklikSecenekleri): Promise<boolean>;
  /** Filtreleri, sıralamayı ve sayfalamayı varsayılana döndürür. */
  sifirla(): Promise<boolean>;
}

/**
 * Liste sorgusu ↔ URL iki yönlü senkronu. Sayfa bileşeninin injection context'inde çağrılır.
 *
 * Tek doğruluk kaynağı URL'dir: durum `ActivatedRoute.queryParamMap`'ten türetilir, değişiklikler
 * `router.navigate` ile yazılır — döngü yok, geri/ileri tuşu ve paylaşılan bağlantı aynı listeyi açar.
 * Yazılan her değer katalogdan geçer; URL'de yalnız varsayılandan farklı değerler durur.
 * Yönetilmeyen sorgu parametreleri (ör. `bilgi`, `#sekme=`) korunur (`queryParamsHandling: 'merge'`).
 */
export function listQueryUrlSync<K extends FilterCatalog>(
  definition: ListeTanimi<K>,
): ListQueryUrlSync<K> {
  const router = inject(Router);
  const route = inject(ActivatedRoute);

  const query = toSignal(route.queryParamMap.pipe(map((p) => parseQuery(definition, p))), {
    requireSync: true,
    equal: (a, b) => queryKey(definition, a) === queryKey(definition, b),
  });

  // Aynı tikte art arda gelen değişiklikler (ör. iki filtre) gezinme bitmeden birbirini ezmesin:
  // bekleyen değişiklik bir sonrakinin tabanı olur.
  let pending: ListeSorgusu<K> | null = null;

  const write = (newItem: ListeSorgusu<K>, isTyping: boolean): Promise<boolean> => {
    pending = newItem;
    return router
      .navigate([], {
        relativeTo: route,
        queryParams: urlParameters(definition, newItem),
        queryParamsHandling: 'merge',
        preserveFragment: true,
        replaceUrl: isTyping,
      })
      .finally(() => {
        if (pending === newItem) pending = null;
      });
  };

  return {
    sorgu: query,
    apiParametreleri: computed(() => apiParams(definition, query())),
    etkinFiltreSayisi: computed(() => activeFilterCount(definition, query())),
    degistir: (change, option) =>
      write(changeQuery(definition, pending ?? query(), change), option?.yaziyor ?? false),
    sifirla: () => write(parseQuery(definition, {}), false),
  };
}
