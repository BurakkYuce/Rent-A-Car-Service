import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';

import type { AvailabilityOptions, AvailabilityResponse } from './musaitlik-modeli';

/** Müsaitlik arama store'u: sonuç (sunucu pencere + fiyat + broker çiti) ve süzgeç seçenekleri (sessiz). */
@Injectable()
export class AvailabilityStore {
  private readonly api = inject(ApiIstemcisi);

  readonly sonuc = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<AvailabilityResponse>('/api/ui/v1/musaitlik', {
        parametreler: p,
        // 400 (eksik bitiş/gün sayısı) sayfada sonuç yerine gösterilir; ikinci bant/toast üretmesin.
        context: requestContext({ sessiz: true }),
      }),
    { oncekiVeriyiKoru: true },
  );

  readonly options = new TemelStore(() =>
    this.api.get<AvailabilityOptions>('/api/ui/v1/musaitlik/secenekler', {
      context: requestContext({ sessiz: true }),
    }),
  );
}
