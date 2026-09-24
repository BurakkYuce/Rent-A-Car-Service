import { DestroyRef, type Signal, effect, inject, signal, untracked } from '@angular/core';
import type { AbstractControl, FormGroup } from '@angular/forms';

import { type ApiHatasi, apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { OturumServisi } from '@core/oturum/oturum-servisi';

import { yeniIslemAnahtari } from './gonderim-kilidi';
import { type MoneyAttempt, PendingMoneyAttempts, sessionContext } from './money-attempts';
import {
  type DuplicateKind,
  type MoneyContent,
  type MoneyNotice,
  UNCERTAIN_NOTICE,
  classifyDuplicate,
  duplicateNotice,
} from './money-notice';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from './sunucu-hatalari';
import { sonucuBilinmeyenHata } from './tahsilat-denemesi';

/** Yeni bir para işleminin isteği (donmuş deneme YOKSA kurulur). */
export interface MoneyRequest<TBody> {
  readonly path: ApiYolu;
  readonly body: TBody;
  /** Anahtarın bağlı olduğu kayıt; başka kayda geçilince yeni anahtar. Varsayılan: kapsam. */
  readonly target?: string;
  /** Kanca: bildirimdeki "girdiğiniz" tutarı (ör. servis kaleminin NET satır tutarı). Gövdeye girmez. */
  readonly content?: MoneyContent | null;
  readonly method?: 'post' | 'put';
}

/** `settled` nedeni: 2xx, `mukerrer`, `cakisma` — ekran verisi yeniden okunmalı. */
export type MoneySettleReason = 'done' | 'duplicate' | 'conflict';

export interface MoneyRunOptions<TBody, TResult> {
  /** Uçuşta ve donmuşken kilitlenen, hataların yazıldığı form. */
  readonly form: AbstractControl;
  /** Yeni istek; `null` → gönderim yok. Donmuş deneme varken ÇAĞRILMAZ (tekrar kopyadan gider). */
  readonly build: () => MoneyRequest<TBody> | null;
  /** Yalnız yeni işlemde sorulur (donmuş denemenin tekrarı aynı işlemdir). Açıkken ikinci tık yok sayılır. */
  readonly confirm?: () => Promise<boolean>;
  /** Sunucu alan adı → form yolu; hata ANINDA çağrılır (toplu satırlar gönderilen kopyadan eşlenir). */
  readonly fieldMap?: () => Readonly<Record<string, string>>;
  readonly success: (result: TResult, attempt: MoneyAttempt<TBody>) => void;
  readonly settled?: (reason: MoneySettleReason) => void;
  /** Kanca: `mevcut`lu 409 sonrası (önceki deneme KAYITLI) form sıfırlaması — tam ya da kısmi. */
  readonly afterDuplicate?: (kind: DuplicateKind) => void;
  /** Kanca: 409 `cakisma` — form SİLİNMEZ; güncel kayıt dokunulmamış alanlara birleştirilebilir. */
  readonly conflict?: () => void;
  /** İstemci doğrulaması geçmedi ya da sunucu alan hatası döndü (sekme/alan odaklama). */
  readonly invalid?: () => void;
  /** Kesin red; `retry`: sonucu bilinmeyen denemenin tekrarı reddedildi (bağlam notu için). */
  readonly rejected?: (error: ApiHatasi, retry: boolean) => void;
}

export interface MoneySubmissionConfig {
  /**
   * Deneme kaydının anahtarı (ör. `ceza-odeme:{id}`). Verilirse bileşen yok olup geri gelince `restore()` donmuş
   * denemeyi geri getirir. Verilmezse örneğe özgü kapsam: bileşen yok olunca kesinleşmiş deneme kayıttan düşer.
   */
  readonly scope?: () => string;
  /**
   * Kanca: `mukerrer` sonrası yeni gönderim ancak bu alan AÇIKÇA doldurulunca (boş tutar "kalanın tamamı" demekse).
   * Kilit yalnız BAŞARILI gönderimle kalkar; yazıp silmek açmaz.
   */
  readonly amountControl?: () => AbstractControl;
  /**
   * `mukerrer` bildiriminin yeri: `notice` (varsayılan; form altında kalıcı) ya da `toast` — form, kayıt yenilenince
   * gizlenebiliyorsa (ödenen poliçe/taksit paneli) not onunla kaybolurdu. Metin iki yerde de AYNI.
   */
  readonly duplicateDisplay?: 'notice' | 'toast';
  /**
   * Yapısal uçlar (anahtar sunucuda kayıt durumudur: poliçe başına tek ödeme, servis başına tek yansıtma): `mevcut`
   * başkasının işlemi olabilir — "önceki denemeniz" yerine bu nötr metin (No + tutar parametreli). r316 L1.
   */
  readonly recordedMessage?: CeviriAnahtari;
  /** Kapsam dışı `Idempotency-Key` üretimi (test). */
  readonly newKey?: () => string;
}

/** Şablonun okuduğu durum (gönder şeridi gövde tipinden bağımsız). */
export interface MoneySubmissionState {
  readonly sending: Signal<boolean>;
  readonly frozen: Signal<object | null>;
  readonly errors: Signal<readonly string[]>;
  readonly notice: Signal<MoneyNotice | null>;
  abandon(): Promise<boolean>;
}

let instanceCounter = 0;

/**
 * Para yazan formların TEK gönderim çekirdeği (DEVIR §5 "Para formu yaşam döngüsü"; işlem başına rastgele anahtar):
 *
 * - İşlem başına bir `Idempotency-Key`; 2xx ya da `mevcut`lu 409 sonrası yenilenir; kesin redde (400/403) KORUNUR
 *   (düzeltilmiş gövde aynı işlemdir); `cakisma`da yenilenir.
 * - Deneme gönderimden ÖNCE {@link PendingMoneyAttempts}'e "uçuşta" yazılır; istek bileşen yok edilince İPTAL
 *   EDİLMEZ (sunucu yazmış olabilir), sonuç kaydı günceller.
 * - İstek uçarken form KİLİTLİ. Sonucu bilinmeyen hatada (ağ/5xx) yol + anahtar + gövde DONAR, form kilitli kalır;
 *   tekrar YALNIZ donmuş kopyayla. Kesin redde önce kilit açılır, sonra alan hataları yazılır.
 * - 409 `mukerrer`: otomatik tekrar YOK; not {@link duplicateNotice}. `mevcut`suz 409'da anahtar YENİLENMEZ, gövde
 *   donar (tekrar kesin sonucu getirir); bilinçli "Vazgeç" (onaylı) yeni işlem başlatır.
 * - `mukerrer` bildirimini interceptor değil form notu gösterir (`mukerrerCagiranGosterir`).
 */
export class MoneySubmission<TBody = unknown> implements MoneySubmissionState {
  private readonly api = inject(ApiIstemcisi);
  private readonly attempts = inject(PendingMoneyAttempts);
  private readonly confirmService = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly session = inject(OturumServisi, { optional: true });
  private readonly t = ceviriFonksiyonu();
  private readonly scope: () => string;
  private readonly restorable: boolean;
  private readonly newKey: () => string;
  private key: string | null = null;
  private keyTarget: string | null = null;
  private lockedForm: AbstractControl | null = null;
  /** Bu çekirdeğin son kullandığı form (bağlam değişince temizlenir). */
  private lastForm: AbstractControl | null = null;
  /** Gönderim onayı açık (gövde kuruldu, istek henüz gitmedi) — cari değişimi gibi geçişler beklemeli. */
  private readonly confirming = signal(false);
  private destroyed = false;

  private readonly sendingState = signal(false);
  private readonly frozenState = signal<MoneyAttempt<TBody> | null>(null);
  private readonly errorsState = signal<readonly string[]>([]);
  private readonly noticeState = signal<MoneyNotice | null>(null);
  private readonly amountRequiredState = signal(false);

  /** İstek uçuşta. */
  readonly sending: Signal<boolean> = this.sendingState.asReadonly();
  /** Donmuş deneme — varken form kilitli, düğme "aynı işlemi tekrar gönder". */
  readonly frozen: Signal<MoneyAttempt<TBody> | null> = this.frozenState.asReadonly();
  /** Alana bağlanamayan sunucu mesajları + alansız hata detayı. */
  readonly errors: Signal<readonly string[]> = this.errorsState.asReadonly();
  /** Kalıcı form notu (sonuç bilinmiyor / önceki deneme kayıtlı / çağıranın notu). */
  readonly notice: Signal<MoneyNotice | null> = this.noticeState.asReadonly();
  /** `amountControl` kancası: tutar açıkça girilene kadar gönderim kilitli. */
  readonly amountRequired: Signal<boolean> = this.amountRequiredState.asReadonly();

  constructor(private readonly config: MoneySubmissionConfig = {}) {
    const own = `money:${++instanceCounter}`;
    this.scope = config.scope ?? (() => own);
    this.restorable = config.scope !== undefined;
    this.newKey = config.newKey ?? yeniIslemAnahtari;
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      // Geri getirilemeyen kapsam: kesinleşmemiş deneme kayıtta asılı kalmasın (uçuştaysa yanıt temizler).
      if (!this.restorable && !this.sendingState()) this.attempts.delete(this.scope());
    });
    // r316 M1: oturum bağlamı değişince (çıkış, yeniden girişte başka kullanıcı/şube) önceki kullanıcının donmuş
    // denemesi, notu ve form içeriği bu bileşende de kalmaz; anahtar bırakılır.
    let last = sessionContext(this.session);
    effect(() => {
      const current = sessionContext(this.session);
      untracked(() => {
        if (current !== last) this.contextChanged();
        last = current;
      });
    });
  }

  /** Sonucu bilinmeyen (donmuş) ya da uçan işlem — sayfa terk koruması ve form-yok-eden düğmeler bunu sorar. */
  pending(): boolean {
    return this.frozenState() !== null || this.sendingState() || this.confirming();
  }

  /** Bir sonraki gönderimin anahtarı (henüz yoksa `null`). */
  get currentKey(): string | null {
    return this.frozenState()?.key ?? this.key;
  }

  async run<TResult>(o: MoneyRunOptions<TBody, TResult>): Promise<void> {
    // Onay penceresi açıkken ikinci tık yok sayılır: yoksa ilk işlem bitip anahtar yenilendikten sonra ikinci onay
    // YENİ anahtarla ikinci kaydı yazardı.
    if (this.sendingState() || this.confirming()) return;
    this.errorsState.set([]);
    this.lastForm = o.form;
    const frozen = this.frozenState();
    if (frozen) {
      this.send(frozen, o, true);
      return;
    }
    sunucuHatalariniTemizle(o.form);
    o.form.markAllAsTouched();
    if (o.form.invalid) {
      o.invalid?.();
      return;
    }
    if (this.amountMissing()) return;
    const next = o.build();
    if (next === null) return;
    if (o.confirm) {
      this.confirming.set(true);
      try {
        if (!(await o.confirm())) return;
      } finally {
        this.confirming.set(false);
      }
      if (this.sendingState() || this.frozenState()) return;
    }
    const target = next.target ?? this.scope();
    if (this.keyTarget !== target) {
      this.key = null;
      this.keyTarget = target;
    }
    this.key ??= this.newKey();
    this.send(
      {
        target,
        path: next.path,
        method: next.method ?? 'post',
        key: this.key,
        body: next.body,
        content: next.content ?? null,
        formValue: o.form.getRawValue(),
        inFlight: true,
        notice: UNCERTAIN_NOTICE,
        context: sessionContext(this.session),
      },
      o,
      false,
    );
  }

  /**
   * Bileşen geri geldi: bu kapsamın kesinleşmemiş denemesi varsa form aynı içerikle KİLİTLİ açılır (`true`). Uçuştaki
   * istek başka örnekteyse sonucu burada bilinmez: "sonucu bilinmiyor" sayılır; tekrar aynı anahtarla.
   */
  restore(form: AbstractControl, write?: (value: unknown) => void): boolean {
    const a = this.attempts.get(this.scope()) as MoneyAttempt<TBody> | undefined;
    if (!a) return false;
    if (write) write(a.formValue);
    else form.reset(a.formValue, { emitEvent: false });
    this.lock(form);
    this.freeze({ ...a, inFlight: false }, a.inFlight ? UNCERTAIN_NOTICE : a.notice);
    return true;
  }

  /** Donmuş denemeden BİLİNÇLİ vazgeçiş (onaylı): kopya ve anahtar bırakılır, form açılır (`true`). */
  async abandon(): Promise<boolean> {
    if (this.sendingState() || this.frozenState() === null) return false;
    const yes = await this.confirmService.sor({
      baslik: this.t('paraIslemi.vazgecBaslik'),
      mesaj: this.t('paraIslemi.vazgecMesaj'),
      tehlikeli: true,
    });
    if (!yes || this.sendingState() || this.frozenState() === null) return false;
    this.attempts.delete(this.scope());
    this.frozenState.set(null);
    this.noticeState.set(null);
    this.resetKey();
    this.unlock();
    return true;
  }

  /** Form yeni bir kayda sıfırlandı: sonraki gönderim yeni anahtarla. Donmuş deneme varken etkisiz. */
  renew(): void {
    if (this.frozenState() === null && !this.sendingState()) this.resetKey();
  }

  /** Çağıranın bağlam notu (ör. satışta "araç zaten satılmış" = önceki deneme yazılmış olabilir). */
  showNotice(notice: MoneyNotice | null): void {
    this.noticeState.set(notice);
  }

  private send<TResult>(
    attempt: MoneyAttempt<TBody>,
    o: MoneyRunOptions<TBody, TResult>,
    retry: boolean,
  ): void {
    const scope = this.scope();
    // Gönderimden ÖNCE kayda: bileşen yok edilse de deneme kaybolmaz.
    this.attempts.set(scope, { ...attempt, inFlight: true, notice: UNCERTAIN_NOTICE });
    this.noticeState.set(null);
    this.sendingState.set(true);
    this.lock(o.form);
    const options = {
      islemAnahtari: attempt.key,
      context: istekBaglami({ mukerrerCagiranGosterir: true }),
    };
    const request =
      attempt.method === 'put'
        ? this.api.put<TResult>(attempt.path, attempt.body, options)
        : this.api.post<TResult>(attempt.path, attempt.body, options);
    // Bileşen yok edilse de istek İPTAL EDİLMEZ (takeUntilDestroyed yok): sonuç kaydı günceller.
    request.subscribe({
      next: (result) => this.succeeded(scope, attempt, result, o),
      error: (raw: unknown) => this.failed(scope, attempt, apiHatasinaCevir(raw), o, retry),
    });
  }

  private succeeded<TResult>(
    scope: string,
    attempt: MoneyAttempt<TBody>,
    result: TResult,
    o: MoneyRunOptions<TBody, TResult>,
  ): void {
    this.attempts.delete(scope, attempt.context);
    if (this.destroyed || this.staleContext(attempt)) return;
    this.sendingState.set(false);
    this.frozenState.set(null);
    this.amountRequiredState.set(false);
    this.resetKey();
    this.unlock();
    o.form.markAsPristine();
    o.success(result, attempt);
    o.settled?.('done');
  }

  private failed<TResult>(
    scope: string,
    attempt: MoneyAttempt<TBody>,
    error: ApiHatasi,
    o: MoneyRunOptions<TBody, TResult>,
    retry: boolean,
  ): void {
    const uncertain = sonucuBilinmeyenHata(error);
    const duplicate = error.kod === 'mukerrer' ? classifyDuplicate(error) : null;
    const notice = duplicate
      ? duplicateNotice(error, attempt.content, this.config.recordedMessage)
      : UNCERTAIN_NOTICE;
    // Kayıt, bileşen yok edilmiş olsa da güncellenir: sonucu hâlâ kesin olmayan deneme (belirsiz ya da `mevcut`suz
    // 409) kalır, gerisi (kesin sonuç) düşer.
    // Bağlam değiştiyse `set` yazmaz (yanıt çıkıştan sonra geldi).
    if ((uncertain || duplicate === 'recordedEarlier') && (this.restorable || !this.destroyed))
      this.attempts.set(scope, { ...attempt, inFlight: false, notice });
    else this.attempts.delete(scope, attempt.context);
    if (this.destroyed || this.staleContext(attempt)) return;
    this.sendingState.set(false);
    if (uncertain) {
      this.freeze(attempt, UNCERTAIN_NOTICE);
      return; // interceptor hata toast'u + formda kalıcı "sonucu bilinmiyor" notu
    }
    if (duplicate) {
      if (this.config.amountControl) this.amountRequiredState.set(true);
      if (duplicate === 'recordedEarlier') {
        // Form donmuş kalır, not formda (panel gizlense de deneme kayıtta; geri gelince görünür).
        this.freeze(attempt, notice);
      } else {
        const toast = this.config.duplicateDisplay === 'toast';
        this.frozenState.set(null);
        this.resetKey();
        this.unlock();
        this.noticeState.set(toast ? null : notice);
        if (toast) this.announce(notice);
        o.afterDuplicate?.(duplicate);
      }
      o.settled?.('duplicate');
      return;
    }
    // Kesin sonuç: yazılmadı. Önce kilit açılır (açmak sunucu hatalarını silerdi), sonra hatalar yazılır.
    this.frozenState.set(null);
    this.unlock();
    if (error.kod === 'cakisma') {
      this.resetKey();
      o.conflict?.();
      o.settled?.('conflict'); // alansız cakisma bandı interceptor'da; form SİLİNMEZ
      return;
    }
    const unmatched = sunucuHatalariniUygula(o.form, error.alanlar, o.fieldMap?.());
    if (error.alanlar === undefined && !genelGosterilir(error)) this.errorsState.set([error.detay]);
    else if (unmatched.length > 0) this.errorsState.set(unmatched);
    if (error.alanlar !== undefined) o.invalid?.();
    o.rejected?.(error, retry);
  }

  private announce(n: MoneyNotice): void {
    const text = [this.t(n.message, n.params), n.detail].filter(Boolean).join(' ');
    const options = n.title ? { baslik: this.t(n.title) } : undefined;
    if (n.tone === 'bilgi') this.toast.bilgi(text, options);
    else this.toast.uyari(text, options);
  }

  /** Donmuş deneme: anahtar ona bağlanır, gövde forma geri yazılır (ekranda gönderilenin aynısı), form kilitli. */
  private freeze(attempt: MoneyAttempt<TBody>, notice: MoneyNotice): void {
    this.key = attempt.key;
    this.keyTarget = attempt.target;
    this.frozenState.set({ ...attempt, inFlight: false });
    this.noticeState.set(notice);
    const form = this.lockedForm;
    if (form && attempt.formValue !== null && typeof attempt.formValue === 'object')
      form.patchValue(attempt.formValue, { emitEvent: false });
  }

  /** Yanıt, gönderildiği oturum bağlamı artık geçerli değilken geldi: bileşene dokunulmaz (yalnız uçuş biter). */
  private staleContext(attempt: MoneyAttempt<TBody>): boolean {
    if (attempt.context === sessionContext(this.session)) return false;
    this.sendingState.set(false);
    return true;
  }

  private contextChanged(): void {
    const form = this.lockedForm ?? this.lastForm;
    this.frozenState.set(null);
    this.noticeState.set(null);
    this.errorsState.set([]);
    this.amountRequiredState.set(false);
    this.resetKey();
    this.unlock();
    form?.reset(undefined, { emitEvent: false });
  }

  private amountMissing(): boolean {
    const control = this.config.amountControl?.();
    if (!control || !this.amountRequiredState()) return false;
    const v: unknown = control.value;
    if (v !== null && v !== undefined && String(v).trim() !== '') return false;
    control.markAsTouched();
    control.setErrors({ ...(control.errors ?? {}), required: true });
    return true;
  }

  private resetKey(): void {
    this.key = null;
    this.keyTarget = null;
  }

  private lock(form: AbstractControl): void {
    this.lastForm = form;
    if (this.lockedForm !== form) this.unlock();
    this.lockedForm = form;
    if (!form.disabled) form.disable({ emitEvent: false });
  }

  private unlock(): void {
    const form = this.lockedForm;
    this.lockedForm = null;
    if (form?.disabled) form.enable({ emitEvent: false });
  }
}

/** Enjeksiyon bağlamında para gönderimi (`protected readonly x = moneySubmission<Govde>({ scope })`). */
export function moneySubmission<TBody = unknown>(
  config?: MoneySubmissionConfig,
): MoneySubmission<TBody> {
  return new MoneySubmission<TBody>(config);
}

/**
 * Onay kapısı: pencere açıkken ikinci tık yok sayılır (`false`) — anahtarsız (yapısal) işlemlerde çift tık iki pencere
 * ve iki POST üretiyordu.
 */
export class ConfirmGate {
  private open = false;

  get isOpen(): boolean {
    return this.open;
  }

  async ask(question: () => Promise<boolean>): Promise<boolean> {
    if (this.open) return false;
    this.open = true;
    try {
      return await question();
    } finally {
      this.open = false;
    }
  }
}

/**
 * `cakisma` birleştirmesi: güncel kaydın değeri yalnız DOKUNULMAMIŞ alanlara yazılır; kullanıcının yazdığı korunur.
 * Yalnız formda karşılığı olan anahtarlar.
 */
export function mergeUntouched(form: FormGroup, fresh: Readonly<Record<string, unknown>>): void {
  for (const [name, value] of Object.entries(fresh)) {
    const control = form.get(name);
    if (control && control.pristine) control.setValue(value, { emitEvent: false });
  }
}

/** Bir ya da daha çok gönderimden biri uçuşta/donmuş mu (form-yok-eden düğmelerin pasifliği için). */
export function anyPending(...submissions: readonly { pending(): boolean }[]): boolean {
  return submissions.some((s) => s.pending());
}
