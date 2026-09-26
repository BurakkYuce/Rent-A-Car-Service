import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { type FleetListRow, count } from './filo-modeli';

type Translation = (key: CeviriAnahtari, parameters?: Record<string, unknown>) => string;

export type ColumnCode =
  | 'no'
  | 'sozlesmeNo'
  | 'musteri'
  | 'plaka'
  | 'basTar'
  | 'sureAy'
  | 'aylikUcret'
  | 'genelToplam'
  | 'satisTemsilcisi'
  | 'kaynak'
  | 'vadeGun'
  | 'durum';

/**
 * Blazor FiloKiralamaList tablosunun sütunları. Genel toplam SUNUCUNUN taksit planından (UI formül
 * taşımaz); tutarlar sözleşmenin dövizinde. Sıralanabilirler `SiralamaHaritasi` ile aynı.
 */
export function fleetColumns(t: Translation): readonly TabloSutunu<FleetListRow>[] {
  const s = (code: ColumnCode) => t(`filoKiralama.sutun.${code}`);
  const currency = (r: FleetListRow) => r.doviz || 'TRY';
  return [
    { kod: 'no', baslik: s('no'), deger: (r) => r.no, sirala: true, sabit: true, genislik: 130 },
    { kod: 'sozlesmeNo', baslik: s('sozlesmeNo'), deger: (r) => r.sozlesmeNo, genislik: 110 },
    {
      kod: 'musteri',
      baslik: s('musteri'),
      deger: (r) => r.musteriAd,
      sirala: true,
      genislik: 170,
    },
    { kod: 'plaka', baslik: s('plaka'), deger: (r) => r.plaka, sirala: true, genislik: 100 },
    {
      kod: 'basTar',
      baslik: s('basTar'),
      deger: (r) => r.basTar,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    {
      kod: 'sureAy',
      baslik: s('sureAy'),
      deger: (r) => count(r.sureAy),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 70,
    },
    {
      kod: 'aylikUcret',
      baslik: s('aylikUcret'),
      deger: (r) => count(r.aylikUcret),
      tur: 'para',
      paraBirimi: currency,
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'genelToplam',
      baslik: s('genelToplam'),
      deger: (r) => count(r.genelToplam),
      tur: 'para',
      paraBirimi: currency,
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'satisTemsilcisi',
      baslik: s('satisTemsilcisi'),
      deger: (r) => r.satisTemsilcisi,
      genislik: 130,
    },
    { kod: 'kaynak', baslik: s('kaynak'), deger: (r) => r.kaynak, genislik: 100 },
    {
      kod: 'vadeGun',
      baslik: s('vadeGun'),
      deger: (r) => count(r.vadeGun),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 80,
    },
    { kod: 'durum', baslik: s('durum'), deger: (r) => r.durum, sirala: true, genislik: 110 },
  ];
}
