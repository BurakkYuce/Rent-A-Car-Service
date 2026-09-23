import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import { listeTanimi } from '@core/veri/liste-sorgusu';
import { TemelStore } from '@core/veri/temel-store';

import {
  REZERVASYON_DURUMLARI,
  REZERVASYON_KOKU,
  type RezervasyonListeSatiri,
} from '../rezervasyon-modeli';

/**
 * Rezervasyon listesi URL ↔ API sözleşmesi (F5.1 `RezervasyonApi.RezervasyonListeFiltresi` + `SiralamaHaritasi`).
 * Parametre adları API ile BİREBİR (URL = API); Blazor ReservationList süzgeçlerinin tamamı (FAZ-48: ara, durum,
 * başlangıç aralığı, kaynak). Varsayılan sıralama YOK: sunucu servis sırasını korur.
 */
export const REZERVASYON_LISTESI = listeTanimi({
  filtreler: {
    q: { tur: 'metin' },
    durum: { tur: 'secim', degerler: REZERVASYON_DURUMLARI },
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
export function disaAktarmaParametreleri(p: SorguParametreleri): SorguParametreleri {
  const deger = (ad: string) => {
    const v = p[ad];
    return typeof v === 'string' && v !== '' ? v : undefined;
  };
  return {
    ara: deger('q'),
    durum: deger('durum'),
    bas: deger('basMin'),
    bit: deger('basMax'),
    kaynak: deger('kaynak'),
  };
}

/** Sayfa düzeyi store (sayfanın `providers`'ında): sunucu sayfalı rezervasyon listesi. */
@Injectable()
export class RezervasyonListesiStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<RezervasyonListeSatiri>>(REZERVASYON_KOKU, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}
