import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject, Injectable, InjectionToken, signal } from '@angular/core';

import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';

/** Sayfayı yeniden yükleme/başka adrese tam gitme (testte sahtelenir). */
export interface SayfaYenileyici {
  yenile(): void;
  git(adres: string): void;
}

export const SAYFA_YENILEYICI = new InjectionToken<SayfaYenileyici>('SAYFA_YENILEYICI', {
  providedIn: 'root',
  factory: () => {
    const konum = inject(DOCUMENT).location;
    return { yenile: () => konum.reload(), git: (adres) => konum.assign(adres) };
  },
});

/**
 * Yayındaki `index.html` metni (sürüm karşılaştırması için). Göreli `index.html` `<base href="/app/">`'e
 * göre çözülür; kabuk `no-cache` sunulur, `cache: 'no-store'` tarayıcı önbelleğini de atlar.
 */
export const SURUM_KAYNAGI = new InjectionToken<() => Promise<string | null>>('SURUM_KAYNAGI', {
  providedIn: 'root',
  factory: () => {
    const pencere = inject(DOCUMENT).defaultView;
    return async () => {
      if (!pencere?.fetch) return null;
      const yanit = await pencere.fetch('index.html', {
        cache: 'no-store',
        credentials: 'same-origin',
      });
      return yanit.ok ? yanit.text() : null;
    };
  },
});

/** Denetim aralığı; ayrıca sekme görünür olunca (en erken bu kadar sonra) denetlenir. */
export const SURUM_DENETIM_ARALIGI = 5 * 60_000;
export const GORUNURLUK_DENETIM_ARALIGI = 60_000;

/**
 * Sürüm imzası: kabuğun yüklediği ana paket adı (`main-HASH.js`). Production çıktısında içerik hash'li
 * olduğu için her yayında değişir; geliştirmede (`main.js`) hiç değişmez → bildirim çıkmaz. `ngsw` yok.
 */
export function surumImzasi(html: string): string | null {
  return /<script\b[^>]*\bsrc="([^"]*main[^"/]*\.js)"/i.exec(html)?.[1] ?? null;
}

/**
 * Yeni sürüm algılama (roadmap F3.3). Açık sekme yayından sonra eski paketle çalışmayı sürdürür (eski
 * parçalar bir yayın boyunca sunucuda tutulur — F2.2 `chunks.txt`); yeni `index.html` farklı ana paket
 * gösteriyorsa kalıcı "Yeni sürüm var — Yenile" bildirimi çıkar. Kullanıcı kaydetmeden zorla yenilenmez.
 */
@Injectable({ providedIn: 'root' })
export class SurumServisi {
  private readonly belge = inject(DOCUMENT);
  private readonly kaynak = inject(SURUM_KAYNAGI);
  private readonly sayfa = inject(SAYFA_YENILEYICI);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  /** Bu sekmenin çalıştırdığı sürüm (belgedeki ana paket). */
  readonly mevcut = surumImzasi(this.belge.documentElement.outerHTML);
  readonly yeniSurumVar = signal(false);
  private sonDenetim = 0;
  private durdur: (() => void) | null = null;

  baslat(): void {
    const pencere = this.belge.defaultView;
    if (this.durdur || !pencere || this.mevcut === null) return;
    const zamanlayici = pencere.setInterval(() => void this.denetle(), SURUM_DENETIM_ARALIGI);
    const gorunurluk = () => {
      if (
        this.belge.visibilityState === 'visible' &&
        Date.now() - this.sonDenetim >= GORUNURLUK_DENETIM_ARALIGI
      ) {
        void this.denetle();
      }
    };
    this.belge.addEventListener('visibilitychange', gorunurluk);
    this.durdur = () => {
      pencere.clearInterval(zamanlayici);
      this.belge.removeEventListener('visibilitychange', gorunurluk);
    };
    this.sonDenetim = Date.now();
    this.destroyRef.onDestroy(() => this.durdur?.());
  }

  /** Yayındaki sürümü okur; farklıysa bildirimi bir kez gösterir. Ağ hatası sessiz (sonraki denetim). */
  async denetle(): Promise<boolean> {
    if (this.yeniSurumVar() || this.mevcut === null) return this.yeniSurumVar();
    this.sonDenetim = Date.now();
    let html: string | null;
    try {
      html = await this.kaynak();
    } catch {
      return false;
    }
    const yayindaki = html === null ? null : surumImzasi(html);
    if (yayindaki === null || yayindaki === this.mevcut) return false;

    this.yeniSurumVar.set(true);
    this.durdur?.();
    this.toast.bilgi(this.t('surum.yeniSurum'), {
      sure: 0,
      eylem: { etiket: this.t('surum.yenile'), calistir: () => this.sayfa.yenile() },
    });
    return true;
  }
}
