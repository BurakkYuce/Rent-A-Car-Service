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

import type { FinansHesapOgesi } from '@core/api/ui-tipleri';
import { paraBicimle, tarihBicimle } from '@core/bicim/bicim';
import { moneySubmission } from '@core/form/money-submission';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import type {
  AccountKind,
  InstallmentPayRequest,
  InstallmentPayResponse,
  LoanDetail,
} from '../finance-model';
import { LOANS, recordPath } from '../finance.store';

/**
 * "Taksit Öde" (Blazor `/arac-kredi/taksit-ode`): sonraki taksit sunucunun planından (sıra, vade, tutar — kullanıcı
 * tutar YAZMAZ), hesap türü Kasa/Banka + isteğe bağlı spesifik hesap. Gerçek gider + dengeli defter yazar.
 * Para yaşam döngüsü çekirdek `MoneySubmission`'da (işlem başına anahtar, uçuşta kilit, belirsizde donmuş gövde);
 * anahtar KREDİYE bağlı (başka kredide yeni anahtar). Sıra sunucunun "sonraki taksit"idir; arada başka ödeme olduysa
 * sunucu 409 `cakisma` verir.
 */
@Component({
  selector: 'rc-installment-payment-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, MoneySubmitBar, Secim],
  templateUrl: './installment-payment-panel.html',
})
export class InstallmentPaymentPanel implements OnInit {
  readonly loan = input.required<LoanDetail>();
  /** Kasa/banka hesapları (sayfa yükler; hata sessiz → seçici görünmez). */
  readonly accounts = input<readonly FinansHesapOgesi[]>([]);
  /** Kayıt yenileniyor: düğme pasif (bayat sıra/anahtarla gönderim olmasın). */
  readonly refreshing = input(false);
  /** 2xx ya da kesin 409: kredi yeniden okunmalı. */
  readonly settled = output<void>();

  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
  });
  /** Tüm taksitler ödenince panel gizlenir: `mukerrer` bildirimi toast'ta. */
  protected readonly payment = moneySubmission<InstallmentPayRequest>({
    scope: () => `kredi-taksit:${this.loan().id}`,
    duplicateDisplay: 'toast',
  });

  protected readonly kindOptions: readonly SecenekOgesi<AccountKind>[] = [
    { deger: 'Kasa', etiket: this.t('aracFinans.kredi.kasa') },
    { deger: 'Banka', etiket: this.t('aracFinans.kredi.banka') },
  ];
  private readonly kind = toSignal(this.form.controls.hesap.valueChanges, {
    initialValue: this.form.controls.hesap.value,
  });
  protected readonly accountOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.accounts()
      .filter((h) => h.tur !== null && h.tur === this.kind())
      .map((h) => ({ deger: h.id, etiket: h.etiket })),
  );

  protected readonly next = computed(() => this.loan().sonrakiTaksit);
  protected readonly nextText = computed(() => {
    const n = this.next();
    if (!n) return '';
    return this.t('aracFinans.kredi.sonrakiTaksit', {
      sira: n.sira,
      toplam: this.loan().taksitSayisi,
      vade: tarihBicimle(n.vade),
      tutar: paraBicimle(toNumber(n.tutar), this.loan().doviz),
    });
  });
  protected readonly retryLabel = this.t('aracFinans.kredi.tekrarDene');

  constructor() {
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.accountOptions().some((h) => h.deger === id))
        this.form.controls.hesapId.setValue(null);
    });
  }

  ngOnInit(): void {
    this.payment.restore(this.form);
  }

  /** Sonucu bilinmeyen (donmuş) ya da uçuştaki ödeme var mı — sayfa terk koruması bunu sorar (inceleme L1). */
  hasPendingPayment(): boolean {
    return this.payment.pending();
  }

  protected pay(): void {
    if (this.refreshing()) return;
    const loan = this.loan();
    void this.payment.run<InstallmentPayResponse>({
      form: this.form,
      build: () => {
        const next = loan.sonrakiTaksit;
        if (next === null || !loan.yetkiler.taksitOde) return null;
        const v = this.form.getRawValue();
        return {
          path: recordPath(LOANS, loan.id, '/taksit-ode'),
          target: `kredi-taksit:${loan.id}`,
          body: { sira: next.sira, hesap: v.hesap ?? 'Kasa', hesapId: v.hesapId },
          content: { tutar: next.tutar, doviz: loan.doviz },
        };
      },
      success: (r) =>
        this.toast.basari(
          this.t('aracFinans.kredi.odendi', {
            sira: r.sira,
            no: r.giderNo,
            tutar: paraBicimle(toNumber(r.tutar), r.doviz),
          }),
        ),
      settled: () => this.settled.emit(),
    });
  }
}
