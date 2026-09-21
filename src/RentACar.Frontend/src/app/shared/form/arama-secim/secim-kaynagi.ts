import { inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { SecimUcu, SecimUcuOgesi } from '@core/api/ui-tipleri';

export type { SecimUcu, SecimUcuOgesi } from '@core/api/ui-tipleri';

/**
 * Aranabilir seçimin öğesi: her F1.6 ucunda ortak `id` + `etiket` (PII yok). Uca özgü alanlar
 * (`kod`, `plaka`, `tip`, `subeId`…) üretilen tipte (`SecimUcuOgesi<'arac'>`) durur.
 */
export interface SecimSecenegi {
  readonly id: string;
  readonly etiket: string;
}

/** Sunucu sözleşmesi: `limit` varsayılan ve en çok 20. */
export const SECIM_AZAMI_LIMIT = 20;

/** `(arama, limit) → öğeler`. Test ve yerel listeler için elle de yazılabilir. */
export type SecimKaynagi<T extends SecimSecenegi = SecimSecenegi> = (
  arama: string,
  limit: number,
) => Observable<readonly T[]>;

/**
 * Sunucu seçim kaynağı (`GET /api/ui/v1/secim/{uc}?q=&limit=`). Enjeksiyon bağlamında çağrılır:
 * `protected readonly musteriler = sunucuSecimKaynagi('musteri');` — öğe tipi sözleşmeden gelir.
 */
export function sunucuSecimKaynagi<U extends SecimUcu>(
  uc: U,
  ek?: SorguParametreleri,
): SecimKaynagi<SecimUcuOgesi<U>> {
  const api = inject(ApiIstemcisi);
  return (arama, limit) =>
    api.get<readonly SecimUcuOgesi<U>[]>(`/api/ui/v1/secim/${uc}`, {
      parametreler: {
        ...ek,
        q: arama.trim() === '' ? null : arama.trim(),
        limit: Math.min(Math.max(1, Math.trunc(limit)), SECIM_AZAMI_LIMIT),
      },
    });
}
