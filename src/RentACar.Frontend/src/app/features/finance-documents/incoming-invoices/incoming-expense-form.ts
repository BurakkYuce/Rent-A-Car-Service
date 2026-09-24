import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { SecimOgesi } from '@core/api/ui-tipleri';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';

import { DocumentNotice } from '../document-notice';
import {
  type IncomingInvoiceExpenseResult,
  type IncomingInvoiceRow,
  PAYMENT_METHODS,
  type PaymentMethod,
} from '../document-model';
import {
  type FormNotice,
  type IncomingExpenseForm as ExpenseValue,
  formNotice,
  incomingExpenseRequest,
  vatBreakdownText,
} from '../document-requests';
import { INCOMING, recordPath } from '../document.store';

/**
 * Giderleştirme — PARA, gelen faturanın TEK para yolu (`POST /gelen-efatura/{id}/giderlestir`; her KDV kademesi ayrı
 * gider satırı, gider servisinin dengeli kümesi). Anahtar SUNUCUDA deterministiktir (fatura + kademe): başlık
 * gönderilmez, ikinci istek 409 `mukerrer` (kayıt yenilenir, otomatik tekrar yok). Onaylı ister.
 */
@Component({
  selector: 'rc-incoming-expense-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    DocumentNotice,
    FormHatalari,
    MetinGirdisi,
    Secim,
  ],
  template: `
    <section class="bolum" aria-labelledby="rc-gelen-gider">
      <h2 id="rc-gelen-gider">
        {{ 'finansBelge.gelen.giderlestirBaslik' | transloco: { ettn: invoice().ettn } }}
      </h2>
      <p class="not">{{ 'finansBelge.gelen.giderlestirAciklama' | transloco }}</p>
      <form class="form" [formGroup]="form" (ngSubmit)="submit()">
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'finansBelge.odemeYontemi' | transloco">
            <rc-secim formControlName="odemeYontemi" [secenekler]="methodOptions" />
          </rc-alan>
          <rc-alan
            [etiket]="'finansBelge.gelen.tedarikciAcik' | transloco"
            [ipucu]="'finansBelge.gelen.tedarikciIpucu' | transloco"
          >
            <rc-arama-secim formControlName="cari" [kaynak]="customers" />
          </rc-alan>
          <rc-alan [etiket]="'finansBelge.sube' | transloco">
            <rc-metin-girdisi formControlName="sube" liste="dl-gelen-sube" [azamiUzunluk]="64" />
          </rc-alan>
        </div>
        <datalist id="dl-gelen-sube">
          @for (b of branches() ?? []; track b.id) {
            <option [value]="b.etiket"></option>
          }
        </datalist>
        <rc-form-hatalari [hatalar]="submission.genelHatalar()" />
        <rc-document-notice [notice]="notice()" />
        <div class="form__eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="submission.gonderiliyor()"
          >
            {{
              submission.gonderiliyor()
                ? ('form.gonderiliyor' | transloco)
                : ('finansBelge.gelen.deftereYaz' | transloco)
            }}
          </button>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            (click)="closed.emit()"
          >
            {{ 'finansBelge.kapat' | transloco }}
          </button>
        </div>
      </form>
    </section>
  `,
  styleUrl: '../finance-documents.scss',
})
export class IncomingExpenseForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly invoice = input.required<IncomingInvoiceRow>();
  readonly branches = input<readonly SecimOgesi[] | undefined>(undefined);
  readonly done = output<IncomingInvoiceExpenseResult | null>();
  readonly closed = output<void>();

  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly methodOptions: readonly SecenekOgesi<PaymentMethod>[] = PAYMENT_METHODS.map(
    (x) => ({ deger: x, etiket: this.t(`finansBelge.odemeYontemleri.${x}`) }),
  );
  protected readonly notice = signal<FormNotice | null>(null);
  protected readonly form = new FormGroup({
    odemeYontemi: new FormControl<PaymentMethod | null>('AcikHesap', Validators.required),
    cari: new FormControl<SecimSecenegi | null>(null),
    sube: new FormControl<string | null>(null, Validators.maxLength(64)),
  });
  protected readonly submission = formGonderimi();

  protected async submit(): Promise<void> {
    if (this.submission.gonderiliyor()) return;
    this.notice.set(null);
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.gelen.giderlestir'),
      // r300 M4: deftere gidecek kırılım (KAYITLI değerler) onayda görünür; kaydedilmemiş bağlama burada yoktur.
      mesaj: this.t('finansBelge.gelen.giderlestirOnay', {
        ettn: this.invoice().ettn,
        kirilim: vatBreakdownText(this.invoice(), this.t('finansBelge.gelen.kirilimYok')),
      }),
    });
    if (!yes) return;
    const id = this.invoice().id;
    const body = incomingExpenseRequest(this.form.getRawValue() as ExpenseValue);
    this.submission.gonder(
      this.form,
      () =>
        this.api.post<IncomingInvoiceExpenseResult>(
          recordPath(INCOMING, id, '/giderlestir'),
          body,
          {
            context: istekBaglami({ mukerrerCagiranGosterir: true }),
          },
        ),
      {
        esleme: { cariId: 'cari' },
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.gelen.giderlestirildi', { adet: r.giderSatiri }));
          this.done.emit(r);
        },
        hata: (h) => {
          this.notice.set(formNotice(h));
          if (h.kod === 'mukerrer' || h.kod === 'cakisma') this.done.emit(null);
        },
      },
    );
  }
}
