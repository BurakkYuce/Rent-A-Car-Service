import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { KiraFiltreSecenekleri, KiraListeOzeti, KiraListeSatiri } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { listeTanimi } from '@core/veri/liste-sorgusu';
import { TemelStore } from '@core/veri/temel-store';

/** Sunucu enum ADLARI (`RentalStatus`, `TarihListesiTuru`, `OfisDurumu`) — API tanımsız adı 400'ler. */
export const KIRA_DURUMLARI = ['Kirada', 'Tamamlandi', 'Iptal'] as const;
export const TARIH_TURLERI = ['Baslangic', 'Bitis', 'Islem', 'Vade'] as const;
export const OFIS_DURUMLARI = ['Cikis', 'Donus'] as const;

export type KiraDurumu = (typeof KIRA_DURUMLARI)[number];
export type TarihTuru = (typeof TARIH_TURLERI)[number];
export type OfisDurumu = (typeof OFIS_DURUMLARI)[number];

/**
 * Kira listesi URL ↔ API sözleşmesi (F4.1 `KiraApi.KiraListeFiltresi` + `SiralamaHaritasi`). Parametre
 * adları API ile BİREBİR (URL = API); Blazor RentalList süzgeçlerinin tamamı (FAZ-46 dahil).
 * Varsayılan sıralama YOK: `sirala` gönderilmezse sunucu Blazor listesinin sırasını (oluşturma zamanı,
 * yeniden eskiye) korur.
 */
export const KIRA_LISTESI = listeTanimi({
  filtreler: {
    q: { tur: 'metin' },
    durum: { tur: 'secim', degerler: KIRA_DURUMLARI },
    fatura: { tur: 'bayrak' },
    tarihTuru: { tur: 'secim', degerler: TARIH_TURLERI },
    basMin: { tur: 'tarih' },
    basMax: { tur: 'tarih' },
    ofis: { tur: 'metin' },
    ofisDurum: { tur: 'secim', degerler: OFIS_DURUMLARI },
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
export function ozetParametreleri(p: SorguParametreleri): SorguParametreleri {
  const sonuc: Record<string, SorguParametreleri[string]> = {};
  for (const [ad, deger] of Object.entries(p)) {
    if (ad !== 'sayfa' && ad !== 'boyut' && ad !== 'sirala') sonuc[ad] = deger;
  }
  return sonuc;
}

/**
 * Sayfa düzeyi store (sayfanın `providers`'ında). Üç kaynak: liste (sunucu sayfalı), özet satırı ("N
 * sözleşme • N kirada • N faturasız", AYNI süzgeçle) ve sahip/grup öneri listeleri. Özet ve öneriler
 * SESSİZ: onların hatası liste hatasının yanında ikinci bant/toast üretmez (özet satırı gizlenir).
 */
@Injectable()
export class KiraListesiStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<KiraListeSatiri>>('/api/ui/v1/kiralar', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly ozet = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<KiraListeOzeti>('/api/ui/v1/kiralar/ozet', {
        parametreler: p,
        context: istekBaglami({ sessiz: true }),
      }),
    { oncekiVeriyiKoru: true },
  );

  readonly secenekler = new TemelStore(() =>
    this.api.get<KiraFiltreSecenekleri>('/api/ui/v1/kiralar/filtre-secenekleri', {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}
