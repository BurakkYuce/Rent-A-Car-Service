import { InjectionToken, Signal, signal } from '@angular/core';

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
