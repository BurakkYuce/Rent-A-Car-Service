import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import { listDefinition } from '@core/veri/liste-sorgusu';
import { TemelStore } from '@core/veri/temel-store';

import {
  RESERVATION_STATUSES,
  RESERVATION_ROOT,
  type ReservationListRow,
} from '../rezervasyon-modeli';

/**
 * Rezervasyon listesi URL ↔ API sözleşmesi (F5.1 `RezervasyonApi.RezervasyonListeFiltresi` + `SiralamaHaritasi`).
 * Parametre adları API ile BİREBİR (URL = API); Blazor ReservationList süzgeçlerinin tamamı (FAZ-48: ara, durum,
 * başlangıç aralığı, kaynak). Varsayılan sıralama YOK: sunucu servis sırasını korur.
 */
export const RESERVATION_LIST = listDefinition({
  filtreler: {
    q: { tur: 'metin' },
    durum: { tur: 'secim', degerler: RESERVATION_STATUSES },
    basMin: { tur: 'tarih' },
    basMax: { tur: 'tarih' },
    kaynak: { tur: 'metin' },
  },
  siralanabilir: [
    'no',
    'musteri',
    'plaka',
    'basTar',
    'bitTar',
    'cikisOfisi',
    'kaynak',
    'gun',
    'tutar',
    'durum',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/**
 * Dışa aktarma parametreleri: Blazor liste export ucu (`/listeler/export/rezervasyonlar`) FAZ-48 adlarını okur
 * (`ara`, `durum`, `bas`, `bit`, `kaynak`) — ekrandaki süzgeç AYNEN taşınır (gördüğün = indirdiğin). Sayfa ve
 * sıralama taşınmaz (uç sayfasız ve kendi sırasıyla yazar).
 */
export function exportParams(p: QueryParameters): QueryParameters {
  const value = (name: string) => {
    const v = p[name];
    return typeof v === 'string' && v !== '' ? v : undefined;
  };
  return {
    ara: value('q'),
    durum: value('durum'),
    bas: value('basMin'),
    bit: value('basMax'),
    kaynak: value('kaynak'),
  };
}

/** Sayfa düzeyi store (sayfanın `providers`'ında): sunucu sayfalı rezervasyon listesi. */
@Injectable()
export class ReservationListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<ReservationListRow>>(RESERVATION_ROOT, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}
