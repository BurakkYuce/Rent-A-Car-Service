import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { FinanceAccountItem } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';

import type {
  Allocation,
  CustomerInstallment,
  CustomerInstallmentSummary,
  DamageFile,
  FleetPlan,
  LoanBoard,
  LoanDetail,
  LoanRow,
  OrderDetail,
  OrderRow,
} from './finance-model';

export const LOANS = '/api/ui/v1/arac-kredileri';
export const CUSTOMER_INSTALLMENTS = '/api/ui/v1/musteri-taksitleri';
export const ORDERS = '/api/ui/v1/arac-siparisleri';
export const ALLOCATIONS = '/api/ui/v1/baflar';
export const DAMAGE_FILES = '/api/ui/v1/hasar-dosyalari';
export const FLEET_PLANS = '/api/ui/v1/filo-plan';

/** Kimlikli alt yol (kimlik kaçışlanır). */
export function recordPath<B extends `/api/ui/v1/${string}`>(
  base: B,
  id: string,
  suffix = '',
): `/api/ui/v1/${string}` {
  return `${base}/${encodeURIComponent(id)}${suffix}` as `/api/ui/v1/${string}`;
}

/** Filtre parametrelerinden sayfalama/sıralama düşer (özet kartları aynı süzgeçle, sayfasız). */
export function withoutPaging(p: QueryParameters): QueryParameters {
  return Object.fromEntries(
    Object.entries(p).filter(([name]) => name !== 'sayfa' && name !== 'boyut' && name !== 'sirala'),
  );
}

@Injectable()
export class LoanListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<LoanRow>>(LOANS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  /** Liste üstü 5 özet kart (filtreli küme; iptal hariç) — salt gösterge. */
  readonly board = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<LoanBoard>(`${LOANS}/ozet`, { parametreler: withoutPaging(p) }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class LoanDetailStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detail = new TemelStore(
    (id: string) => this.api.get<LoanDetail>(recordPath(LOANS, id)),
    {
      oncekiVeriyiKoru: true,
    },
  );

  /** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici görünmez, tür yine seçilir). */
  readonly accounts = new TemelStore(() =>
    this.api.get<readonly FinanceAccountItem[]>('/api/ui/v1/finans/hesaplar', {
      context: requestContext({ sessiz: true }),
    }),
  );
}

@Injectable()
export class CustomerInstallmentStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<CustomerInstallment>>(CUSTOMER_INSTALLMENTS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly summary = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<CustomerInstallmentSummary>(`${CUSTOMER_INSTALLMENTS}/ozet`, {
        parametreler: withoutPaging(p),
      }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class OrderListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<OrderRow>>(ORDERS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  /** Kredi etiketleri ("No — Banka"; Blazor sütunu ve form seçimi). Hata sessiz: sütun "—" kalır. */
  readonly loans = new TemelStore(() =>
    this.api.get<Sayfa<LoanRow>>(LOANS, {
      parametreler: { boyut: 200 },
      context: requestContext({ sessiz: true }),
    }),
  );
}

@Injectable()
export class OrderFormStore {
  private readonly api = inject(ApiIstemcisi);

  readonly order = new TemelStore(
    (id: string) => this.api.get<OrderDetail>(recordPath(ORDERS, id)),
    {
      oncekiVeriyiKoru: true,
    },
  );

  readonly loans = new TemelStore(() =>
    this.api.get<Sayfa<LoanRow>>(LOANS, {
      parametreler: { boyut: 200 },
      context: requestContext({ sessiz: true }),
    }),
  );

  /** Tedarikçi önerileri: mevcut siparişlerin tedarikçileri (Blazor: master tanım yok, kendini besler). */
  readonly suppliers = new TemelStore(() =>
    this.api.get<Sayfa<OrderRow>>(ORDERS, {
      parametreler: { boyut: 200 },
      context: requestContext({ sessiz: true }),
    }),
  );
}

@Injectable()
export class AllocationStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<Allocation>>(ALLOCATIONS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class DamageFileStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<DamageFile>>(DAMAGE_FILES, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class FleetPlanStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<FleetPlan>>(FLEET_PLANS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}
