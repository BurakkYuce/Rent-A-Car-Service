import { Injectable, type Signal, computed, inject, signal } from '@angular/core';

import type { ApiYolu } from '@core/api/api-istemcisi';
import { OturumServisi } from '@core/oturum/oturum-servisi';

import type { MoneyContent, MoneyNotice } from './money-notice';

/** Sonucu kesinleşmemiş (uçuşta ya da belirsiz) bir para gönderiminin kopyası: aynı yol + anahtar + gövde. */
export interface MoneyAttempt<TBody = unknown> {
  /** Anahtarın bağlı olduğu kayıt (bu MTV'nin ödemesi, bu servisin kalemi…); başka kayıt → yeni anahtar. */
  readonly target: string;
  readonly path: ApiYolu;
  readonly method: 'post' | 'put';
  readonly key: string;
  readonly body: TBody;
  /** Bildirimdeki "girdiğiniz" tutarı (kanca; gövdeye girmez). */
  readonly content: MoneyContent | null;
  /** Formun o anki ham değeri (bileşen yeniden kurulunca form aynı içerikle kilitli açılır). */
  readonly formValue: unknown;
  /** Yanıt henüz gelmedi. */
  readonly inFlight: boolean;
  /** Bileşen geri gelince gösterilecek not. */
  readonly notice: MoneyNotice;
}

/**
 * Sonucu kesinleşmemiş para denemeleri — kapsam (`scope`) başına bir kayıt. Deneme GÖNDERİLMEDEN ÖNCE "uçuşta" olarak
 * yazılır ve yalnız KESİN sonuçta (2xx, `mevcut`lu 409, kesin red, `cakisma`) silinir: form uçuştayken yok edilse de
 * anahtar ve gövde burada kalır, bileşen geri gelince form kilitli, AYNI gövde + anahtarla açılır — yeni anahtarla
 * ikinci ödeme yazılmaz.
 *
 * Varsayılan örnek köktedir; sayfa `providers`'ına koyarak kapsamı sayfaya daraltır (sayfa terk koruması ve "uçuşta"
 * düğme pasifliği o sayfanın denemelerini sayar). Yalnız bellekte (tarayıcı deposuna yazılmaz; tutar kişisel veri
 * sayılmasa da kalıcı kalmasın); çıkışta temizlenir.
 */
@Injectable({ providedIn: 'root' })
export class PendingMoneyAttempts {
  private readonly attempts = signal<ReadonlyMap<string, MoneyAttempt>>(new Map());
  /** Kesinleşmemiş deneme sayısı (sayfa terk koruması). */
  readonly count: Signal<number> = computed(() => this.attempts().size);
  /** Yanıtı beklenen gönderim var mı: formu yok eden düğmeler (Kapat, başka satır, süzgeç) pasif olmalı. */
  readonly inFlight: Signal<boolean> = computed(() =>
    [...this.attempts().values()].some((a) => a.inFlight),
  );

  constructor() {
    inject(OturumServisi, { optional: true })?.temizlikKaydet(() => this.attempts.set(new Map()));
  }

  get(scope: string): MoneyAttempt | undefined {
    return this.attempts().get(scope);
  }

  set(scope: string, attempt: MoneyAttempt): void {
    const next = new Map(this.attempts());
    next.set(scope, attempt);
    this.attempts.set(next);
  }

  delete(scope: string): void {
    if (!this.attempts().has(scope)) return;
    const next = new Map(this.attempts());
    next.delete(scope);
    this.attempts.set(next);
  }
}
