import { InjectionToken, type Signal } from '@angular/core';

/**
 * `rc-alan` sarmalayıcısının içindeki kontrole verdiği bağlam: odaklanan öğenin kimliği (etiketin
 * `for`'u), `aria-describedby` (ipucu + görünen hata), `aria-invalid`, `aria-required`.
 * Kontrol sarmalayıcısız da çalışır (bağlam `null`, kendi kimliğini üretir).
 */
export interface AlanBaglami {
  readonly kimlik: string;
  readonly etiketKimligi: string;
  readonly aciklayanlar: Signal<string | null>;
  readonly gecersiz: Signal<boolean>;
  readonly zorunlu: Signal<boolean>;
}

export const ALAN_BAGLAMI = new InjectionToken<AlanBaglami>('ALAN_BAGLAMI');

let sayac = 0;

/** Sayfa içinde tekil kimlik (`rc-a-12`). SSR yok; sayaç yeterli. */
export function tekilKimlik(onek = 'rc-a'): string {
  sayac += 1;
  return `${onek}-${sayac}`;
}
