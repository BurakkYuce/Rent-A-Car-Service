import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  type OnInit,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { paraBicimle } from '@core/bicim/bicim';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import {
  ACCOUNT_KINDS,
  type AccountKind,
  type PenaltyDetail,
  type PenaltyPaymentResult,
} from '../document-model';
import {
  type PenaltyPaymentForm as PaymentValue,
  penaltyPaymentRequest,
} from '../document-requests';
import { DocumentNotice } from '../document-notice';
import { DocumentSubmission } from '../document-submission';
import { DocumentSubmitBar } from '../document-submit-bar';
import { PENALTIES, recordPath } from '../document.store';

/** Ceza ödeme formunun donmuş deneme kapsamı (sayfa `PendingDocumentAttempts` anahtarı). */
export const penaltyPaymentScope = (id: string) => `ceza-odeme:${id}`;

/**
 * Ceza kalem ödemesi — PARA (`POST /cezalar/{id}/odeme`; Borç Gider / Alacak Kasa·Banka). Gönderim
 * {@link DocumentSubmission}: uçuşta kilit; belirsiz sonuçta gövde donar (ceza başına sayfada saklanır), tekrar yalnız
 * o gövdeyle; `mevcut`suz 409'da anahtar KORUNUR (r300 M3). Tutar boş → kalemin kalanının tamamı (SUNUCU). Kalem
 * değişince tutar TEMİZLENİR (eski kalemin tutarı yeni kaleme gitmesin). Bileşen ceza başına yeniden kurulur.
 */
@Component({
  selector: 'rc-penalty-payment-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    DocumentNotice,
    DocumentSubmitBar,
    MetinGirdisi,
    ParaGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './penalty-payment-form.html',
  styleUrl: '../finance-documents.scss',
})
export class PenaltyPaymentForm implements OnInit {
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly detail = input.required<PenaltyDetail>();
  readonly paid = output<PenaltyPaymentResult | null>();
  readonly dirtyChange = output<boolean>();

  protected readonly lineOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.detail()
      .kalemler.filter((k) => (toNumber(k.kalan) ?? 0) > 0)
      .map((k) => ({
        deger: k.id,
        etiket: this.t('finansBelge.ceza.kalemSecenek', {
          sira: k.sira,
          sebep: k.sebep ?? '—',
          kalan: paraBicimle(toNumber(k.kalan)),
        }),
      })),
  );
  protected readonly accountOptions: readonly SecenekOgesi<AccountKind>[] = ACCOUNT_KINDS.map(
    (a) => ({ deger: a, etiket: a }),
  );

  protected readonly form = new FormGroup({
    satirId: new FormControl<string | null>(null, Validators.required),
    tutar: new FormControl<string | null>(null),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    tarih: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    islemYapan: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = new DocumentSubmission(
    this.form,
    () => penaltyPaymentScope(this.detail().ceza.id),
    {},
  );

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.form.controls.satirId.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.form.controls.tutar.reset(null));
    this.form.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  ngOnInit(): void {
    this.submission.restore();
  }

  protected submit(): void {
    const id = this.detail().ceza.id;
    this.submission.submit<PenaltyPaymentResult>(
      recordPath(PENALTIES, id, '/odeme'),
      () => penaltyPaymentRequest(this.form.getRawValue() as PaymentValue),
      {
        succeeded: (r) => {
          this.toast.basari(
            this.t('finansBelge.ceza.odemeYazildi', {
              tutar: paraBicimle(toNumber(r.tutar)),
              kalan: paraBicimle(toNumber(r.cezaKalan)),
            }),
          );
          this.reset();
          this.paid.emit(r);
        },
        recorded: () => this.reset(),
        reload: () => this.paid.emit(null),
      },
    );
  }

  protected async abandon(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.vazgecBaslik'),
      mesaj: this.t('finansBelge.vazgecMesaj'),
      tehlikeli: true,
    });
    if (yes) this.submission.abandon();
  }

  private reset(): void {
    this.form.reset({ hesap: 'Kasa' });
    this.submission.renew();
    this.dirtyChange.emit(false);
  }
}
