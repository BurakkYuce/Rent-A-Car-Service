import { InjectionToken } from '@angular/core';
import type { Translation } from '@jsverse/transloco';

/**
 * YALNIZ birim testleri sağlar (`src/test-saglayicilari.ts`, üretilir; bu dosya Transloco'yu çalışma zamanında
 * içe aktarmaz — test ortamı kurulurken derleyici henüz yok): çekirdekle birlikte eşzamanlı
 * yüklenecek özellik blokları. Bileşen testi rotadan geçmez; blok rotada yüklendiği için burada önyüklenir.
 * Uygulama bunu SAĞLAMAZ — üretimde bloklar yalnız `ceviriBlogu` ile gelir.
 */
export const ONYUKLU_CEVIRI_BLOKLARI = new InjectionToken<readonly Translation[]>(
  'ONYUKLU_CEVIRI_BLOKLARI',
  { factory: () => [] },
);
