import { DestroyRef, type Signal, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { AbstractControl } from '@angular/forms';
import { finalize } from 'rxjs';

import { apiHatasinaCevir, type ApiHatasi } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import { paraBicimle } from '@core/bicim/bicim';
import { sunucuHatalariniTemizle, sunucuHatalariniUygula } from '@core/form/sunucu-hatalari';
import { TahsilatDenemeKaydi } from '@core/form/tahsilat-denemesi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';

import type { AttemptContent } from './finance-model';
import { type FinanceDuplicateType, type FrozenOperation, MoneyOperation } from './money-operation';

type Translate = ReturnType<typeof ceviriFonksiyonu>;

function amount(v: number | string | null | undefined): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
}

/**
 * `mukerrer` bildirimi (başlık + metin + ton). r299 HIGH-1: HİÇBİR sınıf "yazılmadı / değişti / yeniden girin" demez
 * ve kullanıcıyı ikinci işleme yönlendirmez — bu anahtarla bir kayıt VAR (önceki denemesi yazılmıştır).
 */
export function duplicateNotice(
  type: FinanceDuplicateType,
  error: ApiHatasi,
  submitted: AttemptContent,
  t: Translate,
): { readonly tone: 'bilgi' | 'uyari'; readonly title: string; readonly message: string } {
  switch (type) {
    case 'zatenKaydedildi':
      return {
        tone: 'bilgi',
        title: t('finans.islem.zatenKaydedildi'),
        message: `${error.detay} ${t('finans.islem.hareketleriKontrol')}`,
      };
    case 'oncekiDenemeKaydedilmis':
      return {
        tone: 'uyari',
        title: t('finans.islem.oncekiDenemeBaslik'),
        message: t('finans.islem.oncekiDenemeKaydedildi', {
          no: error.mevcut?.belgeNo ?? '',
          kayitli: paraBicimle(amount(error.mevcut?.tutar), error.mevcut?.doviz),
          girilen: paraBicimle(amount(submitted.tutar), submitted.doviz),
        }),
      };
    default:
      return {
        tone: 'bilgi',
        title: t('finans.islem.dahaOnceKaydedildiBaslik'),
        message: t('finans.islem.dahaOnceKaydedildi'),
      };
  }
}

/** Şablonun okuduğu durum (gönder düğmesi bileşeni gövde tipinden bağımsız). */
export interface MoneyActionState {
  readonly sending: Signal<boolean>;
  readonly frozen: Signal<object | null>;
  readonly errors: Signal<readonly string[]>;
}

export interface MoneyRunOptions<TBody, TResult> {
  /** Hataların yazılacağı ve donmuş kopya varken kilitlenecek form. */
  readonly form: AbstractControl;
  /** Yeni gövde (donmuş kopya YOKSA çağrılır). `null` → gönderim yapılmaz. */
  readonly build: () => {
    readonly path: ApiYolu;
    readonly body: TBody;
    readonly content: AttemptContent;
  } | null;
  /** Gönderimden önce onay (yalnız yeni işlemde; donmuş kopyanın tekrarı aynı işlemdir, yeniden sorulmaz). */
  readonly confirm?: () => Promise<boolean>;
  /**
   * Sunucu alan adı → form yolu. Hata ANINDA çağrılır: toplu işlemler satırları gönderilen (donmuş) kopyadaki satır
   * kimliklerinden eşler (r299 MEDIUM-1), ekrandaki sıradan değil.
   */
  readonly fieldMap?: () => Readonly<Record<string, string>>;
  readonly success: (result: TResult, copy: FrozenOperation<TBody>) => void;
  /** 2xx ya da kesin 409 sonrası: ekran verisi yenilenir. */
  readonly settled: () => void;
  /** 409 `mukerrer` sonrası form (tutar) temizliği — yazılan işlem ikinci kez gönderilmesin. */
  readonly afterDuplicate?: () => void;
}

/**
 * Bileşen tarafı para gönderimi ({@link MoneyOperation} + HTTP + mesajlar + form kilidi). Enjeksiyon bağlamında
 * oluşturulur (`protected readonly transfer = moneyAction()`). Çift tık tek istek; `sending`/`frozen` şablonda.
 */
export class MoneyAction<TBody = unknown> implements MoneyActionState {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirmService = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();
  private readonly operation = new MoneyOperation<TBody>(inject(TahsilatDenemeKaydi));
  private lockedForm: AbstractControl | null = null;
  private confirming = false;

  private readonly _sending = signal(false);
  private readonly _frozen = signal<FrozenOperation<TBody> | null>(null);
  private readonly _errors = signal<readonly string[]>([]);
  readonly sending: Signal<boolean> = this._sending.asReadonly();
  readonly frozen: Signal<FrozenOperation<TBody> | null> = this._frozen.asReadonly();
  readonly errors: Signal<readonly string[]> = this._errors.asReadonly();

  /** Sonucu bilinmeyen (donmuş) ya da uçan işlem — sayfa terk koruması bunu sorar. */
  pending(): boolean {
    return this._frozen() !== null || this._sending();
  }

  async run<TResult>(o: MoneyRunOptions<TBody, TResult>): Promise<void> {
    // Onay penceresi açıkken ikinci tık yok sayılır: yoksa ilk işlem bitip anahtar yenilendikten sonra ikinci onay
    // YENİ anahtarla ikinci kaydı yazardı (çift tık → iki onay → iki işlem).
    if (this._sending() || this.confirming) return;
    this._errors.set([]);
    let copy = this.operation.frozen;
    if (copy === null) {
      sunucuHatalariniTemizle(o.form);
      o.form.markAllAsTouched();
      if (o.form.invalid) return;
      const next = o.build();
      if (next === null) return;
      if (o.confirm) {
        this.confirming = true;
        try {
          if (!(await o.confirm())) return;
        } finally {
          this.confirming = false;
        }
      }
      if (this._sending() || this.operation.frozen) return;
      copy = this.operation.prepare(next.path, next.body, next.content);
    }
    this.send(copy, o);
  }

  /** Donmuş denemeden bilinçli vazgeçiş (onaylı): yeni deneme yeni anahtarla gider. */
  async abandon(settled: () => void): Promise<void> {
    const yes = await this.confirmService.sor({
      baslik: this.t('finans.islem.vazgecBaslik'),
      mesaj: this.t('finans.islem.vazgecMesaj'),
    });
    if (!yes) return;
    this.operation.abandon();
    this.setFrozen(null);
    settled();
  }

  private send<TResult>(copy: FrozenOperation<TBody>, o: MoneyRunOptions<TBody, TResult>): void {
    this._sending.set(true);
    // r299 MEDIUM-1: istek uçarken de form kilitli — ekranda görünen ile gönderilen ayrışmasın.
    this.lock(o.form);
    this.operation.started(copy);
    this.api
      .post<TResult>(copy.path, copy.body, {
        islemAnahtari: copy.key,
        context: istekBaglami({ mukerrerCagiranGosterir: true }),
      })
      .pipe(
        finalize(() => {
          this._sending.set(false);
          if (this._frozen() === null) this.unlock();
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => {
          this.operation.succeeded();
          this.setFrozen(null);
          o.form.markAsPristine();
          o.success(result, copy);
          o.settled();
        },
        error: (raw: unknown) => this.failed(copy, apiHatasinaCevir(raw), o),
      });
  }

  private failed<TResult>(
    copy: FrozenOperation<TBody>,
    error: ApiHatasi,
    o: MoneyRunOptions<TBody, TResult>,
  ): void {
    const outcome = this.operation.failed(copy, error);
    this.setFrozen(this.operation.frozen);
    switch (outcome.kind) {
      case 'uncertain':
        return; // interceptor hata toast'u + sayfada kalıcı "sonucu bilinmiyor" bandı
      case 'duplicate': {
        const n = duplicateNotice(outcome.type, error, copy.content, this.t);
        if (n.tone === 'bilgi') this.toast.bilgi(n.message, { baslik: n.title });
        else this.toast.uyari(n.message, { baslik: n.title });
        o.afterDuplicate?.();
        o.settled();
        return;
      }
      case 'stale':
        o.settled(); // alansız cakisma bandı interceptor'da
        return;
      default: {
        const unmatched = sunucuHatalariniUygula(o.form, error.alanlar, o.fieldMap?.());
        if (error.alanlar === undefined && !genelGosterilir(error)) this._errors.set([error.detay]);
        else if (unmatched.length > 0) this._errors.set(unmatched);
      }
    }
  }

  /**
   * Donmuş kopya varken form kilitli (yeniden deneme formdan değil kopyadan gider); kopya çözülünce kilit, istek
   * uçmuyorsa açılır. Hatalar kilit açıldıktan SONRA yazılır (devre dışı kontrol hata göstermez).
   */
  private setFrozen(copy: FrozenOperation<TBody> | null): void {
    this._frozen.set(copy);
    if (copy === null) this.unlock();
  }

  private lock(form: AbstractControl): void {
    this.lockedForm = form;
    form.disable({ emitEvent: false });
  }

  private unlock(): void {
    this.lockedForm?.enable({ emitEvent: false });
    this.lockedForm = null;
  }
}

/** Enjeksiyon bağlamında para gönderimi (`protected readonly x = moneyAction<Govde>()`). */
export function moneyAction<TBody = unknown>(): MoneyAction<TBody> {
  return new MoneyAction<TBody>();
}
