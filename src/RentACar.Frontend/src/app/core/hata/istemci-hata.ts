import { HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { ErrorHandler, inject, Injectable, Injector } from '@angular/core';

import { ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { ClientErrorRequest } from '@core/api/ui-tipleri';
import { requestContext } from '@core/oturum/request-context';
import { SessionService } from '@core/oturum/session-service';
import { ChunkErrorService, isChunkLoadError } from '@core/surum/parca-hatasi';
import { VersionService } from '@core/surum/version-service';

/** `POST /api/ui/v1/istemci-hata` gövdesi (sunucu sınırları: gövde ≤ 4 KB, alanlar kırpılır). */
export type ClientErrorReport = ClientErrorRequest;

export const REPORT_LIMITS = { mesaj: 500, yigin: 2500, url: 300, surum: 64 } as const;
/** Sayfa ömrü boyunca en çok bu kadar rapor (hata döngüsü sunucuyu doldurmasın). */
export const MAX_REPORTS = 10;

function clamp(text: string, limit: number): string {
  return text.length > limit ? text.slice(0, limit) : text;
}

/**
 * Yakalanmamış istemci hatasını sunucuya raporlar (backend WARNING loglar; firma/kullanıcıyla).
 * Yalnız oturum açıkken (uç kimlik ister), sayfa başına {@link MAX_REPORTS}, aynı mesaj bir kez.
 * URL'den yalnız YOL gider — sorgu dizesi (arama metni, müşteri adı) KVKK gereği gönderilmez.
 * HTTP hataları raporlanmaz (sunucu zaten loglar).
 */
@Injectable({ providedIn: 'root' })
export class ClientErrorReporter {
  private readonly api = inject(ApiIstemcisi);
  private readonly oturum = inject(SessionService);
  private readonly surum = inject(VersionService);
  private readonly location = inject(DOCUMENT).location;
  private readonly seen = new Set<string>();

  report(error: unknown): boolean {
    if (error instanceof HttpErrorResponse || error instanceof ApiHatasi) return false;
    if (!this.oturum.loggedIn() || this.seen.size >= MAX_REPORTS) return false;

    const message = clamp(
      (error instanceof Error ? `${error.name}: ${error.message}` : String(error)) ||
        'Bilinmeyen hata',
      REPORT_LIMITS.mesaj,
    );
    if (this.seen.has(message)) return false;
    this.seen.add(message);

    const stack =
      error instanceof Error && error.stack ? clamp(error.stack, REPORT_LIMITS.yigin) : undefined;
    const report: ClientErrorReport = {
      mesaj: message,
      yigin: stack ?? null,
      url: clamp(this.location.pathname, REPORT_LIMITS.url),
      surum: clamp(this.surum.mevcut ?? 'gelistirme', REPORT_LIMITS.surum),
    };
    this.api
      .post<unknown>('/api/ui/v1/istemci-hata', report, {
        context: requestContext({ sessiz: true, yenidenGirisYok: true }),
      })
      .subscribe({ error: () => undefined });
    return true;
  }
}

/**
 * Uygulama `ErrorHandler`'ı: konsola yazar; tembel parça yüklenemediyse kontrollü yenileme, değilse
 * sunucuya rapor. Servisler ilk hatada çözülür (ErrorHandler açılışta çok erken kurulur).
 */
@Injectable()
export class RcErrorHandler implements ErrorHandler {
  private readonly injector = inject(Injector);

  handleError(error: unknown): void {
    console.error(error);
    try {
      if (isChunkLoadError(error)) {
        this.injector.get(ChunkErrorService).isle();
        return;
      }
      this.injector.get(ClientErrorReporter).report(error);
    } catch (ic: unknown) {
      console.error(ic);
    }
  }
}
