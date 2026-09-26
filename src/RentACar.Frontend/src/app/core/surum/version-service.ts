import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject, Injectable, InjectionToken, signal } from '@angular/core';

import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';

/** Sayfayı yeniden yükleme/başka adrese tam gitme (testte sahtelenir). */
export interface PageReloader {
  yenile(): void;
  git(address: string): void;
}

export const PAGE_RELOADER = new InjectionToken<PageReloader>('SAYFA_YENILEYICI', {
  providedIn: 'root',
  factory: () => {
    const location = inject(DOCUMENT).location;
    return { yenile: () => location.reload(), git: (address) => location.assign(address) };
  },
});

/**
 * Yayındaki `index.html` metni (sürüm karşılaştırması için). Göreli `index.html` `<base href="/app/">`'e
 * göre çözülür; kabuk `no-cache` sunulur, `cache: 'no-store'` tarayıcı önbelleğini de atlar.
 */
export const VERSION_SOURCE = new InjectionToken<() => Promise<string | null>>('SURUM_KAYNAGI', {
  providedIn: 'root',
  factory: () => {
    const window = inject(DOCUMENT).defaultView;
    return async () => {
      if (!window?.fetch) return null;
      const response = await window.fetch('index.html', {
        cache: 'no-store',
        credentials: 'same-origin',
      });
      return response.ok ? response.text() : null;
    };
  },
});

/** Denetim aralığı; ayrıca sekme görünür olunca (en erken bu kadar sonra) denetlenir. */
export const VERSION_CHECK_INTERVAL = 5 * 60_000;
export const VISIBILITY_CHECK_INTERVAL = 60_000;

/**
 * Sürüm imzası: kabuğun yüklediği ana paket adı (`main-HASH.js`). Production çıktısında içerik hash'li
 * olduğu için her yayında değişir; geliştirmede (`main.js`) hiç değişmez → bildirim çıkmaz. `ngsw` yok.
 */
export function versionSignature(html: string): string | null {
  return /<script\b[^>]*\bsrc="([^"]*main[^"/]*\.js)"/i.exec(html)?.[1] ?? null;
}

/**
 * Yeni sürüm algılama (roadmap F3.3). Açık sekme yayından sonra eski paketle çalışmayı sürdürür (eski
 * parçalar bir yayın boyunca sunucuda tutulur — F2.2 `chunks.txt`); yeni `index.html` farklı ana paket
 * gösteriyorsa kalıcı "Yeni sürüm var — Yenile" bildirimi çıkar. Kullanıcı kaydetmeden zorla yenilenmez.
 */
@Injectable({ providedIn: 'root' })
export class VersionService {
  private readonly belge = inject(DOCUMENT);
  private readonly kaynak = inject(VERSION_SOURCE);
  private readonly sayfa = inject(PAGE_RELOADER);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  /** Bu sekmenin çalıştırdığı sürüm (belgedeki ana paket). */
  readonly mevcut = versionSignature(this.belge.documentElement.outerHTML);
  readonly hasNewVersion = signal(false);
  private lastCheck = 0;
  private stop: (() => void) | null = null;

  start(): void {
    const window = this.belge.defaultView;
    if (this.stop || !window || this.mevcut === null) return;
    const timer = window.setInterval(() => void this.check(), VERSION_CHECK_INTERVAL);
    const visibility = () => {
      if (
        this.belge.visibilityState === 'visible' &&
        Date.now() - this.lastCheck >= VISIBILITY_CHECK_INTERVAL
      ) {
        void this.check();
      }
    };
    this.belge.addEventListener('visibilitychange', visibility);
    this.stop = () => {
      window.clearInterval(timer);
      this.belge.removeEventListener('visibilitychange', visibility);
    };
    this.lastCheck = Date.now();
    this.destroyRef.onDestroy(() => this.stop?.());
  }

  /** Yayındaki sürümü okur; farklıysa bildirimi bir kez gösterir. Ağ hatası sessiz (sonraki denetim). */
  async check(): Promise<boolean> {
    if (this.hasNewVersion() || this.mevcut === null) return this.hasNewVersion();
    this.lastCheck = Date.now();
    let html: string | null;
    try {
      html = await this.kaynak();
    } catch {
      return false;
    }
    const published = html === null ? null : versionSignature(html);
    if (published === null || published === this.mevcut) return false;

    this.hasNewVersion.set(true);
    this.stop?.();
    this.toast.bilgi(this.t('surum.yeniSurum'), {
      sure: 0,
      eylem: { etiket: this.t('surum.yenile'), calistir: () => this.sayfa.yenile() },
    });
    return true;
  }
}
