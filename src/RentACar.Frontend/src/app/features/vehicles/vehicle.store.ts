import { Injectable, inject } from '@angular/core';
import { type Observable, map } from 'rxjs';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SecimUcuOgesi } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';

import type {
  DetailedRow,
  StatusBoardResponse,
  SuggestionValue,
  VehicleCard,
  VehicleDetail,
  VehicleListRow,
  VehicleModelGroups,
  VehiclePhoto,
  VehicleSummary,
} from './vehicle-model';

const vehiclePath = (id: string) => `/api/ui/v1/araclar/${encodeURIComponent(id)}` as const;

/** Araç listesi: sunucu sayfalı liste + özet şerit (tüm filo) + "modele göre grupla" görünümü. */
@Injectable()
export class VehicleListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<VehicleListRow>>('/api/ui/v1/araclar', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly summary = new TemelStore(() => this.api.get<VehicleSummary>('/api/ui/v1/araclar/ozet'));

  readonly modelGroups = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<VehicleModelGroups>('/api/ui/v1/araclar/model-gruplari', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

/** Detaylı araç listesi (ViewReports). */
@Injectable()
export class DetailedListStore {
  private readonly api = inject(ApiIstemcisi);

  readonly list = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<DetailedRow>>('/api/ui/v1/araclar/detayli', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

/** Araç güncel durum panosu: sayfa + filtreye uyan tüm satırların sayaçları. */
@Injectable()
export class StatusBoardStore {
  private readonly api = inject(ApiIstemcisi);

  readonly board = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<StatusBoardResponse>('/api/ui/v1/araclar/durum', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
}

/** Araç kartı (düzenleme formu) + son KM kayıtları + fotoğraflar + varsayılan grup. */
@Injectable()
export class VehicleCardStore {
  private readonly api = inject(ApiIstemcisi);

  readonly card = new TemelStore((id: string) => this.api.get<VehicleCard>(vehiclePath(id)), {
    oncekiVeriyiKoru: true,
  });

  /** Son 3 KM kaydı (salt okunur özet) — detay ucundan; hata sessiz (form çalışmaya devam eder). */
  readonly detail = new TemelStore((id: string) =>
    this.api.get<VehicleDetail>(`${vehiclePath(id)}/detay`, {
      context: istekBaglami({ sessiz: true }),
    }),
  );

  readonly photos = new TemelStore(
    (id: string) => this.api.get<readonly VehiclePhoto[]>(`${vehiclePath(id)}/fotograflar`),
    { oncekiVeriyiKoru: true },
  );

  readonly defaultGroup = new TemelStore(() =>
    this.api.get<{ readonly ad: string | null }>('/api/ui/v1/araclar/secim/varsayilan-grup', {
      context: istekBaglami({ sessiz: true }),
    }),
  );

  /** Kartın grubu tanımlı gruplarda var mı (Blazor "— tanımsız" işareti). */
  readonly groupMatches = new TemelStore((name: string) =>
    this.api.get<readonly SecimUcuOgesi<'arac-grubu'>[]>('/api/ui/v1/secim/arac-grubu', {
      parametreler: { q: name, limit: 20 },
      context: istekBaglami({ sessiz: true }),
    }),
  );
}

/** Araç detayı: kira/servis/ceza/hasar geçmişi + son 10 KM kaydı. */
@Injectable()
export class VehicleDetailStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detail = new TemelStore(
    (id: string) => this.api.get<VehicleDetail>(`${vehiclePath(id)}/detay`),
    { oncekiVeriyiKoru: true },
  );
}

/** Seç-veya-yaz öneri kaynağı: `q` ile sunucu araması, yalnız etiket. */
export type SuggestionFetch = (q: string) => Observable<readonly string[]>;

export function vehicleSuggestionFetch(
  api: ApiIstemcisi,
  kind: 'marka' | 'tip' | 'renk' | 'segment' | 'sahip',
  extra: () => SorguParametreleri = () => ({}),
): SuggestionFetch {
  return (q) =>
    api
      .get<readonly SuggestionValue[]>(`/api/ui/v1/araclar/secim/${kind}`, {
        parametreler: { ...extra(), q: q === '' ? null : q, limit: 20 },
        context: istekBaglami({ sessiz: true }),
      })
      .pipe(map((list) => list.map((x) => x.deger)));
}

export function secimSuggestionFetch(
  api: ApiIstemcisi,
  endpoint: 'sube' | 'arac-grubu',
): SuggestionFetch {
  return (q) =>
    api
      .get<readonly SecimUcuOgesi<'sube'>[]>(`/api/ui/v1/secim/${endpoint}`, {
        parametreler: { q: q === '' ? null : q, limit: 20 },
        context: istekBaglami({ sessiz: true }),
      })
      .pipe(map((list) => list.map((x) => x.etiket)));
}
