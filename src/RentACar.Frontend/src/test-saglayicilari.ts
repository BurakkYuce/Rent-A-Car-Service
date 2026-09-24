/** OTOMATİK ÜRETİLDİ: scripts/i18n-tipleri.mjs. Elle düzenlemeyin. */
import type { Provider } from '@angular/core';
import { ONYUKLU_CEVIRI_BLOKLARI } from './app/core/i18n/onyuklu-ceviri';
import arac from './i18n/bloklar/arac.json';
import aracFinans from './i18n/bloklar/arac-finans.json';
import kiraFormu from './i18n/bloklar/kira-formu.json';
import kiralar from './i18n/bloklar/kiralar.json';
import panel from './i18n/bloklar/panel.json';
import planlama from './i18n/bloklar/planlama.json';
import platform from './i18n/bloklar/platform.json';
import rezervasyon from './i18n/bloklar/rezervasyon.json';
import vitrin from './i18n/bloklar/vitrin.json';

/**
 * Birim testleri (angular.json `test.providersFile`): bileşen/servis testleri rotadan geçmediği için tüm
 * özellik çeviri blokları çekirdekle birlikte eşzamanlı yüklenir. Uygulama bu dosyayı İÇE AKTARMAZ; üretimde
 * bloklar rotada `ceviriBlogu(...)` ile tembel gelir (yükleme davranışı `ceviri-blogu.spec.ts`'te).
 */
const saglayicilar: Provider[] = [
  {
    provide: ONYUKLU_CEVIRI_BLOKLARI,
    useValue: [
      arac,
      aracFinans,
      kiraFormu,
      kiralar,
      panel,
      planlama,
      platform,
      rezervasyon,
      vitrin,
    ],
  },
];
export default saglayicilar;
