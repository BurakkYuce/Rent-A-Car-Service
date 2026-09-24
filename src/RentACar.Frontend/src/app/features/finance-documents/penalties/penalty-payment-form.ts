import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { paraBicimle } from '@core/bicim/bicim';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentNotice } from '../document-notice';
import {
  ACCOUNT_KINDS,
  type AccountKind,
  type PenaltyDetail,
  type PenaltyPaymentResult,
} from '../document-model';
import {
  type FormNotice,
  type PenaltyPaymentForm as PaymentValue,
  formNotice,
  penaltyPaymentRequest,
} from '../document-requests';
import { PENALTIES, recordPath } from '../document.store';

/**
 * Ceza kalem ödemesi — PARA (`POST /cezalar/{id}/odeme`; Borç Gider / Alacak Kasa·Banka). `Idempotency-Key` ZORUNLU:
 * mantıksal gönderim başına bir anahtar; doğrulama/ağ hatası ve oturum düşmesinden sonraki tekrar AYNI anahtarla,
 * 2xx ve `mukerrer` sonrası yeni anahtar. Tutar boş → kalemin kalanının tamamı (SUNUCU; istemci kalanı kopyalamaz).
 * Kalem değişince tutar TEMİZLENİR (eski kalemin tutarı yeni kaleme gitmesin — DEVIR §5). Bileşen ceza başına yeniden
 * kurulur (`@for … track id`).
 */
@Component({
  selector: 'rc-penalty-payment-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    DocumentNotice,
    FormHatalari,
    MetinGirdisi,
    ParaGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './penalty-payment-form.html',
  styleUrl: '../finance-documents.scss',
})
export class PenaltyPaymentForm {
  private readonly api = inject(ApiIstemcisi);
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
  protected readonly notice = signal<FormNotice | null>(null);

  protected readonly form = new FormGroup({
    satirId: new FormControl<string | null>(null, Validators.required),
    tutar: new FormControl<string | null>(null),
    hesap: new FormControl<AccountKind | null>('Kasa', Validators.required),
    tarih: new FormControl<string | null>(null),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    islemYapan: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formGonderimi();

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.form.controls.satirId.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.form.controls.tutar.reset(null));
    this.form.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  protected submit(): void {
    this.notice.set(null);
    const id = this.detail().ceza.id;
    const body = penaltyPaymentRequest(this.form.getRawValue() as PaymentValue);
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<PenaltyPaymentResult>(recordPath(PENALTIES, id, '/odeme'), body, {
          islemAnahtari: key,
        }),
      {
        basarili: (r) => {
          this.toast.basari(
            this.t('finansBelge.ceza.odemeYazildi', {
              tutar: paraBicimle(toNumber(r.tutar)),
              kalan: paraBicimle(toNumber(r.cezaKalan)),
            }),
          );
          this.reset();
          this.paid.emit(r);
        },
        hata: (h) => {
          this.notice.set(formNotice(h));
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.reset();
            this.paid.emit(null);
          }
          if (h.kod === 'cakisma') this.paid.emit(null);
        },
      },
    );
  }

  private reset(): void {
    this.form.reset({ hesap: 'Kasa' });
    this.submission.kilit.yenile();
    this.dirtyChange.emit(false);
  }
}
