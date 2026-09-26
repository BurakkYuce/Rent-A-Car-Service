import { Injectable, inject } from '@angular/core';
import { map, type Observable } from 'rxjs';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SelectionEndpointItem } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';

import {
  CUSTOMERS,
  customerPath,
  statementPath,
  type CustomerCard,
  type CustomerDetail,
  type CustomerRow,
  type CustomerStatement,
} from './customer-model';

@Injectable()
export class CustomerListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<CustomerRow>>(CUSTOMERS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class CustomerCardStore {
  private readonly api = inject(ApiIstemcisi);

  readonly card = new TemelStore((id: string) => this.api.get<CustomerCard>(customerPath(id)), {
    oncekiVeriyiKoru: true,
  });
}

export interface StatementQuery {
  readonly id: string;
  readonly bas: string | null;
  readonly bit: string | null;
}

@Injectable()
export class CustomerDetailStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detail = new TemelStore(
    (id: string) => this.api.get<CustomerDetail>(customerPath(id, '/detay')),
    { oncekiVeriyiKoru: true },
  );

  /** Cari ekstre (devir + yürüyen bakiye SUNUCUDA; SPA toplamaz). */
  readonly statement = new TemelStore(
    (q: StatementQuery) =>
      this.api.get<CustomerStatement>(statementPath(q.id), {
        parametreler: { bas: q.bas, bit: q.bit },
      }),
    { oncekiVeriyiKoru: true },
  );
}

/** Seç-veya-yaz önerileri: `(q) → metinler`; hata boş liste (alan serbest metin olarak çalışır). */
export type SuggestionFetch = (q: string) => Observable<readonly string[]>;

export function addressSuggestionFetch(api: ApiIstemcisi, kind: 'il' | 'ilce'): SuggestionFetch {
  return (q) =>
    api
      .get<readonly { readonly deger: string }[]>(`${CUSTOMERS}/secim/${kind}`, {
        parametreler: { q: q === '' ? null : q, limit: 20 },
        context: requestContext({ sessiz: true }),
      })
      .pipe(map((list) => list.map((x) => x.deger)));
}

/** Kaynak önerisi: aktif rezervasyon kaynakları (Blazor ComboBox master tanımdan). */
export function sourceSuggestionFetch(api: ApiIstemcisi): SuggestionFetch {
  return (q) =>
    api
      .get<readonly SelectionEndpointItem<'rezervasyon-kaynagi'>[]>(
        '/api/ui/v1/secim/rezervasyon-kaynagi',
        {
          parametreler: { q: q === '' ? null : q, limit: 20 },
          context: requestContext({ sessiz: true }),
        },
      )
      .pipe(map((list) => list.map((x) => x.etiket)));
}
