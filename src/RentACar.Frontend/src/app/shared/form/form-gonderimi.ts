import { DestroyRef, Signal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { AbstractControl } from '@angular/forms';
import type { Observable } from 'rxjs';
import { apiHatasinaCevir, type ApiHatasi } from '@core/api/api-hatasi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';

export interface GonderimSecenekleri<T> {
  /** Sunucunun deterministik anahtarı (ör. DTO'daki `tahsilatAnahtar`) — istemci anahtarından önce. */
  readonly deterministikAnahtar?: string | null;
  /** Sunucu alan adı → form yolu (adlar farklıysa). */
  readonly esleme?: Readonly<Record<string, string>>;
  /** İstemci doğrulaması geçmedi ya da sunucu alan hatası döndü (ör. `sekmeliForm.ilkGecersizeGit()`). */
  readonly gecersiz?: () => void;
  readonly basarili?: (sonuc: T) => void;
  readonly hata?: (hata: ApiHatasi) => void;
}

export interface FormGonderimi {
  readonly kilit: GonderimKilidi;
  /** İstek uçarken `true` — gönder düğmesi `[disabled]`. */
  readonly gonderiliyor: Signal<boolean>;
  /** Alana bağlanamayan sunucu mesajları + alansız hata detayı (form üstünde `rc-form-hatalari`). */
  readonly genelHatalar: Signal<readonly string[]>;
  gonder<T>(
    form: AbstractControl,
    istek: (anahtar: string) => Observable<T>,
    secenek?: GonderimSecenekleri<T>,
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
export function formGonderimi(kilit: GonderimKilidi = new GonderimKilidi()): FormGonderimi {
  const destroyRef = inject(DestroyRef);
  const genelHatalar = signal<readonly string[]>([]);

  return {
    kilit,
    gonderiliyor: kilit.gonderiliyor,
    genelHatalar: genelHatalar.asReadonly(),
    gonder<T>(
      form: AbstractControl,
      istek: (anahtar: string) => Observable<T>,
      secenek: GonderimSecenekleri<T> = {},
    ): void {
      if (kilit.gonderiliyor()) return;
      sunucuHatalariniTemizle(form);
      genelHatalar.set([]);
      form.markAllAsTouched();
      if (form.invalid) {
        secenek.gecersiz?.();
        return;
      }
      const gonderilen = JSON.stringify(form.getRawValue());
      kilit
        .gonder(istek, { deterministikAnahtar: secenek.deterministikAnahtar ?? null })
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe({
          next: (sonuc) => {
            if (JSON.stringify(form.getRawValue()) === gonderilen) form.markAsPristine();
            secenek.basarili?.(sonuc);
          },
          error: (ham: unknown) => {
            const hata = apiHatasinaCevir(ham);
            const eslesmeyen = sunucuHatalariniUygula(form, hata.alanlar, secenek.esleme);
            if (hata.alanlar === undefined) genelHatalar.set([hata.detay]);
            else if (eslesmeyen.length > 0) genelHatalar.set(eslesmeyen);
            if (hata.alanlar !== undefined) secenek.gecersiz?.();
            secenek.hata?.(hata);
          },
        });
    },
  };
}
