import {
  HttpClient,
  HttpContext,
  HttpHeaders,
  HttpInterceptorFn,
  HttpParams,
  provideHttpClient,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { EnvironmentProviders, Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';

import { toApiError } from './api-hatasi';

/** Tüm yeni arayüz uçlarının kökü. Göreli (kök-göreli) — mutlak URL lint'le yasak. */
export const API_ROOT = '/api/ui/v1';

/**
 * Uç yolu TİP düzeyinde kök-göreli: `/api/ui/v1/...`. OpenAPI anlık görüntüsündeki (`docs/api/ui-v1.json`)
 * `paths` anahtarlarıyla birebir aynı biçim — grep ve tip üretimi için.
 */
export type ApiPath = `${typeof API_ROOT}/${string}`;

/** Backend sözleşmesi (`UiApiExtensions.XsrfCerezi` / `XsrfBasligi`). */
export const XSRF_COOKIE = 'XSRF-TOKEN';
export const XSRF_HEADER = 'X-XSRF-TOKEN';
export const IDEMPOTENCY_HEADER = 'Idempotency-Key';

type QueryValue = string | number | boolean;

/** Sorgu parametreleri; `null`/`undefined` değerler gönderilmez. */
export type QueryParameters = Readonly<
  Record<string, QueryValue | readonly QueryValue[] | null | undefined>
>;

export interface IstekSecenekleri {
  readonly parametreler?: QueryParameters;
  /**
   * `Idempotency-Key` başlığı (16–128 görünür ASCII). Sunucu bunu UUIDv5(kiracı|kullanıcı|başlık)'a
   * çevirir; deterministik sunucu anahtarı (ör. `TahsilatAnahtar`) varsa o önceliklidir. Anahtarın
   * üretimi/yenilenmesi (her 2xx'ten sonra) F3.6 gönderim kilidinde.
   */
  readonly islemAnahtari?: string;
  /** F3.3 interceptor'larına istek başına bayrak taşımak için. */
  readonly context?: HttpContext;
}

/**
 * `/api/ui/v1` için ince `HttpClient` sarmalayıcısı. Özellik kodu `HttpClient`'ı doğrudan kullanmaz
 * (lint: `features/**` içinde `HttpClient` içe aktarımı yasak).
 *
 * - **Yalnız göreli URL.** Angular'ın XSRF interceptor'ı `X-XSRF-TOKEN` başlığını yalnız sayfayla aynı
 *   kökene giden güvensiz isteklere ekler; kök-göreli yol bunu garanti eder. Yol ayrıca çalışma
 *   zamanında denetlenir (`?`/`#` yok — sorgu `parametreler` ile; `.`/`..` segmenti yok).
 * - **`withCredentials: true`** her istekte (oturum çerezi `racar.session`).
 * - **Her hata `ApiHatasi`'na çevrilir** (`kod` birliği). `kod`'a göre davranış F3.3'te.
 * - **Otomatik yeniden deneme yok** — para uçlarında çift gönderim üretir; `mukerrer`'de kayıt yeniden
 *   yüklenir, yeni anahtarla tekrar gönderilmez (idempotency envanteri, LOW-A).
 */
@Injectable({ providedIn: 'root' })
export class ApiIstemcisi {
  private readonly http = inject(HttpClient);

  get<T>(path: ApiPath, option?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('GET', path, undefined, option);
  }

  post<T>(path: ApiPath, body: unknown, option?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('POST', path, body, option);
  }

  put<T>(path: ApiPath, body: unknown, option?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('PUT', path, body, option);
  }

  patch<T>(path: ApiPath, body: unknown, option?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('PATCH', path, body, option);
  }

  delete<T>(path: ApiPath, option?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('DELETE', path, undefined, option);
  }

  private istek<T>(
    method: string,
    path: ApiPath,
    body: unknown,
    option: IstekSecenekleri | undefined,
  ): Observable<T> {
    checkPath(path);
    let headers = new HttpHeaders();
    if (option?.islemAnahtari !== undefined) {
      headers = headers.set(IDEMPOTENCY_HEADER, option.islemAnahtari);
    }
    return this.http
      .request<T>(method, path, {
        body: body,
        params: httpParams(option?.parametreler),
        headers: headers,
        context: option?.context,
        withCredentials: true,
        observe: 'body',
        responseType: 'json',
      })
      .pipe(catchError((error: unknown) => throwError(() => toApiError(error))));
  }
}

/**
 * Uygulama sağlayıcısı: `HttpClient` + XSRF yapılandırması (backend çerez/başlık adları). F3.3 kendi
 * interceptor'larını (kod'a göre diyalog/bant/toast) buraya parametre olarak verir.
 */
export function provideApiClient(...interceptors: HttpInterceptorFn[]): EnvironmentProviders {
  return provideHttpClient(
    withXsrfConfiguration({ cookieName: XSRF_COOKIE, headerName: XSRF_HEADER }),
    withInterceptors(interceptors),
  );
}

/** Programlama hatası → yüksek sesle (eşzamanlı) fırlatır; `TemelStore` bunu yine `hata` durumuna çevirir. */
function checkPath(path: string): void {
  const valid =
    path.startsWith(`${API_ROOT}/`) &&
    !path.includes('?') &&
    !path.includes('#') &&
    !path.includes('\\') &&
    !path
      .slice(API_ROOT.length + 1)
      .split('/')
      .some((segment) => segment === '' || segment === '.' || segment === '..');
  if (!valid) {
    throw new TypeError(
      `Geçersiz API yolu: "${path}". Yol "${API_ROOT}/" ile başlamalı; sorgu "parametreler" ile verilir.`,
    );
  }
}

function httpParams(parameters: QueryParameters | undefined): HttpParams {
  let result = new HttpParams();
  if (parameters === undefined) return result;
  for (const [name, value] of Object.entries(parameters)) {
    if (value === null || value === undefined) continue;
    const values: readonly QueryValue[] = Array.isArray(value)
      ? (value as readonly QueryValue[])
      : [value as QueryValue];
    for (const tek of values) result = result.append(name, String(tek));
  }
  return result;
}
