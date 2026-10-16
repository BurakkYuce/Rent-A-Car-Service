import type { Schema } from '@core/api/ui-tipleri';

export type KabisStatus = Schema<'KabisDurumDto'>;
export type KabisNotice = Schema<'KabisBildirimDto'>;

/** Rozet tonu — renk = durum (Yol tasarım dili): bildirildi yeşil, bekliyor sarı, hata kırmızı, kapalı nötr. */
export type KabisTone = 'basari' | 'uyari' | 'hata' | 'notr';

export const KABIS_STATUSES = ['Bekliyor', 'Bildirildi', 'Hata', 'GerekliDegil'] as const;
export const KABIS_TYPES = ['Kiralama', 'Iade', 'Iptal'] as const;

export function kabisTone(status: string): KabisTone {
  switch (status) {
    case 'Bildirildi':
      return 'basari';
    case 'Bekliyor':
      return 'uyari';
    case 'Hata':
      return 'hata';
    default:
      return 'notr';
  }
}

/** Operatör işlemi (yeniden dene / gerekli değil / elle bildirildi) yalnız açık kayıtta anlamlı. */
export function kabisActionable(n: Pick<KabisNotice, 'durum'>): boolean {
  return n.durum === 'Bekliyor' || n.durum === 'Hata';
}

/** Bilinmeyen (gelecekte eklenecek) değer çeviri anahtarına dönüşmez — ham değer gösterilir. */
export function knownStatus(status: string): status is (typeof KABIS_STATUSES)[number] {
  return (KABIS_STATUSES as readonly string[]).includes(status);
}

export function knownType(type: string): type is (typeof KABIS_TYPES)[number] {
  return (KABIS_TYPES as readonly string[]).includes(type);
}

/** OpenAPI tamsayıyı `number | string` üretir (JSON sayısı ya da metin); ekranda sayı. */
export function attemptCount(value: number | string): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

/** Bekleyen ya da hatalı kayıt var mı (kartın uyarı kenarı, panel sayacıyla aynı tanım). */
export function kabisHasProblem(status: KabisStatus | null): boolean {
  return status?.etkin === true && status.bildirimler.some(kabisActionable);
}
