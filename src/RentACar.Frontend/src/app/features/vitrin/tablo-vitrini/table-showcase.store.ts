import { Injectable } from '@angular/core';

import type { QueryParameters } from '@core/api/api-istemcisi';
import { TemelStore } from '@core/veri/temel-store';
import { listDefinition } from '@core/veri/liste-sorgusu';

import { SCENARIOS, fakeVehicleEndpoint } from './arac-verisi';

/** URL ↔ sorgu kataloğu (F3.4). `senaryo` yalnız vitrin içindir: boş / hata / yavaş durumlarını göstermek. */
export const VEHICLE_SHOWCASE_LIST = listDefinition({
  filtreler: { senaryo: { tur: 'secim', degerler: SCENARIOS } },
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
export class TableShowcaseStore {
  readonly liste = new TemelStore((p: QueryParameters) => fakeVehicleEndpoint(p), {
    oncekiVeriyiKoru: true,
  });
}
