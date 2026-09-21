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

import { apiHatasinaCevir } from './api-hatasi';

/** Tüm yeni arayüz uçlarının kökü. Göreli (kök-göreli) — mutlak URL lint'le yasak. */
export const API_KOKU = '/api/ui/v1';

/**
 * Uç yolu TİP düzeyinde kök-göreli: `/api/ui/v1/...`. OpenAPI anlık görüntüsündeki (`docs/api/ui-v1.json`)
 * `paths` anahtarlarıyla birebir aynı biçim — grep ve tip üretimi için.
 */
export type ApiYolu = `${typeof API_KOKU}/${string}`;

/** Backend sözleşmesi (`UiApiExtensions.XsrfCerezi` / `XsrfBasligi`). */
export const XSRF_CEREZI = 'XSRF-TOKEN';
export const XSRF_BASLIGI = 'X-XSRF-TOKEN';
export const IDEMPOTENCY_BASLIGI = 'Idempotency-Key';

type SorguDegeri = string | number | boolean;

/** Sorgu parametreleri; `null`/`undefined` değerler gönderilmez. */
export type SorguParametreleri = Readonly<
  Record<string, SorguDegeri | readonly SorguDegeri[] | null | undefined>
>;

export interface IstekSecenekleri {
  readonly parametreler?: SorguParametreleri;
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

  get<T>(yol: ApiYolu, secenek?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('GET', yol, undefined, secenek);
  }

  post<T>(yol: ApiYolu, govde: unknown, secenek?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('POST', yol, govde, secenek);
  }

  put<T>(yol: ApiYolu, govde: unknown, secenek?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('PUT', yol, govde, secenek);
  }

  patch<T>(yol: ApiYolu, govde: unknown, secenek?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('PATCH', yol, govde, secenek);
  }

  delete<T>(yol: ApiYolu, secenek?: IstekSecenekleri): Observable<T> {
    return this.istek<T>('DELETE', yol, undefined, secenek);
  }

  private istek<T>(
    yontem: string,
    yol: ApiYolu,
    govde: unknown,
    secenek: IstekSecenekleri | undefined,
  ): Observable<T> {
    yoluDenetle(yol);
    let basliklar = new HttpHeaders();
    if (secenek?.islemAnahtari !== undefined) {
      basliklar = basliklar.set(IDEMPOTENCY_BASLIGI, secenek.islemAnahtari);
    }
    return this.http
      .request<T>(yontem, yol, {
        body: govde,
        params: httpParametreleri(secenek?.parametreler),
        headers: basliklar,
        context: secenek?.context,
        withCredentials: true,
        observe: 'body',
        responseType: 'json',
      })
      .pipe(catchError((hata: unknown) => throwError(() => apiHatasinaCevir(hata))));
  }
}

/**
 * Uygulama sağlayıcısı: `HttpClient` + XSRF yapılandırması (backend çerez/başlık adları). F3.3 kendi
 * interceptor'larını (kod'a göre diyalog/bant/toast) buraya parametre olarak verir.
 */
export function provideApiIstemcisi(...interceptorlar: HttpInterceptorFn[]): EnvironmentProviders {
  return provideHttpClient(
    withXsrfConfiguration({ cookieName: XSRF_CEREZI, headerName: XSRF_BASLIGI }),
    withInterceptors(interceptorlar),
  );
}

/** Programlama hatası → yüksek sesle (eşzamanlı) fırlatır; `TemelStore` bunu yine `hata` durumuna çevirir. */
function yoluDenetle(yol: string): void {
  const gecerli =
    yol.startsWith(`${API_KOKU}/`) &&
    !yol.includes('?') &&
    !yol.includes('#') &&
    !yol.includes('\\') &&
    !yol
      .slice(API_KOKU.length + 1)
      .split('/')
      .some((segment) => segment === '' || segment === '.' || segment === '..');
  if (!gecerli) {
    throw new TypeError(
      `Geçersiz API yolu: "${yol}". Yol "${API_KOKU}/" ile başlamalı; sorgu "parametreler" ile verilir.`,
    );
  }
}

function httpParametreleri(parametreler: SorguParametreleri | undefined): HttpParams {
  let sonuc = new HttpParams();
  if (parametreler === undefined) return sonuc;
  for (const [ad, deger] of Object.entries(parametreler)) {
    if (deger === null || deger === undefined) continue;
    const degerler: readonly SorguDegeri[] = Array.isArray(deger)
      ? (deger as readonly SorguDegeri[])
      : [deger as SorguDegeri];
    for (const tek of degerler) sonuc = sonuc.append(ad, String(tek));
  }
  return sonuc;
}
