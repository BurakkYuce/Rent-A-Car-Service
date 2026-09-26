import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { FinanceAccountItem, SelectionItem } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';

import type {
  ExpenseRow,
  IncomingInvoiceDetail,
  IncomingInvoiceRow,
  InvoiceDetail,
  InvoiceLineRow,
  InvoiceRow,
  InvoiceSummary,
  PenaltyDetail,
  PenaltyRow,
  PenaltyType,
  RentalRow,
  VehicleSaleRow,
} from './document-model';

export const INVOICES = '/api/ui/v1/faturalar';
export const PENALTIES = '/api/ui/v1/cezalar';
export const EXPENSES = '/api/ui/v1/giderler';
export const INCOMING = '/api/ui/v1/gelen-efatura';
export const SALES = '/api/ui/v1/satislar';

/** Kimlikli alt yol (kimlik kaçışlanır). */
export function recordPath(base: string, id: string, suffix = ''): `/api/ui/v1/${string}` {
  return `${base}/${encodeURIComponent(id)}${suffix}` as `/api/ui/v1/${string}`;
}

/** Liste parametrelerinden sayfalama/sıralama düşer: özet uçları tüm eşleşen kümeyi toplar. */
export function summaryParameters(p: QueryParameters): QueryParameters {
  return Object.fromEntries(
    Object.entries(p).filter(([k]) => k !== 'sayfa' && k !== 'boyut' && k !== 'sirala'),
  );
}

/** Öneri listeleri (şube adları, kasa/banka hesapları): hata SESSİZ — alan serbest metin/boş kalır. */
function quiet() {
  return { context: requestContext({ sessiz: true }) };
}

@Injectable()
export class BranchNames {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(() =>
    this.api.get<readonly SelectionItem[]>('/api/ui/v1/secim/sube', {
      parametreler: { limit: 20 },
      ...quiet(),
    }),
  );
}

@Injectable()
export class InvoiceStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<InvoiceRow>>(INVOICES, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  readonly detail = new TemelStore((id: string) =>
    this.api.get<InvoiceDetail>(recordPath(INVOICES, id)),
  );
  /**
   * Döviz bazında toplam (`/faturalar/ozet`, #300): listenin süzgeçleri, sayfalamasız. Toplam SUNUCUDA; hata sessiz
   * (liste kendi hatasını zaten gösterir, toplam satırı yalnız görünmez).
   */
  readonly summary = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<InvoiceSummary>(`${INVOICES}/ozet`, { parametreler: p, ...quiet() }),
    { oncekiVeriyiKoru: true },
  );
  /** Toplu faturalama adayları: faturasız kiralar (Blazor ile aynı: iptal ve tutarsızlar ekranda elenir). */
  readonly unbilled = new TemelStore(() =>
    this.api.get<Sayfa<RentalRow>>('/api/ui/v1/kiralar', {
      parametreler: { fatura: false, boyut: 200 },
    }),
  );
}

@Injectable()
export class InvoiceLineStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) =>
      this.api.get<Sayfa<InvoiceLineRow>>(`${INVOICES}/satirlar`, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class PenaltyStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<PenaltyRow>>(PENALTIES, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  readonly detail = new TemelStore(
    (id: string) => this.api.get<PenaltyDetail>(recordPath(PENALTIES, id)),
    { oncekiVeriyiKoru: true },
  );
  /** Ceza türü önerileri (seç veya yaz; OperationsWrite — yoksa sessizce serbest metin). */
  readonly types = new TemelStore(() =>
    this.api.get<Sayfa<PenaltyType>>('/api/ui/v1/ceza-turleri', {
      parametreler: { aktif: true, boyut: 200 },
      ...quiet(),
    }),
  );
}

@Injectable()
export class ExpenseStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<ExpenseRow>>(EXPENSES, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  readonly accounts = new TemelStore(() =>
    this.api.get<readonly FinanceAccountItem[]>('/api/ui/v1/finans/hesaplar', quiet()),
  );
}

@Injectable()
export class IncomingInvoiceStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<IncomingInvoiceRow>>(INCOMING, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  readonly detail = new TemelStore((id: string) =>
    this.api.get<IncomingInvoiceDetail>(recordPath(INCOMING, id)),
  );
}

@Injectable()
export class SaleStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = new TemelStore(
    (p: QueryParameters) => this.api.get<Sayfa<VehicleSaleRow>>(SALES, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}
