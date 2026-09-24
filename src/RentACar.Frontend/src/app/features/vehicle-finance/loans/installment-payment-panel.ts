import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { FinansHesapOgesi } from '@core/api/ui-tipleri';
import { paraBicimle, tarihBicimle } from '@core/bicim/bicim';
import { TahsilatDenemeKaydi, tahsilatMukerrerBildir } from '@core/form/tahsilat-denemesi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { genelGosterilir } from '@core/oturum/oturum-interceptor';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { FormHatalari } from '@shared/form/form-hatalari';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';

import type { AccountKind, InstallmentPayResponse, LoanDetail } from '../finance-model';
import { LOANS, recordPath } from '../finance.store';
import { type FrozenPayment, InstallmentPayment } from './installment-payment';

/**
 * "Taksit Öde" (Blazor `/arac-kredi/taksit-ode`): sonraki taksit sunucunun planından (sıra, vade, tutar — kullanıcı
 * tutar YAZMAZ), hesap türü Kasa/Banka + isteğe bağlı spesifik hesap. Gerçek gider + dengeli defter yazar.
 * Para yaşam döngüsü {@link InstallmentPayment}'ta; bu bileşen yalnız formu, mesajları ve kilidi taşır.
 */
@Component({
  selector: 'rc-installment-payment-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormHatalari, Ikon, Secim],
  templateUrl: './installment-payment-panel.html',
})
export class InstallmentPaymentPanel {
  readonly loan = input.required<LoanDetail>();
  /** Kasa/banka hesapları (sayfa yükler; hata sessiz → seçici görünmez). */
  readonly accounts = input<readonly FinansHesapOgesi[]>([]);
  /** Kayıt yenileniyor: düğme pasif (bayat sıra/anahtarla gönderim olmasın). */
  readonly refreshing = input(false);
  /** 2xx ya da kesin 409: kredi yeniden okunmalı. */
  readonly settled = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly confirm = inject(OnayServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  private readonly payment = new InstallmentPayment(inject(TahsilatDenemeKaydi));
  protected readonly sending = signal(false);
  protected readonly frozen = signal<FrozenPayment | null>(null);
  protected readonly errors = signal<readonly string[]>([]);

  protected readonly form = new FormGroup({
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    hesapId: new FormControl<string | null>(null),
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

  constructor() {
    this.form.controls.hesap.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      const id = this.form.controls.hesapId.value;
      if (id !== null && !this.accountOptions().some((h) => h.deger === id))
        this.form.controls.hesapId.setValue(null);
    });
    // Donmuş kopya varken form kilitli: yeniden deneme formdan değil kopyadan gider.
    effect(() => {
      const locked = this.frozen() !== null;
      untracked(() => (locked ? this.form.disable() : this.form.enable()));
    });
  }

  protected pay(): void {
    if (this.sending() || this.refreshing()) return;
    this.errors.set([]);
    this.form.markAllAsTouched();
    if (this.frozen() === null && this.form.invalid) return;
    const v = this.form.getRawValue();
    const copy = this.payment.prepare(this.loan(), {
      hesap: v.hesap ?? 'Kasa',
      hesapId: v.hesapId,
    });
    if (copy === null) return;
    this.sending.set(true);
    this.payment.started(copy);
    this.api
      .post<InstallmentPayResponse>(recordPath(LOANS, copy.loanId, '/taksit-ode'), copy.body, {
        islemAnahtari: copy.key,
        context: istekBaglami({ mukerrerCagiranGosterir: true }),
      })
      .pipe(
        finalize(() => this.sending.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.payment.succeeded();
          this.frozen.set(null);
          this.toast.basari(
            this.t('aracFinans.kredi.odendi', {
              sira: r.sira,
              no: r.giderNo,
              tutar: paraBicimle(toNumber(r.tutar), r.doviz),
            }),
          );
          this.settled.emit();
        },
        error: (raw: unknown) => this.failed(copy, raw),
      });
  }

  /** Donmuş denemeden bilinçli vazgeçiş (onaylı): yeni deneme yeni anahtarla, sıra kontrolü sunucuda. */
  protected async abandon(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('aracFinans.kredi.vazgecBaslik'),
      mesaj: this.t('aracFinans.kredi.vazgecMesaj'),
    });
    if (!yes) return;
    this.payment.abandon();
    this.frozen.set(null);
    this.settled.emit();
  }

  private failed(copy: FrozenPayment, raw: unknown): void {
    const error = apiHatasinaCevir(raw);
    const outcome = this.payment.failed(copy, error);
    this.frozen.set(this.payment.frozen);
    switch (outcome.kind) {
      case 'uncertain':
        return; // interceptor hata toast'u + sayfada kalıcı "sonucu bilinmiyor" bandı
      case 'duplicate': {
        const g = this.payment.lastSubmission;
        if (g)
          tahsilatMukerrerBildir(this.toast, this.t, outcome.type, error, {
            gonderim: g,
            bayatBaslik: this.t('aracFinans.kredi.krediDegismis'),
            ek: this.t('geriBildirim.mukerrerYenilendi'),
          });
        this.settled.emit();
        return;
      }
      case 'stale':
        this.settled.emit(); // alansız cakisma bandı interceptor'da
        return;
      default: {
        const messages = error.alanlar ? Object.values(error.alanlar).flat() : [];
        if (messages.length > 0) this.errors.set(messages);
        else if (!genelGosterilir(error)) this.errors.set([error.detay]);
      }
    }
  }
}
