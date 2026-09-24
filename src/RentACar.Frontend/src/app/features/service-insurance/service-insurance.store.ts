import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { FinansHesapOgesi } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';

import {
  DUE_BOARD,
  type DueBoard,
  type InspectionDetail,
  type InspectionRow,
  type MtvDetail,
  type MtvRow,
  type PolicyDetail,
  type PolicyRow,
  REGULATION,
  type RegulationOptions,
  SERVICES,
  type ServiceRecordDetail,
  type ServiceRecordRow,
  recordPath,
} from './service-insurance-model';

@Injectable()
export class ServiceListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: SorguParametreleri) => this.api.get<Sayfa<ServiceRecordRow>>(SERVICES, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class ServiceDetailStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detail = new TemelStore(
    (id: string) => this.api.get<ServiceRecordDetail>(recordPath(SERVICES, id)),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class PolicyListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<PolicyRow>>(`${REGULATION}/sigortalar`, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class MtvListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<MtvRow>>(`${REGULATION}/mtv`, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class InspectionListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<InspectionRow>>(`${REGULATION}/muayeneler`, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

/** Firma / zeyil tipi / döviz / sigorta tipi önerileri (hata sessiz: yerel varsayılanlar kalır). */
@Injectable()
export class RegulationOptionsStore {
  private readonly api = inject(ApiIstemcisi);

  readonly options = new TemelStore(() =>
    this.api.get<RegulationOptions>(`${REGULATION}/secenekler`, {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}

@Injectable()
export class PolicyDetailStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detail = new TemelStore(
    (id: string) => this.api.get<PolicyDetail>(recordPath(`${REGULATION}/sigortalar`, id)),
    { oncekiVeriyiKoru: true },
  );

  readonly accounts = accountsStore(this.api);
}

@Injectable()
export class InstallmentRecordStore {
  private readonly api = inject(ApiIstemcisi);

  readonly mtv = new TemelStore(
    (id: string) => this.api.get<MtvDetail>(recordPath(`${REGULATION}/mtv`, id)),
    { oncekiVeriyiKoru: true },
  );

  readonly inspection = new TemelStore(
    (id: string) => this.api.get<InspectionDetail>(recordPath(`${REGULATION}/muayeneler`, id)),
    { oncekiVeriyiKoru: true },
  );

  readonly accounts = accountsStore(this.api);
}

@Injectable()
export class DueBoardStore {
  private readonly api = inject(ApiIstemcisi);

  readonly board = new TemelStore(
    (p: SorguParametreleri) => this.api.get<DueBoard>(DUE_BOARD, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

/** Aktif kasa/banka hesapları (seçim isteğe bağlı; hata sessiz — seçici görünmez, tür yine seçilir). */
function accountsStore(api: ApiIstemcisi): TemelStore<readonly FinansHesapOgesi[]> {
  return new TemelStore(() =>
    api.get<readonly FinansHesapOgesi[]>('/api/ui/v1/finans/hesaplar', {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}
