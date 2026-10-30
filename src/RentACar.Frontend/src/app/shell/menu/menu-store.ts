import { inject, Injectable } from '@angular/core';

import { ApiIstemcisi } from '@core/api/api-client';
import type { MenuResponse } from '@core/api/ui-types';
import { requestContext } from '@core/session/request-context';
import { TemelStore } from '@core/data/base-store';

/**
 * `GET /api/ui/v1/menu` (F1.6): sunucu öğeleri izin + modüle göre SÜZER ve rozet sayaçlarını ekler;
 * istemci süzmez. Hata sessiz (genel toast/diyalog yok) — menü kendi hata/yeniden dene kutusunu
 * gösterir. Kabuğun `providers`'ında; ne zaman yükleneceğine `FetchPolicy` karar verir.
 */
@Injectable()
export class MenuStore {
  private readonly api = inject(ApiIstemcisi);

  readonly menu = new TemelStore<MenuResponse, number>(
    () =>
      this.api.get<MenuResponse>('/api/ui/v1/menu', {
        context: requestContext({ sessiz: true, yenidenGirisYok: true }),
      }),
    { oncekiVeriyiKoru: true },
  );
}
