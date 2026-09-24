import { DestroyRef, Injectable, type Signal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { FormGroup } from '@angular/forms';
import { finalize } from 'rxjs';

import { type ApiHatasi, apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import { sonucuBilinmeyenHata } from '@core/form/tahsilat-denemesi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';

import { type FormNotice, recordedNotice } from './document-requests';

/** Sonucu bilinmeyen (ya da yarışı kaybetmiş) gönderimin DONMUŞ kopyası: aynı yol + anahtar + birebir aynı gövde. */
export interface FrozenDocumentAttempt {
  readonly path: ApiYolu;
  readonly key: string;
  readonly body: unknown;
  readonly notice: FormNotice;
  /** Formun o anki değeri (kayda geri dönülünce form aynı içerikle kilitli açılır). */
  readonly formValue: unknown;
}

/**
 * Sayfa düzeyinde donmuş denemeler (sayfanın `providers`'ı). Kayıt başına kurulan ödeme formu başka satıra geçilince
 * yok olur; belirsiz denemenin ANAHTARI ve gövdesi burada kalır, kayda dönülünce form kilitli geri gelir (r300 L4).
 * Yalnız bellekte (anahtar + tutar gövdesi; tarayıcı deposuna yazılmaz).
 */
@Injectable()
export class PendingDocumentAttempts {
  private readonly attempts = new Map<string, FrozenDocumentAttempt>();
  private readonly count = signal(0);
  /** Donmuş deneme var mı (sayfa terk koruması). */
  readonly any: Signal<number> = this.count.asReadonly();

  get(scope: string): FrozenDocumentAttempt | undefined {
    return this.attempts.get(scope);
  }

  set(scope: string, attempt: FrozenDocumentAttempt): void {
    this.attempts.set(scope, attempt);
    this.count.set(this.attempts.size);
  }

  delete(scope: string): void {
    this.attempts.delete(scope);
    this.count.set(this.attempts.size);
  }
}

export interface DocumentSubmitHandlers<T> {
  /** 2xx. Form ÇAĞIRAN tarafından sıfırlanır. */
  readonly succeeded: (result: T) => void;
  /** 409 `mukerrer` + `mevcut`: önceki deneme kayıtlı; çağıran formu sıfırlar ve listeyi yeniler. */
  readonly recorded?: () => void;
  /** Kaydı yeniden oku (`mevcut`suz `mukerrer`, `cakisma`). */
  readonly reload?: () => void;
  /** İstemci doğrulaması ya da sunucu alan hatası (sekme/alan odaklama). */
  readonly invalid?: () => void;
}

/**
 * Başlık anahtarlı PARA gönderimi (manuel fatura, gider, gider ödemesi, ceza ödemesi) — DEVIR §5 "Para formu yaşam
 * döngüsü", kardeş #299 `MoneyOperation` sözleşmesiyle aynı (kopya değil; bu ekranların `mevcut` kuralları farklı):
 *
 * - İşlem başına bir `Idempotency-Key`; kesin redde (doğrulama, yetki, dönem kilidi) KORUNUR — düzeltilmiş gövde aynı
 *   işlemdir ve önceki deneme yazıldıysa sunucu önce anahtarı arar (409, ikinci belge yok).
 * - İstek uçarken form KİLİTLİ (gönderilen gövde ile ekrandaki değer ayrışmasın — r300 M1).
 * - Sonucu bilinmeyen hata (ağ/5xx): gövde DONAR, form kilitli kalır; tekrar YALNIZ donmuş gövde + aynı anahtarla
 *   (r300 HIGH-1: düzeltilmiş tutar aynı anahtarla gidip "başka kayıt" sanılıyor, ikinci belge yazılıyordu).
 * - 409 `mukerrer` + `mevcut` (aynı ya da farklı içerik): "önceki denemeniz kaydedildi (No, tutar); değiştirdiğiniz
 *   içerik yazılmadı" — anahtar yenilenir, form sıfırlanır; düzeltme iade/iptalle.
 * - 409 `mukerrer`, `mevcut` YOK (yarış kaybı; ilk istek hâlâ işleniyor olabilir): anahtar YENİLENMEZ, gövde donar,
 *   kayıt yenilenir; tekrar aynı anahtarla gider ve sunucu kesin sonucu verir (r300 M3).
 * - 409 `cakisma`: kayıt değişti, yeni anahtar, form korunur.
 * - Toast'u interceptor değil form notu gösterir (`mukerrerCagiranGosterir`; aynı metin iki yerde çıkmasın — r300 L1).
 */
export class DocumentSubmission {
  private readonly api = inject(ApiIstemcisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly pending = inject(PendingDocumentAttempts);
  private key: string | null = null;

  private readonly sendingState = signal(false);
  private readonly frozenState = signal<FrozenDocumentAttempt | null>(null);
  private readonly errorsState = signal<readonly string[]>([]);
  private readonly noticeState = signal<FormNotice | null>(null);

  readonly sending = this.sendingState.asReadonly();
  /** Donmuş deneme — varken form kilitli, düğme "aynı işlemi tekrar gönder". */
  readonly frozen = this.frozenState.asReadonly();
  readonly generalErrors = this.errorsState.asReadonly();
  readonly notice = this.noticeState.asReadonly();

  constructor(
    private readonly form: FormGroup,
    private readonly scope: () => string,
    private readonly mapping: Readonly<Record<string, string>> = {},
    private readonly newKey: () => string = yeniIslemAnahtari,
  ) {}

  /** Kayda dönülünce: bu kapsamın donmuş denemesi varsa form aynı içerikle KİLİTLİ açılır. */
  restore(): void {
    const f = this.pending.get(this.scope());
    if (!f) return;
    this.form.reset(f.formValue as never, { emitEvent: false });
    this.lock(f);
  }

  /** Bir sonraki gönderimin anahtarı (test/teşhis). */
  get currentKey(): string | null {
    return this.frozenState()?.key ?? this.key;
  }

  submit<T>(path: ApiYolu, build: () => unknown, h: DocumentSubmitHandlers<T>): void {
    if (this.sendingState()) return;
    const frozen = this.frozenState();
    let attempt: { path: ApiYolu; key: string; body: unknown };
    let formValue: unknown;
    if (frozen) {
      attempt = frozen;
      formValue = frozen.formValue;
    } else {
      sunucuHatalariniTemizle(this.form);
      this.errorsState.set([]);
      this.form.markAllAsTouched();
      if (this.form.invalid) {
        h.invalid?.();
        return;
      }
      this.key ??= this.newKey();
      attempt = { path, key: this.key, body: build() };
      formValue = this.form.getRawValue();
    }
    this.noticeState.set(null);
    this.sendingState.set(true);
    this.form.disable({ emitEvent: false });
    this.api
      .post<T>(attempt.path, attempt.body, {
        islemAnahtari: attempt.key,
        context: istekBaglami({ mukerrerCagiranGosterir: true }),
      })
      .pipe(
        finalize(() => this.sendingState.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.clear();
          h.succeeded(result);
        },
        error: (raw: unknown) => this.failed(apiHatasinaCevir(raw), attempt, formValue, h),
      });
  }

  /** Kullanıcı donmuş denemeden BİLİNÇLİ vazgeçti (onaylı): kopya ve anahtar bırakılır, form açılır. */
  abandon(): void {
    this.clear();
    this.noticeState.set(null);
  }

  /** Form yeni bir kayda sıfırlandı: sonraki gönderim yeni anahtarla. Donmuş deneme varken çağrılmaz. */
  renew(): void {
    if (this.frozenState()) return;
    this.key = null;
  }

  private failed<T>(
    error: ApiHatasi,
    attempt: { path: ApiYolu; key: string; body: unknown },
    formValue: unknown,
    h: DocumentSubmitHandlers<T>,
  ): void {
    if (sonucuBilinmeyenHata(error)) {
      this.freeze(attempt, formValue, { tone: 'uyari', key: 'belirsiz', params: {} });
      return;
    }
    if (error.kod === 'mukerrer' && error.mevcut) {
      this.clear();
      this.noticeState.set(recordedNotice(error.mevcut));
      h.recorded?.();
      h.reload?.();
      return;
    }
    if (error.kod === 'mukerrer') {
      this.freeze(attempt, formValue, { tone: 'uyari', key: 'olabilir', params: {} });
      h.reload?.();
      return;
    }
    // Kesin red: yazılmadı. Donmuş kopya varsa o gövde de reddedildi (önceki deneme yazılmamıştı).
    this.unfreeze();
    if (error.kod === 'cakisma') {
      this.key = null;
      h.reload?.();
      return;
    }
    const unmatched = sunucuHatalariniUygula(this.form, error.alanlar, this.mapping);
    if (error.alanlar === undefined && !genelGosterilir(error)) this.errorsState.set([error.detay]);
    else if (unmatched.length > 0) this.errorsState.set(unmatched);
    if (error.alanlar !== undefined) h.invalid?.();
  }

  private freeze(
    attempt: { path: ApiYolu; key: string; body: unknown },
    formValue: unknown,
    notice: FormNotice,
  ): void {
    const f: FrozenDocumentAttempt = { ...attempt, notice, formValue };
    this.key = attempt.key;
    this.pending.set(this.scope(), f);
    this.lock(f);
  }

  private lock(f: FrozenDocumentAttempt): void {
    this.key = f.key;
    this.frozenState.set(f);
    this.noticeState.set(f.notice);
    this.form.disable({ emitEvent: false });
  }

  private unfreeze(): void {
    this.frozenState.set(null);
    this.pending.delete(this.scope());
    this.form.enable({ emitEvent: false });
  }

  private clear(): void {
    this.unfreeze();
    this.key = null;
  }
}
