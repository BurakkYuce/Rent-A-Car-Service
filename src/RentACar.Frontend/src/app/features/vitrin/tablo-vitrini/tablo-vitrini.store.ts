import { Injectable } from '@angular/core';

import type { SorguParametreleri } from '@core/api/api-istemcisi';
import { TemelStore } from '@core/veri/temel-store';
import { listeTanimi } from '@core/veri/liste-sorgusu';

import { SENARYOLAR, sahteAracUcu } from './arac-verisi';

/** URL ↔ sorgu kataloğu (F3.4). `senaryo` yalnız vitrin içindir: boş / hata / yavaş durumlarını göstermek. */
export const ARAC_VITRIN_LISTESI = listeTanimi({
  filtreler: { senaryo: { tur: 'secim', degerler: SENARYOLAR } },
  siralanabilir: [
    'plaka',
    'marka',
    'model',
    'modelYili',
    'yakit',
    'segment',
    'sube',
    'durum',
    'km',
    'gunlukFiyat',
    'haftalikFiyat',
    'aylikFiyat',
    'alisBedeli',
    'alisTarihi',
    'kaskoBitis',
    'kiraSayisi',
    'doluluk',
    'toplamGelir',
    'netKar',
  ],
  varsayilanSirala: 'plaka',
  varsayilanBoyut: 100,
});

/** Sayfa düzeyi store: veri `TemelStore` ile (dört durum, son istek kazanır). */
@Injectable()
export class TabloVitriniStore {
  readonly liste = new TemelStore((p: SorguParametreleri) => sahteAracUcu(p), {
    oncekiVeriyiKoru: true,
  });
}
