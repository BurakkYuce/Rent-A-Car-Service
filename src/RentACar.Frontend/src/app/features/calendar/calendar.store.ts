import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-client';
import { requestContext } from '@core/session/request-context';
import { TemelStore } from '@core/data/base-store';

import type { CalendarOptions, CalendarResponse } from './calendar-model';

/** Takvim sayfası store'u: ay ızgarası (sunucu hesaplar) + süzgeç önerileri (sessiz). */
@Injectable()
export class CalendarStore {
  private readonly api = inject(ApiIstemcisi);

  readonly grid = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<CalendarResponse>('/api/ui/v1/takvim', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly options = new TemelStore(() =>
    this.api.get<CalendarOptions>('/api/ui/v1/takvim/secenekler', {
      context: requestContext({ sessiz: true }),
    }),
  );
}
