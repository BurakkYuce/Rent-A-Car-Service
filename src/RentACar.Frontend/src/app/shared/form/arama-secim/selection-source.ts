import { inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { SelectionEndpoint, SelectionEndpointItem } from '@core/api/ui-tipleri';

export type {
  SelectionEndpoint as SecimUcu,
  SelectionEndpointItem as SecimUcuOgesi,
} from '@core/api/ui-tipleri';

/**
 * Aranabilir seçimin öğesi: her F1.6 ucunda ortak `id` + `etiket` (PII yok). Uca özgü alanlar
 * (`kod`, `plaka`, `tip`, `subeId`…) üretilen tipte (`SecimUcuOgesi<'arac'>`) durur.
 */
export interface SecimSecenegi {
  readonly id: string;
  readonly etiket: string;
}

/** Sunucu sözleşmesi: `limit` varsayılan ve en çok 20. */
export const SELECTION_MAX_LIMIT = 20;

/** `(arama, limit) → öğeler`. Test ve yerel listeler için elle de yazılabilir. */
export type SelectionSource<T extends SecimSecenegi = SecimSecenegi> = (
  search: string,
  limit: number,
) => Observable<readonly T[]>;

/**
 * Sunucu seçim kaynağı (`GET /api/ui/v1/secim/{uc}?q=&limit=`). Enjeksiyon bağlamında çağrılır:
 * `protected readonly musteriler = sunucuSecimKaynagi('musteri');` — öğe tipi sözleşmeden gelir.
 */
export function serverSelectionSource<U extends SelectionEndpoint>(
  endpoint: U,
  extra?: QueryParameters,
): SelectionSource<SelectionEndpointItem<U>> {
  const api = inject(ApiIstemcisi);
  return (search, limit) =>
    api.get<readonly SelectionEndpointItem<U>[]>(`/api/ui/v1/secim/${endpoint}`, {
      parametreler: {
        ...extra,
        q: search.trim() === '' ? null : search.trim(),
        limit: Math.min(Math.max(1, Math.trunc(limit)), SELECTION_MAX_LIMIT),
      },
    });
}
