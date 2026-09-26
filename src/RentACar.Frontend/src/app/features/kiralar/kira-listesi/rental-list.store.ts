import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { RentalFilterOptions, RentalListSummary, RentalListRow } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { listDefinition } from '@core/veri/liste-sorgusu';
import { TemelStore } from '@core/veri/temel-store';

/** Sunucu enum ADLARI (`RentalStatus`, `TarihListesiTuru`, `OfisDurumu`) — API tanımsız adı 400'ler. */
export const RENTAL_STATUSES = ['Kirada', 'Tamamlandi', 'Iptal'] as const;
export const DATE_TYPES = ['Baslangic', 'Bitis', 'Islem', 'Vade'] as const;
export const OFFICE_STATUSES = ['Cikis', 'Donus'] as const;

export type RentalStatus = (typeof RENTAL_STATUSES)[number];
export type DateType = (typeof DATE_TYPES)[number];
export type OfficeStatus = (typeof OFFICE_STATUSES)[number];

/**
 * Kira listesi URL ↔ API sözleşmesi (F4.1 `KiraApi.KiraListeFiltresi` + `SiralamaHaritasi`). Parametre
 * adları API ile BİREBİR (URL = API); Blazor RentalList süzgeçlerinin tamamı (FAZ-46 dahil).
 * Varsayılan sıralama YOK: `sirala` gönderilmezse sunucu Blazor listesinin sırasını (oluşturma zamanı,
 * yeniden eskiye) korur.
 */
export const RENTAL_LIST = listDefinition({
  filtreler: {
    q: { tur: 'metin' },
    durum: { tur: 'secim', degerler: RENTAL_STATUSES },
    fatura: { tur: 'bayrak' },
    tarihTuru: { tur: 'secim', degerler: DATE_TYPES },
    basMin: { tur: 'tarih' },
    basMax: { tur: 'tarih' },
    ofis: { tur: 'metin' },
    ofisDurum: { tur: 'secim', degerler: OFFICE_STATUSES },
    sahip: { tur: 'metin' },
    grup: { tur: 'metin' },
    kaynak: { tur: 'metin' },
    personelId: { tur: 'kimlik' },
  },
  siralanabilir: [
    'sozlesmeNo',
    'musteri',
    'plaka',
    'basTar',
    'bitTar',
    'vadeTar',
    'gun',
    'tutar',
    'bakiye',
    'durum',
    'kaynak',
    'cikisOfisi',
    'donusOfisi',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** Liste parametrelerinden yalnız süzgeçler (özet ucu sayfa/sıralama almaz). */
export function summaryParameters(p: QueryParameters): QueryParameters {
  const result: Record<string, QueryParameters[string]> = {};
  for (const [name, value] of Object.entries(p)) {
    if (name !== 'sayfa' && name !== 'boyut' && name !== 'sirala') result[name] = value;
  }
  return result;
}

/**
 * Sayfa düzeyi store (sayfanın `providers`'ında). Üç kaynak: liste (sunucu sayfalı), özet satırı ("N
 * sözleşme • N kirada • N faturasız", AYNI süzgeçle) ve sahip/grup öneri listeleri. Özet ve öneriler
 * SESSİZ: onların hatası liste hatasının yanında ikinci bant/toast üretmez (özet satırı gizlenir).
 */
@Injectable()
export class RentalListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<RentalListRow>>('/api/ui/v1/kiralar', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly ozet = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<RentalListSummary>('/api/ui/v1/kiralar/ozet', {
        parametreler: p,
        context: requestContext({ sessiz: true }),
      }),
    { oncekiVeriyiKoru: true },
  );

  readonly options = new TemelStore(() =>
    this.api.get<RentalFilterOptions>('/api/ui/v1/kiralar/filtre-secenekleri', {
      context: requestContext({ sessiz: true }),
    }),
  );
}
