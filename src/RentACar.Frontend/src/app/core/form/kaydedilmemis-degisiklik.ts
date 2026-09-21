import { DOCUMENT } from '@angular/common';
import { DestroyRef, InjectionToken, inject } from '@angular/core';
import type { CanDeactivateFn } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

/** Kaydedilmemiş değişikliği olan sayfa bileşeni (rota `canDeactivate`'i bunu sorar). */
export interface KaydedilmemisDegisiklikSahibi {
  kaydedilmemisDegisiklikVar(): boolean;
}

export type OnayIstemi = (mesaj: string) => boolean | Promise<boolean>;

/**
 * Onay penceresi. Varsayılan tarayıcının `confirm`'ü (bağımlılıksız, erişilebilir, Esc = vazgeç);
 * F3.3'ün CDK onay diyaloğu bu token'ı değiştirir, guard değişmez.
 */
export const ONAY_ISTEMI = new InjectionToken<OnayIstemi>('ONAY_ISTEMI', {
  providedIn: 'root',
  factory: () => {
    const pencere = inject(DOCUMENT).defaultView;
    return (mesaj) => pencere?.confirm(mesaj) ?? true;
  },
});

/**
 * Uygulama içi gezinmede (rota değişimi) kirli formu korur. Rotaya `canDeactivate: [kaydedilmemisDegisiklikGuard]`.
 * Başarılı kayıttan sonra form `markAsPristine` edildiği için soru gelmez.
 */
export const kaydedilmemisDegisiklikGuard: CanDeactivateFn<
  Partial<KaydedilmemisDegisiklikSahibi> | null
> = (bilesen) => {
  if (!bilesen?.kaydedilmemisDegisiklikVar?.()) return true;
  const mesaj = inject(TranslocoService).translate('form.kaydedilmemis.onay');
  return inject(ONAY_ISTEMI)(mesaj);
};

/**
 * Sekme kapatma / yenileme / Blazor ekranına tam sayfa geçiş için `beforeunload`. Bileşen
 * kurucusunda (enjeksiyon bağlamı) çağrılır; bileşen yok olunca dinleyici kalkar. Tarayıcı kendi
 * sabit metnini gösterir (özel metin 2016'dan beri yok sayılıyor).
 */
export function sayfaTerkKorumasi(kirliMi: () => boolean): void {
  const pencere = inject(DOCUMENT).defaultView;
  if (!pencere) return;
  const dinleyici = (olay: BeforeUnloadEvent): void => {
    if (!kirliMi()) return;
    olay.preventDefault();
    // Eski tarayıcılar yalnız returnValue'ya bakar.
    olay.returnValue = '';
  };
  pencere.addEventListener('beforeunload', dinleyici);
  inject(DestroyRef).onDestroy(() => pencere.removeEventListener('beforeunload', dinleyici));
}
