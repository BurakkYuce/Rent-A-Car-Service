import { trAramaAnahtari } from '@core/metin/tr-normalize';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

import type { MenuHedefi, MenuKaydi } from './menu-modeli';

/**
 * Menü grubu → ikon (Yol v2 §5.1). Grup adları sunucu kaydından (`MenuKaydi.cs`) gelir; eşleşme Türkçe-gevşek
 * anahtarla (büyük/küçük harf, İ/ı farkı yok). Bilinmeyen grup genel ikonla çizilir — menü bozulmaz.
 */
const GRUP_IKONLARI: ReadonlyMap<string, IkonAdi> = new Map<string, IkonAdi>(
  (
    [
      ['Araçlar', 'car'],
      ['Kira', 'key'],
      ['Rezervasyon', 'calendar-event'],
      ['Cariler & CRM', 'users'],
      ['Web Sitesi', 'world'],
      ['Servis & Sigorta', 'tool'],
      ['Servis', 'tool'],
      ['Fiyat & Tarife', 'tag'],
      ['Finans', 'coin'],
      ['Raporlar', 'chart-bar'],
      ['Tanımlar', 'adjustments-horizontal'],
      ['Sistem', 'shield'],
    ] as const
  ).map(([ad, ikon]) => [trAramaAnahtari(ad), ikon]),
);

/** Grupsuz öğeler: rota (sonu) → ikon. */
const OGE_IKONLARI: ReadonlyMap<string, IkonAdi> = new Map<string, IkonAdi>([
  ['/', 'home'],
  ['/panel', 'home'],
  ['/bildirimler', 'bell'],
]);

const VARSAYILAN: IkonAdi = 'file-text';

export function grupIkonu(grup: string): IkonAdi {
  return GRUP_IKONLARI.get(trAramaAnahtari(grup)) ?? VARSAYILAN;
}

export function ogeIkonu(kayit: Pick<MenuKaydi, 'hedef'>): IkonAdi {
  return OGE_IKONLARI.get(hedefYolu(kayit.hedef)) ?? VARSAYILAN;
}

function hedefYolu(hedef: MenuHedefi): string {
  return hedef.tur === 'spa' ? hedef.yol : hedef.adres;
}
