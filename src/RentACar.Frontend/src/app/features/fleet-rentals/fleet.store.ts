import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-client';
import type { Sayfa } from '@core/api/page';
import type { SelectionEndpointItem } from '@core/api/ui-types';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/data/base-store';

import type { FleetRental, FleetListRow } from './fleet-model';

/** Filo kiralama listesi: sunucu sayfalı liste + URL'deki müşteri kimliğinin etiketi (sessiz). */
@Injectable()
export class FleetListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<FleetListRow>>('/api/ui/v1/filo-kiralama', { parametreler: p }),
    { oncekiVeriyiKoru: true },
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

/** Filo sözleşmesi detayı (künye + taksit planı + yetkiler; plan sunucuda hesaplanır). */
@Injectable()
export class FleetDetailStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detay = new TemelStore(
    (id: string) => this.api.get<FleetRental>(`/api/ui/v1/filo-kiralama/${encodeURIComponent(id)}`),
    { oncekiVeriyiKoru: true },
  );
}
