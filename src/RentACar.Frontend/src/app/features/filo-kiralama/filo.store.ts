import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SelectionEndpointItem } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';

import type { FleetRental, FleetListRow } from './filo-modeli';

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
