import { trSearchKey } from '@core/metin/tr-normalize';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

import type { MenuTarget, MenuKaydi } from './menu-modeli';

/**
 * Menü grubu → ikon (Yol v2 §5.1). Grup adları sunucu kaydından (`MenuKaydi.cs`) gelir; eşleşme Türkçe-gevşek
 * anahtarla (büyük/küçük harf, İ/ı farkı yok). Bilinmeyen grup genel ikonla çizilir — menü bozulmaz.
 */
const GROUP_ICONS: ReadonlyMap<string, IkonAdi> = new Map<string, IkonAdi>(
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
  ).map(([name, icon]) => [trSearchKey(name), icon]),
);

/** Grupsuz öğeler: rota (sonu) → ikon. */
const ITEM_ICONS: ReadonlyMap<string, IkonAdi> = new Map<string, IkonAdi>([
  ['/', 'home'],
  ['/panel', 'home'],
  ['/bildirimler', 'bell'],
]);

const DEFAULT: IkonAdi = 'file-text';

export function groupIcon(group: string): IkonAdi {
  return GROUP_ICONS.get(trSearchKey(group)) ?? DEFAULT;
}

export function itemIcon(record: Pick<MenuKaydi, 'hedef'>): IkonAdi {
  return ITEM_ICONS.get(targetPath(record.hedef)) ?? DEFAULT;
}

function targetPath(target: MenuTarget): string {
  return target.tur === 'spa' ? target.yol : target.adres;
}
