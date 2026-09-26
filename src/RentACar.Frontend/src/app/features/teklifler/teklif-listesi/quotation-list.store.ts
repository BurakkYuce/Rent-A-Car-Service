import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { listDefinition } from '@core/veri/liste-sorgusu';
import { TemelStore } from '@core/veri/temel-store';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { QUOTATION_STATUSES, QUOTATION_ROOT, type QuotationListRow } from '../teklif-modeli';

/**
 * Teklif listesi URL ↔ API sözleşmesi (F5.1 `TeklifApi.Liste`: isteğe bağlı `durum` + `SiralamaHaritasi`).
 * Blazor QuotationList'te süzgeç yok; API'nin durum süzgeci SPA'da açıldı (ek, parite kaybı yok).
 */
export const QUOTATION_LIST = listDefinition({
  filtreler: {
    durum: { tur: 'secim', degerler: QUOTATION_STATUSES },
  },
  siralanabilir: ['no', 'musteri', 'plaka', 'basTar', 'gun', 'tutar', 'gecerlilikTarihi', 'durum'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

@Injectable()
export class QuotationListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<QuotationListRow>>(QUOTATION_ROOT, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

type Translation = (key: CeviriAnahtari, parameters?: Record<string, unknown>) => string;

export type QuotationColumn =
  | 'no'
  | 'musteri'
  | 'plaka'
  | 'basTar'
  | 'bitTar'
  | 'gun'
  | 'tutar'
  | 'gecerlilik'
  | 'durum'
  | 'islemler';

const toNumber = (v: number | string | null | undefined): number | null => {
  const n = typeof v === 'string' && v.trim() !== '' ? Number(v) : v;
  return typeof n === 'number' && Number.isFinite(n) ? n : null;
};

/**
 * Blazor QuotationList sütunları (No, Müşteri, Araç, Tarih, Gün, Tutar, Geçerlilik, Durum). Blazor'un birleşik
 * "Tarih" hücresi başlangıç/bitiş olarak ayrıldı (bilgi kaybı yok; sıralanabilir başlangıç). `kod`'lar KALICI.
 */
export function quotationColumns(t: Translation): readonly TabloSutunu<QuotationListRow>[] {
  const s = (code: QuotationColumn) => t(`teklif.sutun.${code}`);
  return [
    { kod: 'no', baslik: s('no'), deger: (r) => r.no, sirala: true, sabit: true, genislik: 140 },
    {
      kod: 'musteri',
      baslik: s('musteri'),
      deger: (r) => r.musteriAd,
      sirala: true,
      genislik: 180,
    },
    { kod: 'plaka', baslik: s('plaka'), deger: (r) => r.plaka, sirala: true, genislik: 124 },
    {
      kod: 'basTar',
      baslik: s('basTar'),
      deger: (r) => r.basTar,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    { kod: 'bitTar', baslik: s('bitTar'), deger: (r) => r.bitTar, tur: 'tarih', genislik: 100 },
    {
      kod: 'gun',
      baslik: s('gun'),
      deger: (r) => toNumber(r.gun),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 64,
    },
    {
      kod: 'tutar',
      baslik: s('tutar'),
      deger: (r) => toNumber(r.tutar),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'gecerlilik',
      baslik: s('gecerlilik'),
      deger: (r) => r.gecerlilikTarihi,
      tur: 'tarih',
      sirala: 'gecerlilikTarihi',
      genislik: 100,
    },
    { kod: 'durum', baslik: s('durum'), deger: (r) => r.durum, sirala: true, genislik: 110 },
    { kod: 'islemler', baslik: s('islemler'), deger: () => null, gizlenemez: true, genislik: 300 },
  ];
}
