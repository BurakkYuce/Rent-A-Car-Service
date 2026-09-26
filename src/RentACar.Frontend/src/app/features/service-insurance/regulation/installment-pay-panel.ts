import {
  ChangeDetectionStrategy,
  Component,
  type OnInit,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { FinanceAccountItem } from '@core/api/ui-tipleri';
import { formatMoney } from '@core/bicim/bicim';
import { moneySubmission } from '@core/form/money-submission';
import type { DayText } from '@core/form/tarih-girdisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';
import { DatePicker } from '@shared/form/tarih/date-picker';

import {
  type AccountKind,
  type InstallmentPaymentRequest,
  type InstallmentPaymentResult,
  REGULATION,
  num,
  recordPath,
} from '../service-insurance-model';

export type InstallmentKind = 'mtv' | 'muayene';

/**
 * MTV / muayene kısmi ödemesi (Blazor `/regulasyon-odeme/mtv|muayene`) — PARA: gider + dengeli defter (Borç
 * Gider[araç] / Alacak Kasa-Banka). Tutar boş → kalanın (muayenede kalan + ceza) tamamı; tutar ve ceza
 * `rc-para-girdisi` ile. `beklenenKalan` = ekranın gördüğü kalan: başka sekme/kullanıcı arada ödediyse sunucu 409
 * `cakisma` döner, form SİLİNMEZ, kayıt yenilenir. Anahtar/donmuş kopya/uçuş kilidi çekirdek `MoneySubmission`'da;
 * `mukerrer` sonrası "açık tutar" kilidi çekirdeğin `amountControl` kancası (inceleme M4 + L-new).
 */
@Component({
  selector: 'rc-installment-pay-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    TextInput,
    MoneySubmitBar,
    MoneyInput,
    Selection,
    DatePicker,
  ],
  templateUrl: './installment-pay-panel.html',
})
export class InstallmentPayPanel implements OnInit {
  readonly kind = input.required<InstallmentKind>();
  readonly recordId = input.required<string>();
  /** Sunucunun kalanı (ekranın gördüğü). */
  readonly remaining = input.required<number>();
  readonly accounts = input<readonly FinanceAccountItem[]>([]);
  /** Kayıt yenileniyor: düğme pasif (bayat kalanla gönderim olmasın). */
  readonly refreshing = input(false);
  /** 2xx ya da kesin 409: kayıt yeniden okunmalı. */
  readonly settled = output<void>();

  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
    tutar: new FormControl<string | null>(null),
    ceza: new FormControl<string | null>(null),
    odemeTarihi: new FormControl<DayText | null>(null),
    evrakNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    islemYapan: new FormControl<string | null>(null, Validators.maxLength(128)),
    kasaKodu: new FormControl<string | null>(null, Validators.maxLength(64)),
    hesapNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  /**
   * `mukerrer` sonrası boş tutar "kalanın tamamını öde" demek — yeni ödeme ancak açıkça girilen tutarla; kilit yalnız
   * BAŞARILI ödemeyle kalkar (yazıp silmek açmaz).
   */
  protected readonly payment = moneySubmission<InstallmentPaymentRequest>({
    scope: () => `${this.kind()}:${this.recordId()}`,
    amountControl: () => this.form.controls.tutar,
    // Tamamen ödenen kayıtta panel gizlenir: bildirim toast'ta (not panelle kaybolurdu).
    duplicateDisplay: 'toast',
  });

  protected readonly kindOptions: readonly SecenekOgesi<AccountKind>[] = [
    { deger: 'Kasa', etiket: this.t('servisSigorta.para.kasa') },
    { deger: 'Banka', etiket: this.t('servisSigorta.para.banka') },
  ];
  private readonly accountKind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.accounts()
      .filter((h) => h.tur !== null && h.tur === this.accountKind())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );
  protected readonly remainingText = computed(() => formatMoney(this.remaining(), 'TRY'));

  constructor() {
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.accountOptions().some((h) => h.deger === id))
        this.form.controls.hesapId.setValue(null);
    });
  }

  ngOnInit(): void {
    // Panel yeniden kurulunca sonucu bilinmeyen ödeme aynı gövde + anahtarla KİLİTLİ geri gelir.
    this.payment.restore(this.form);
  }

  /** Sonucu bilinmeyen (donmuş) ödeme ya da doldurulmuş form — sayfa terk koruması sorar. */
  hasPendingWork(): boolean {
    return this.hasPendingPayment() || this.form.dirty;
  }

  /** Sonucu bilinmeyen ya da uçuştaki ödeme (sayfa terkinde özel uyarı). */
  hasPendingPayment(): boolean {
    return this.payment.pending();
  }

  protected pay(): void {
    if (this.refreshing()) return;
    void this.payment.run<InstallmentPaymentResult>({
      form: this.form,
      build: () => {
        const v = this.form.getRawValue();
        return {
          path: recordPath(
            this.kind() === 'mtv' ? `${REGULATION}/mtv` : `${REGULATION}/muayeneler`,
            this.recordId(),
            '/odeme',
          ),
          target: `${this.kind()}:${this.recordId()}`,
          body: {
            hesap: v.hesap ?? 'Kasa',
            hesapId: v.hesapId,
            tutar: v.tutar,
            ceza: this.kind() === 'muayene' ? v.ceza : null,
            odemeTarihi: momentValue(v.odemeTarihi, null),
            beklenenKalan: this.remaining(),
            evrakNo: textValue(v.evrakNo),
            islemYapan: textValue(v.islemYapan),
            kasaKodu: textValue(v.kasaKodu),
            hesapNo: textValue(v.hesapNo),
            aciklama: textValue(v.aciklama),
          },
          content: { tutar: v.tutar ?? this.remaining(), doviz: 'TRY' },
        };
      },
      success: (r) => {
        this.toast.basari(
          this.t('servisSigorta.odeme.odendi', {
            sira: r.sira,
            tutar: formatMoney(num(r.tutar), 'TRY'),
            kalan: formatMoney(num(r.kalan), 'TRY'),
          }),
        );
        this.resetKeepingAccount();
      },
      // Başarı yolundaki gibi TAM sıfırlama (ceza dahil; hesap korunur) (inceleme M4): aksi halde boş tutar "kalanın
      // tamamı" olarak planlanmamış ikinci ödemeyi ve cezayı ikinci kez yazardı.
      afterDuplicate: () => this.resetKeepingAccount(),
      settled: () => this.settled.emit(),
    });
  }

  protected abandoned(): void {
    this.settled.emit();
  }

  private resetKeepingAccount(): void {
    const { hesap: account, hesapId: accountId } = this.form.getRawValue();
    this.form.reset({ hesap: account ?? 'Kasa', hesapId: accountId });
  }
}
