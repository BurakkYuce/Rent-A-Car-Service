import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';

import { PAGE_RELOADER } from './version-service';

/** Yenileme koruması (sessionStorage; `rc.` önekli → çıkışta silinir). */
export const CHUNK_RELOAD_KEY = 'rc.parcaYenileme';
/** Bu süre içinde ikinci kez yenilenmez (döngü koruması). */
export const CHUNK_RELOAD_INTERVAL = 60_000;

const CHUNK_ERROR_PATTERNS = [
  /Failed to fetch dynamically imported module/i, // Chromium
  /error loading dynamically imported module/i, // Firefox
  /Importing a module script failed/i, // Safari
  /Loading chunk [\w-]+ failed/i, // webpack biçimi (ChunkLoadError)
];

/** Tembel parça (lazy chunk) yüklenemedi mi? Yayından sonra eski parça silinmişse olur. */
export function isChunkLoadError(error: unknown): boolean {
  if (error instanceof Error && error.name === 'ChunkLoadError') return true;
  const message = error instanceof Error ? error.message : typeof error === 'string' ? error : '';
  return CHUNK_ERROR_PATTERNS.some((pattern) => pattern.test(message));
}

/**
 * ChunkLoadError → KONTROLLÜ tek yenileme: gidilmek istenen adrese tam sayfa yükleme (yeni sürümün
 * kabuğu yeni parçaları bilir). Son {@link CHUNK_RELOAD_INTERVAL} içinde zaten yenilendiyse döngüye
 * girmez; kalıcı hata toast'u gösterir.
 */
@Injectable({ providedIn: 'root' })
export class ChunkErrorService {
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly sayfa = inject(PAGE_RELOADER);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();
  /** Yenileme başlatıldı: aynı hatanın ikinci bildirimi (router + ErrorHandler) yok sayılır. */
  private isRefreshing = false;

  /** @returns yenileme başlatıldıysa (ya da zaten sürüyorsa) `true`. */
  isle(targetUrl?: string): boolean {
    if (this.isRefreshing) return true;
    const now = Date.now();
    const last = Number(this.read() ?? 0);
    const soon = Number.isFinite(last) && now - last >= 0 && now - last < CHUNK_RELOAD_INTERVAL;
    // Koruma yazılamıyorsa (engelli depolama) otomatik yenileme de yok: döngü riski.
    if (soon || !this.write(String(now))) {
      this.toast.hata(this.t('surum.yenilemeBasarisiz'), {
        sure: 0,
        eylem: { etiket: this.t('surum.yenile'), calistir: () => this.sayfa.yenile() },
      });
      return false;
    }
    this.isRefreshing = true;
    if (targetUrl) this.sayfa.git(targetUrl);
    else this.sayfa.yenile();
    return true;
  }

  private read(): string | null {
    try {
      return this.window?.sessionStorage.getItem(CHUNK_RELOAD_KEY) ?? null;
    } catch {
      return null;
    }
  }

  private write(value: string): boolean {
    try {
      const store = this.window?.sessionStorage;
      if (!store) return false;
      store.setItem(CHUNK_RELOAD_KEY, value);
      return store.getItem(CHUNK_RELOAD_KEY) === value;
    } catch {
      return false;
    }
  }
}
