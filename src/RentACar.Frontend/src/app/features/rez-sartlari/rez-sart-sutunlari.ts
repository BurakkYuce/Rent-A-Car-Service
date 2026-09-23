import { tarihBicimle } from '@core/bicim/bicim';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import type { RezSart } from './rez-sart-modeli';

type Ceviri = (anahtar: CeviriAnahtari, parametreler?: Record<string, unknown>) => string;

export type SutunKodu =
  'talepTarihi' | 'musteri' | 'grup' | 'sart' | 'gecerlilik' | 'durum' | 'teslimEden' | 'islemler';

/** Geçerlilik aralığı (Blazor `Aralik`): iki uç, tek uç ya da yok. İstanbul günü. */
export function gecerlilikMetni(s: Pick<RezSart, 'basTar' | 'bitTar'>): string | null {
  const b = s.basTar ? tarihBicimle(s.basTar) : '';
  const t = s.bitTar ? tarihBicimle(s.bitTar) : '';
  if (b && t) return `${b} – ${t}`;
  if (b) return `${b} –`;
  if (t) return `– ${t}`;
  return null;
}

/** Blazor RezSartList tablosunun sütunları; sıralanabilirler sunucu `SiralamaHaritasi` ile aynı. */
export function rezSartSutunlari(t: Ceviri): readonly TabloSutunu<RezSart>[] {
  const s = (kod: SutunKodu) => t(`rezSartlari.sutun.${kod}`);
  return [
    {
      kod: 'talepTarihi',
      baslik: s('talepTarihi'),
      deger: (r) => r.talepTarihi,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    {
      kod: 'musteri',
      baslik: s('musteri'),
      deger: (r) => r.musteriAd,
      sirala: true,
      genislik: 170,
    },
    { kod: 'grup', baslik: s('grup'), deger: (r) => r.grup, sirala: true, genislik: 110 },
    { kod: 'sart', baslik: s('sart'), deger: (r) => r.sart, sirala: true, genislik: 240 },
    { kod: 'gecerlilik', baslik: s('gecerlilik'), deger: gecerlilikMetni, genislik: 180 },
    {
      kod: 'durum',
      baslik: s('durum'),
      deger: (r) => r.karsilamaTarihi,
      sirala: 'karsilamaTarihi',
      genislik: 170,
    },
    { kod: 'teslimEden', baslik: s('teslimEden'), deger: (r) => r.teslimEden, genislik: 120 },
    { kod: 'islemler', baslik: s('islemler'), deger: () => null, gizlenemez: true, genislik: 250 },
  ];
}
