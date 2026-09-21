import { Signal, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';

import { SorguParametreleri } from '@core/api/api-istemcisi';
import {
  FiltreKatalogu,
  ListeSorgusu,
  ListeTanimi,
  SorguDegisikligi,
  apiParametreleri,
  etkinFiltreSayisi,
  sorguAnahtari,
  sorguyuCoz,
  sorguyuDegistir,
  urlParametreleri,
} from './liste-sorgusu';

export interface DegisiklikSecenekleri {
  /**
   * Kullanıcı yazıyor (arama kutusu): URL `replaceUrl` ile güncellenir, her tuş vuruşu geçmişe
   * girmez. Sayfa/sıralama gibi ayrık adımlar geçmişe girer (geri tuşu önceki sayfaya döner).
   */
  readonly yaziyor?: boolean;
}

export interface ListeSorgusuUrlSenkronu<K extends FiltreKatalogu> {
  /** URL'den okunan (tek doğruluk kaynağı), normalize edilmiş sorgu. Yalnız anlamca değişince bildirir. */
  readonly sorgu: Signal<ListeSorgusu<K>>;
  /** `sorgu`'nun API parametreleri (`ApiIstemcisi` `parametreler`'ine doğrudan verilir). */
  readonly apiParametreleri: Signal<SorguParametreleri>;
  readonly etkinFiltreSayisi: Signal<number>;
  /** Değişikliği URL'e yazar; `sorgu` gezinme bitince güncellenir. */
  degistir(degisiklik: SorguDegisikligi<K>, secenek?: DegisiklikSecenekleri): Promise<boolean>;
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
export function listeSorgusuUrlSenkronu<K extends FiltreKatalogu>(
  tanim: ListeTanimi<K>,
): ListeSorgusuUrlSenkronu<K> {
  const router = inject(Router);
  const route = inject(ActivatedRoute);

  const sorgu = toSignal(route.queryParamMap.pipe(map((p) => sorguyuCoz(tanim, p))), {
    requireSync: true,
    equal: (a, b) => sorguAnahtari(tanim, a) === sorguAnahtari(tanim, b),
  });

  // Aynı tikte art arda gelen değişiklikler (ör. iki filtre) gezinme bitmeden birbirini ezmesin:
  // bekleyen değişiklik bir sonrakinin tabanı olur.
  let bekleyen: ListeSorgusu<K> | null = null;

  const yaz = (yeni: ListeSorgusu<K>, yaziyor: boolean): Promise<boolean> => {
    bekleyen = yeni;
    return router
      .navigate([], {
        relativeTo: route,
        queryParams: urlParametreleri(tanim, yeni),
        queryParamsHandling: 'merge',
        preserveFragment: true,
        replaceUrl: yaziyor,
      })
      .finally(() => {
        if (bekleyen === yeni) bekleyen = null;
      });
  };

  return {
    sorgu,
    apiParametreleri: computed(() => apiParametreleri(tanim, sorgu())),
    etkinFiltreSayisi: computed(() => etkinFiltreSayisi(tanim, sorgu())),
    degistir: (degisiklik, secenek) =>
      yaz(sorguyuDegistir(tanim, bekleyen ?? sorgu(), degisiklik), secenek?.yaziyor ?? false),
    sifirla: () => yaz(sorguyuCoz(tanim, {}), false),
  };
}
