import { DestroyRef, Signal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { AbstractControl } from '@angular/forms';
import type { Observable } from 'rxjs';
import { toApiError, type ApiHatasi } from '@core/api/api-hatasi';
import { SubmitLock } from '@core/form/submit-lock';
import { clearServerErrors, applyServerErrors } from '@core/form/sunucu-hatalari';
import { genelGosterilir } from '@core/oturum/session-interceptor';

export interface GonderimSecenekleri<T> {
  /** Sunucunun deterministik anahtarı (ör. DTO'daki `tahsilatAnahtar`) — istemci anahtarından önce. */
  readonly deterministikAnahtar?: string | null;
  /** Sunucu alan adı → form yolu (adlar farklıysa). */
  readonly esleme?: Readonly<Record<string, string>>;
  /** İstemci doğrulaması geçmedi ya da sunucu alan hatası döndü (ör. `sekmeliForm.ilkGecersizeGit()`). */
  readonly gecersiz?: () => void;
  readonly basarili?: (result: T) => void;
  readonly hata?: (error: ApiHatasi) => void;
}

export interface FormSubmission {
  readonly kilit: SubmitLock;
  /** İstek uçarken `true` — gönder düğmesi `[disabled]`. */
  readonly gonderiliyor: Signal<boolean>;
  /** Alana bağlanamayan sunucu mesajları + alansız hata detayı (form üstünde `rc-form-hatalari`). */
  readonly genelHatalar: Signal<readonly string[]>;
  gonder<T>(
    form: AbstractControl,
    request: (key: string) => Observable<T>,
    option?: GonderimSecenekleri<T>,
  ): void;
}

/**
 * Formun tek gönderim yolu (enjeksiyon bağlamında, bileşen alanı olarak):
 *
 * 1. Uçan gönderim varsa hiçbir şey yapmaz (çift tık tek istek).
 * 2. Önceki sunucu hataları kalkar, tüm alanlar "dokunuldu" olur; istemci doğrulaması geçmezse istek
 *    GİTMEZ, `gecersiz()` çağrılır.
 * 3. `GonderimKilidi` anahtar kuralıyla gönderir (`Idempotency-Key` → `ApiIstemcisi` `islemAnahtari`).
 * 4. 2xx: form `pristine` olur (kaydedilmemiş değişiklik koruması susar) — yalnız istek sürerken
 *    kullanıcı başka bir şey yazmadıysa.
 * 5. Hata: DEĞERLERE DOKUNULMAZ; `alanlar` ilgili alanların altına, eşleşmeyenler `genelHatalar`'a.
 *    `kod`'a göre genel davranış (oturum diyaloğu, bant, toast) F3.3 interceptor'ında.
 *
 * ```ts
 * protected readonly gonderim = formGonderimi();
 * kaydet() {
 *   this.gonderim.gonder(this.form, (anahtar) =>
 *     this.api.post('/api/ui/v1/araclar', this.govde(), { islemAnahtari: anahtar }),
 *     { gecersiz: () => this.sekmeli().ilkGecersizeGit(), basarili: (r) => this.yonlendir(r) });
 * }
 * ```
 */
export function formSubmission(lockEntry: SubmitLock = new SubmitLock()): FormSubmission {
  const destroyRef = inject(DestroyRef);
  const generalErrors = signal<readonly string[]>([]);

  return {
    kilit: lockEntry,
    gonderiliyor: lockEntry.gonderiliyor,
    genelHatalar: generalErrors.asReadonly(),
    gonder<T>(
      form: AbstractControl,
      request: (key: string) => Observable<T>,
      option: GonderimSecenekleri<T> = {},
    ): void {
      if (lockEntry.gonderiliyor()) return;
      clearServerErrors(form);
      generalErrors.set([]);
      form.markAllAsTouched();
      if (form.invalid) {
        option.gecersiz?.();
        return;
      }
      const sent = JSON.stringify(form.getRawValue());
      lockEntry
        .gonder(request, { deterministikAnahtar: option.deterministikAnahtar ?? null })
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe({
          next: (result) => {
            if (JSON.stringify(form.getRawValue()) === sent) form.markAsPristine();
            option.basarili?.(result);
          },
          error: (raw: unknown) => {
            const error = toApiError(raw);
            const unmatched = applyServerErrors(form, error.alanlar, option.esleme);
            // Bant/toast'ta gösterilen (yetki_yok, alansız cakisma, 5xx…) forma ikinci kez yazılmaz (F3.3).
            if (error.alanlar === undefined && !genelGosterilir(error))
              generalErrors.set([error.detay]);
            else if (unmatched.length > 0) generalErrors.set(unmatched);
            if (error.alanlar !== undefined) option.gecersiz?.();
            option.hata?.(error);
          },
        });
    },
  };
}
