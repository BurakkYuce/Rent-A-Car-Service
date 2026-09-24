import { DestroyRef, Injectable, type Signal, computed, inject, signal } from '@angular/core';
import type { FormGroup } from '@angular/forms';

import { type ApiHatasi, apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import { sonucuBilinmeyenHata } from '@core/form/tahsilat-denemesi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';

import { type FormNotice, recordedNotice } from './document-requests';

/** Sonucu bilinmeyen (uçuşta, belirsiz ya da yarışı kaybetmiş) gönderimin kopyası: aynı yol + anahtar + gövde. */
export interface FrozenDocumentAttempt {
  readonly path: ApiYolu;
  readonly key: string;
  readonly body: unknown;
  readonly notice: FormNotice;
  /** Formun o anki değeri (kayda geri dönülünce form aynı içerikle kilitli açılır). */
  readonly formValue: unknown;
  /** İstek hâlâ uçuşta (yanıt gelmedi). */
  readonly inFlight: boolean;
}

const UNCERTAIN: FormNotice = { tone: 'uyari', key: 'belirsiz', params: {} };

/**
 * Sayfa düzeyinde sonucu kesinleşmemiş denemeler (sayfanın `providers`'ı). Deneme GÖNDERİLMEDEN ÖNCE "uçuşta" olarak
 * yazılır ve yalnız KESİN sonuçta (2xx, `mevcut`lu 409, kesin red) silinir (r300b N1): form uçuştayken yok edilse de
 * anahtar ve gövde burada kalır, kayda dönülünce form kilitli, AYNI gövde + anahtarla açılır — yeni anahtarla ikinci
 * ödeme yazılmaz. Yalnız bellekte (anahtar + tutar gövdesi; tarayıcı deposuna yazılmaz).
 */
@Injectable()
export class PendingDocumentAttempts {
  private readonly attempts = signal<ReadonlyMap<string, FrozenDocumentAttempt>>(new Map());
  /** Kesinleşmemiş deneme sayısı (sayfa terk koruması). */
  readonly any: Signal<number> = computed(() => this.attempts().size);
  /** Yanıtı beklenen gönderim var mı (Kapat / Detay / başka satırın Öde düğmesi pasif). */
  readonly inFlight: Signal<boolean> = computed(() =>
    [...this.attempts().values()].some((a) => a.inFlight),
  );

  get(scope: string): FrozenDocumentAttempt | undefined {
    return this.attempts().get(scope);
  }

  set(scope: string, attempt: FrozenDocumentAttempt): void {
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

export interface DocumentSubmitHandlers<T> {
  /** 2xx. Form ÇAĞIRAN tarafından sıfırlanır. */
  readonly succeeded: (result: T) => void;
  /** 409 `mukerrer` + `mevcut`: önceki deneme kayıtlı; çağıran formu sıfırlar ve listeyi yeniler. */
  readonly recorded?: () => void;
  /** Kaydı yeniden oku (`mevcut`suz `mukerrer`, `cakisma`). */
  readonly reload?: () => void;
  /** İstemci doğrulaması ya da sunucu alan hatası (sekme/alan odaklama). */
  readonly invalid?: () => void;
  /** Kesin red; `retry`: donmuş (sonucu bilinmeyen) denemenin tekrarı reddedildi (bağlam notu için). */
  readonly rejected?: (error: ApiHatasi, retry: boolean) => void;
}

/**
 * Başlık anahtarlı PARA gönderimi (manuel fatura, gider, gider ödemesi, ceza ödemesi, araç satışı) — DEVIR §5 "Para
 * formu yaşam döngüsü", kardeş #299 `MoneyOperation` sözleşmesiyle aynı (birleştirme ayrı iş):
 *
 * - İşlem başına bir `Idempotency-Key`; kesin redde KORUNUR (düzeltilmiş gövde aynı işlemdir).
 * - Gönderimden ÖNCE deneme sayfaya "uçuşta" yazılır; istek uçarken form KİLİTLİ (r300 M1) ve bileşen yok edilse de
 *   istek İPTAL EDİLMEZ — sonuç sayfadaki kaydı günceller (r300b N1).
 * - Sonucu bilinmeyen hata (ağ/5xx): gövde DONAR, form kilitli; tekrar YALNIZ donmuş gövde + aynı anahtarla (r300 H1).
 * - 409 `mukerrer` + `mevcut`: önceki deneme kayıtlı — anahtar yenilenir, form sıfırlanır.
 * - 409 `mukerrer`, `mevcut` YOK (yarış): anahtar YENİLENMEZ, gövde donar, kayıt yenilenir (r300 M3).
 * - 409 `cakisma`: kayıt değişti, yeni anahtar, form korunur.
 * - Toast'u interceptor değil form notu gösterir (`mukerrerCagiranGosterir` — r300 L1).
 */
export class DocumentSubmission {
  private readonly api = inject(ApiIstemcisi);
  private readonly pending = inject(PendingDocumentAttempts);
  private key: string | null = null;
  private destroyed = false;

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
  ) {
    inject(DestroyRef).onDestroy(() => (this.destroyed = true));
  }

  /**
   * Kayda dönülünce: bu kapsamın kesinleşmemiş denemesi (uçuşta ya da belirsiz) varsa form aynı içerikle KİLİTLİ açılır.
   * Uçuştaki istek bu bileşenden değilse sonucu burada bilinmez: "sonucu bilinmiyor" sayılır; tekrar aynı anahtarla.
   */
  restore(): void {
    const f = this.pending.get(this.scope());
    if (!f) return;
    this.form.reset(f.formValue as never, { emitEvent: false });
    this.lock(f.inFlight ? { ...f, notice: UNCERTAIN } : f);
  }

  /** Bir sonraki gönderimin anahtarı (test/teşhis). */
  get currentKey(): string | null {
    return this.frozenState()?.key ?? this.key;
  }

  /** Çağıranın bağlam notu (ör. satışta "araç zaten satılmış" = önceki deneme yazıldı). */
  showNotice(notice: FormNotice): void {
    this.noticeState.set(notice);
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
    const scope = this.scope();
    const retry = frozen !== null;
    // r300b N1: gönderimden ÖNCE sayfaya yaz — bileşen yok edilse de deneme kaybolmaz.
    this.pending.set(scope, { ...attempt, notice: UNCERTAIN, formValue, inFlight: true });
    this.noticeState.set(null);
    this.sendingState.set(true);
    this.form.disable({ emitEvent: false });
    // Bileşen yok edilse de istek İPTAL EDİLMEZ (sunucu yazmış olabilir); sonuç sayfadaki kaydı günceller.
    this.api
      .post<T>(attempt.path, attempt.body, {
        islemAnahtari: attempt.key,
        context: istekBaglami({ mukerrerCagiranGosterir: true }),
      })
      .subscribe({
        next: (result) => {
          this.pending.delete(scope);
          if (this.destroyed) return;
          this.sendingState.set(false);
          this.clear();
          h.succeeded(result);
        },
        error: (raw: unknown) => {
          const error = apiHatasinaCevir(raw);
          this.settle(scope, error, attempt, formValue);
          if (this.destroyed) return;
          this.sendingState.set(false);
          this.failed(error, attempt, h, retry);
        },
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

  /** Sayfadaki kaydı sonuca göre günceller (bileşen yok edilmiş olsa da). */
  private settle(
    scope: string,
    error: ApiHatasi,
    attempt: { path: ApiYolu; key: string; body: unknown },
    formValue: unknown,
  ): void {
    if (sonucuBilinmeyenHata(error))
      this.pending.set(scope, { ...attempt, notice: UNCERTAIN, formValue, inFlight: false });
    else if (error.kod === 'mukerrer' && !error.mevcut)
      this.pending.set(scope, {
        ...attempt,
        notice: { tone: 'uyari', key: 'olabilir', params: {} },
        formValue,
        inFlight: false,
      });
    else this.pending.delete(scope);
  }

  private failed<T>(
    error: ApiHatasi,
    attempt: { path: ApiYolu; key: string; body: unknown },
    h: DocumentSubmitHandlers<T>,
    retry: boolean,
  ): void {
    const stored = this.pending.get(this.scope());
    if (stored) {
      this.lock(stored);
      if (error.kod === 'mukerrer') h.reload?.();
      return;
    }
    if (error.kod === 'mukerrer' && error.mevcut) {
      this.clear();
      this.noticeState.set(recordedNotice(error.mevcut));
      h.recorded?.();
      h.reload?.();
      return;
    }
    // Kesin red: yazılmadı. Donmuş kopya varsa o gövde de reddedildi.
    this.unlock();
    if (error.kod === 'cakisma') {
      this.key = null;
      h.reload?.();
      return;
    }
    this.key = attempt.key;
    const unmatched = sunucuHatalariniUygula(this.form, error.alanlar, this.mapping);
    if (error.alanlar === undefined && !genelGosterilir(error)) this.errorsState.set([error.detay]);
    else if (unmatched.length > 0) this.errorsState.set(unmatched);
    if (error.alanlar !== undefined) h.invalid?.();
    h.rejected?.(error, retry);
  }

  private lock(f: FrozenDocumentAttempt): void {
    this.key = f.key;
    this.frozenState.set(f);
    this.noticeState.set(f.notice);
    this.form.disable({ emitEvent: false });
  }

  private unlock(): void {
    this.frozenState.set(null);
    this.pending.delete(this.scope());
    this.form.enable({ emitEvent: false });
  }

  private clear(): void {
    this.unlock();
    this.key = null;
  }
}
