import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SelectionEndpointItem } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';

import type { ReservationTerm } from './rez-sart-modeli';

/**
 * Rez şartları sayfa store'u. Liste sunucu sayfalı; "bekleyen" sayacı, grup önerileri ve URL'deki
 * müşteri kimliğinin etiketi SESSİZ (hataları liste hatasının yanında ikinci bant üretmez).
 */
@Injectable()
export class ReservationTermsStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<ReservationTerm>>('/api/ui/v1/rez-sartlari', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly pending = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<ReservationTerm>>('/api/ui/v1/rez-sartlari', {
        parametreler: p,
        context: requestContext({ sessiz: true }),
      }),
    { oncekiVeriyiKoru: true },
  );

  readonly groups = new TemelStore(() =>
    this.api.get<readonly string[]>('/api/ui/v1/rez-sartlari/gruplar', {
      context: requestContext({ sessiz: true }),
    }),
  );

  readonly musteri = new TemelStore((id: string) =>
    this.api.get<SelectionEndpointItem<'musteri'>>(
      `/api/ui/v1/secim/musteri/${encodeURIComponent(id)}`,
      {
        context: requestContext({ sessiz: true }),
      },
    ),
  );
}
