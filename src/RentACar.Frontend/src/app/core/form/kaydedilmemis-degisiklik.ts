import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, InjectionToken, inject } from '@angular/core';
import type { CanDeactivateFn } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { TabRouteStrategy } from '@core/sekme/sekme-stratejisi';

/** Kaydedilmemiş değişikliği olan sayfa bileşeni (rota `canDeactivate`'i bunu sorar). */
export interface UnsavedChangesOwner {
  hasUnsavedChanges(): boolean;
  /**
   * İsteğe bağlı özel onay metni (ör. sonucu bilinmeyen para işlemi: "kasa hareketlerini kontrol edin");
   * `null`/yoksa genel "kaydedilmemiş değişiklik" metni.
   */
  unsavedChangesMessage?(): string | null;
}

/** Bileşenin özel terk metni (varsa). Tip güvenli yoklama — sekme servisi de kullanır. */
export function leaveMessage(component: unknown): string | null {
  if (typeof component !== 'object' || component === null) return null;
  const f = (component as Partial<UnsavedChangesOwner>).unsavedChangesMessage;
  return typeof f === 'function' ? (f.call(component) ?? null) : null;
}

export type ConfirmPrompt = (message: string) => boolean | Promise<boolean>;

/**
 * Onay penceresi. Varsayılan tarayıcının `confirm`'ü (bağımlılıksız, erişilebilir, Esc = vazgeç);
 * F3.3'ün CDK onay diyaloğu bu token'ı değiştirir, guard değişmez.
 */
export const CONFIRM_PROMPT = new InjectionToken<ConfirmPrompt>('ONAY_ISTEMI', {
  providedIn: 'root',
  factory: () => {
    const window = inject(DOCUMENT).defaultView;
    return (message) => window?.confirm(message) ?? true;
  },
});

/** Tam sayfa gezinme (Blazor ekranı). Testlerde değiştirilir (jsdom gezinemez). */
export const FULL_PAGE_NAVIGATION = new InjectionToken<(address: string) => void>(
  'TAM_SAYFA_GEZINMESI',
  {
    providedIn: 'root',
    factory: () => {
      const window = inject(DOCUMENT).defaultView;
      return (address) => window?.location.assign(address);
    },
  },
);

/**
 * Kullanıcı ayrılmayı TOPLU onayladığında (F3.2 kabuk: Blazor ekranına geçiş, çıkış) guard ve
 * `beforeunload` aynı soruyu ikinci kez sormasın diye bayrak. Onay kabukta tüm sekmeler için bir kez
 * alınır (`SekmeServisi.ayrilmaOnayi`).
 */
@Injectable({ providedIn: 'root' })
export class PageLeave {
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly navigate = inject(FULL_PAGE_NAVIGATION);
  private approval = false;

  /** Onay alındı: şu an ayrılma soruları atlanır. */
  get confirmed(): boolean {
    return this.approval;
  }

  /** Onaylanmış uygulama içi işlem (çıkış): süresince guard sormaz, bitince bayrak iner. */
  async runConfirmed<T>(is: () => Promise<T>): Promise<T> {
    this.approval = true;
    try {
      return await is();
    } finally {
      this.approval = false;
    }
  }

  /**
   * Onaylanmış tam sayfa terk (SPA dışı adres): `beforeunload` ikinci kez sormaz. Sayfa geri/ileri
   * önbelleğinden dönerse (`pageshow`) bayrak iner, koruma yeniden devrededir.
   */
  goToFullPage(address: string): void {
    this.approval = true;
    this.window?.addEventListener('pageshow', () => (this.approval = false), { once: true });
    this.navigate(address);
  }
}

/**
 * Uygulama içi gezinmede (rota değişimi) kirli formu korur. Rotaya `canDeactivate: [kaydedilmemisDegisiklikGuard]`.
 * Başarılı kayıttan sonra form `markAsPristine` edildiği için soru gelmez.
 *
 * Sekmeli çalışma alanı (F3.2): sayfanın sekmesi AÇIK kalıyorsa (başka sekmeye geçiliyor) bileşen
 * arka planda yaşamaya devam eder, değişiklik kaybolmaz → sorulmaz. Sekme kapatılırken, sekme dışı
 * sayfada ve oturum/sekme temizliğinde sorulur.
 */
export const unsavedChangesGuard: CanDeactivateFn<Partial<UnsavedChangesOwner> | null> = (
  component,
  currentRoute,
) => {
  if (!component?.hasUnsavedChanges?.()) return true;
  if (inject(TabRouteStrategy).shouldStore(currentRoute)) return true;
  if (inject(PageLeave).confirmed) return true;
  const message =
    leaveMessage(component) ??
    inject(TranslocoService).translate<string>('form.kaydedilmemis.onay');
  return inject(CONFIRM_PROMPT)(message);
};

/** Bileşen `KaydedilmemisDegisiklikSahibi` ve şu an kirli mi (tip güvenli yoklama). */
export function isDirtyComponent(component: unknown): boolean {
  if (typeof component !== 'object' || component === null) return false;
  const poll = (component as Partial<UnsavedChangesOwner>).hasUnsavedChanges;
  return typeof poll === 'function' && poll.call(component) === true;
}

/**
 * Sekme kapatma / yenileme / Blazor ekranına tam sayfa geçiş için `beforeunload`. Bileşen
 * kurucusunda (enjeksiyon bağlamı) çağrılır; bileşen yok olunca dinleyici kalkar. Tarayıcı kendi
 * sabit metnini gösterir (özel metin 2016'dan beri yok sayılıyor).
 */
export function pageLeaveGuard(isDirty: () => boolean): void {
  const window = inject(DOCUMENT).defaultView;
  const leave = inject(PageLeave);
  if (!window) return;
  const listener = (evt: BeforeUnloadEvent): void => {
    if (leave.confirmed || !isDirty()) return;
    evt.preventDefault();
    // Eski tarayıcılar yalnız returnValue'ya bakar.
    evt.returnValue = '';
  };
  window.addEventListener('beforeunload', listener);
  inject(DestroyRef).onDestroy(() => window.removeEventListener('beforeunload', listener));
}
