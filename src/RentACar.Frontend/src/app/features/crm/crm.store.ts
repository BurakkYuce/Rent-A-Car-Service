import { Injectable, inject } from '@angular/core';
import { forkJoin, map, of, type Observable } from 'rxjs';

import { ApiIstemcisi, type ApiPath, type QueryParameters } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SelectionEndpointItem } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { TemelStore } from '@core/veri/temel-store';
import type { SelectionSource, SecimSecenegi } from '@shared/form/arama-secim/selection-source';

import {
  ASSISTANCE,
  COMPLAINTS,
  CRM_ANALYSIS,
  LEGAL_FILES,
  RENTAL_PICK,
  SURVEYS,
  rentalOption,
  type Assistance,
  type Complaint,
  type CrmAnalysis,
  type CrmFilterOptions,
  type LegalFile,
  type RentalPickItem,
  type Survey,
} from './crm-model';

/** Özet sayaçlarının bir kalemi: süzgece EK koşul (zaten başka değere süzülmüşse 0 — Blazor sayımı süzülmüş kümede). */
export interface CountRule {
  readonly key: string;
  readonly value: string | boolean;
}

/**
 * Süzülmüş listenin sayaçları (Blazor liste üstü satırı: "N kayıt · M açık"). Liste sayfalı olduğu için SPA saymaz:
 * her kalem aynı süzgeçle `boyut=1` isteğinin `toplam`ıdır (sunucu sayar).
 */
export function countRequests(
  api: ApiIstemcisi,
  path: ApiPath,
  p: QueryParameters,
  rules: Readonly<Record<string, CountRule>>,
): Observable<Readonly<Record<string, number>>> {
  const base: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(p))
    if (k !== 'sayfa' && k !== 'boyut' && k !== 'sirala') base[k] = v;
  const calls: Record<string, Observable<number>> = {};
  for (const [name, rule] of Object.entries(rules)) {
    const current = base[rule.key];
    calls[name] =
      current !== undefined && current !== null && String(current) !== String(rule.value)
        ? of(0)
        : api
            .get<Sayfa<unknown>>(path, {
              parametreler: {
                ...(base as QueryParameters),
                [rule.key]: rule.value,
                sayfa: 1,
                boyut: 1,
              },
              context: requestContext({ sessiz: true }),
            })
            .pipe(map((s) => s.toplam));
  }
  return forkJoin(calls);
}

function pagedStore<T>(api: ApiIstemcisi, path: ApiPath) {
  return new TemelStore((p: QueryParameters) => api.get<Sayfa<T>>(path, { parametreler: p }), {
    oncekiVeriyiKoru: true,
  });
}

@Injectable()
export class SurveyStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = pagedStore<Survey>(this.api, SURVEYS);
  readonly counts = new TemelStore(
    (p: QueryParameters) =>
      countRequests(this.api, SURVEYS, p, {
        yapildi: { key: 'durum', value: 'Yapildi' },
        yapilmadi: { key: 'durum', value: 'Yapilmadi' },
      }),
    { oncekiVeriyiKoru: true },
  );
  /** Varsayılan soru metinleri (Blazor `AnketService.VarsayilanSorular`). */
  readonly defaults = new TemelStore(() =>
    this.api.get<readonly string[]>(`${SURVEYS}/varsayilan-sorular`),
  );
}

@Injectable()
export class ComplaintStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = pagedStore<Complaint>(this.api, COMPLAINTS);
  readonly counts = new TemelStore(
    (p: QueryParameters) =>
      countRequests(this.api, COMPLAINTS, p, { acik: { key: 'durum', value: 'Acik' } }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class AssistanceStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = pagedStore<Assistance>(this.api, ASSISTANCE);
  readonly counts = new TemelStore(
    (p: QueryParameters) =>
      countRequests(this.api, ASSISTANCE, p, {
        acik: { key: 'kapandi', value: false },
        hareketEdemiyor: { key: 'hareketEdemiyor', value: true },
      }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class LegalFileStore {
  private readonly api = inject(ApiIstemcisi);
  readonly list = pagedStore<LegalFile>(this.api, LEGAL_FILES);
  readonly counts = new TemelStore(
    (p: QueryParameters) =>
      countRequests(this.api, LEGAL_FILES, p, { acik: { key: 'durum', value: 'Acik' } }),
    { oncekiVeriyiKoru: true },
  );
}

@Injectable()
export class CrmAnalysisStore {
  private readonly api = inject(ApiIstemcisi);
  readonly analysis = new TemelStore(
    (p: QueryParameters) => this.api.get<CrmAnalysis>(CRM_ANALYSIS, { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );
  /** Seçenekler FİLTRESİZ kümeden (Blazor: süzülmüşten türetilseydi seçim sonrası diğerleri kaybolurdu). */
  readonly options = new TemelStore(() =>
    this.api.get<CrmFilterOptions>(`${CRM_ANALYSIS}/secenekler`, {
      context: requestContext({ sessiz: true }),
    }),
  );
}

/** Kira sözleşmesi seçimi (`/crm/secim/kira`; şube kapsamına süzülü, TC/telefon YOK). Enjeksiyon bağlamında çağrılır. */
export function rentalPickSource(): SelectionSource {
  const api = inject(ApiIstemcisi);
  return (q, limit) =>
    api
      .get<readonly RentalPickItem[]>(RENTAL_PICK, {
        parametreler: { q: q.trim() === '' ? null : q.trim(), limit: Math.min(limit, 20) },
      })
      .pipe(map((list) => list.map(rentalOption)));
}

/** Çıkış ofisi önerileri: tanımlı lokasyonların adları (seç-veya-yaz). */
export function officeSuggestionFetch(
  api: ApiIstemcisi,
): (q: string) => Observable<readonly string[]> {
  return (q) =>
    api
      .get<readonly SelectionEndpointItem<'lokasyon'>[]>('/api/ui/v1/secim/lokasyon', {
        parametreler: { q: q === '' ? null : q, limit: 20 },
        context: requestContext({ sessiz: true }),
      })
      .pipe(map((list) => list.map((x) => x.etiket)));
}

/** URL'deki müşteri kimliğinin etiketi (paylaşılan bağlantı / yenileme): `GET /secim/musteri/{id}`. */
export function customerLabelFetch(api: ApiIstemcisi, id: string): Observable<SecimSecenegi> {
  return api.get<SecimSecenegi>(`/api/ui/v1/secim/musteri/${encodeURIComponent(id)}`, {
    context: requestContext({ sessiz: true }),
  });
}
