import { InjectionToken, Signal, signal } from '@angular/core';

import type { Ben } from './oturum-tipleri';

/**
 * Sunucudaki kimlik: kiracı + kullanıcı. Para işlemlerinde sunucunun idempotency anahtarı bundan türer (şube İÇERMEZ);
 * tekrar öncesi "aynı kimlik mi" karşılaştırması yalnız bununla yapılır.
 */
export function identityOfSession(ben: Pick<Ben, 'kiraci' | 'kullanici'>): string {
  return `${ben.kiraci.id}|${ben.kullanici.id}`;
}

/**
 * Oturum bağlamı anahtarının TEK kaynağı (kiracı|kullanıcı|şube kapsamı). `OturumServisi.baglam` ve para denemesi
 * kayıtları (`MoneyAttempt.context`) bunu kullanır — iki ayrı biçim, bağlamı her zaman "farklı" sayıp güvenlik kilidini
 * tersine çevirirdi (denemeler düşmez ya da hiç okunmaz).
 */
export function contextOfSession(ben: Pick<Ben, 'kiraci' | 'kullanici' | 'subeKapsami'>): string {
  const sube = ben.subeKapsami.tumSubeler ? '*' : (ben.subeKapsami.subeId ?? '-');
  return `${identityOfSession(ben)}|${sube}`;
}

/** {@link contextOfSession} anahtarının kimlik kısmı (kiracı|kullanıcı; şube hariç). */
export function identityOfContext(context: string): string {
  return context.split('|').slice(0, 2).join('|');
}

/**
 * Sayfa verisinin bağlı olduğu oturum bağlamı (Revlo'daki "seçili otel"in karşılığı). Anahtar
 * değişirse (başka kiracı/kullanıcıyla yeniden giriş, şube kapsamı değişti) açık sayfalar verilerini
 * yeniden yükler; `null` = oturum yok → store'lar sıfırlanır (önceki kullanıcının verisi ekranda kalmaz).
 */
export interface OturumBaglami {
  /** Bağlamı tekil tanımlayan opak anahtar (ör. `kiraciId|kullaniciId|subeKapsami`). */
  readonly anahtar: string;
}

/**
 * **YER TUTUCU (F3.4).** Gerçek değeri F3.3 oturum servisi sağlar:
 * `{ provide: OTURUM_BAGLAMI, useFactory: () => inject(OturumServisi).baglam }`.
 * O zamana kadar sabit, boş olmayan bir bağlam: sayfalar yüklenir, yetki sunucuda denetlenir
 * (oturum yoksa `oturum_yok`).
 */
export const OTURUM_BAGLAMI = new InjectionToken<Signal<OturumBaglami | null>>('OTURUM_BAGLAMI', {
  providedIn: 'root',
  factory: () => signal<OturumBaglami | null>({ anahtar: 'yer-tutucu' }).asReadonly(),
});
