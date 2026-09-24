import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { SecimOgesi } from '@core/api/ui-tipleri';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentNotice } from '../document-notice';
import {
  CURRENCIES,
  type DocumentResult,
  SALE_CHANNELS,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
} from '../document-model';
import { type FormNotice, type SaleForm, formNotice, saleRequest } from '../document-requests';
import { SALES } from '../document.store';

const EMPTY = { kdvOrani: '0.20', doviz: 'TRY', kirayaVerme: false, satisiVerildi: false } as const;

/**
 * Yeni araç satışı — PARA (`POST /satislar`; Borç Cari(brüt) / Alacak Gelir(net) + KDV). KDV ve toplam SUNUCUDA; kur
 * boş → sunucu çözer. Yapısal idempotency: araç başına tek tamamlanmış satış (ikinci 400). Satış belgesi DEĞİŞMEZ →
 * gönderim onay ister. Bilgi alanları deftere yansımaz (Blazor notu aynen).
 */
@Component({
  selector: 'rc-sale-create-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    DocumentNotice,
    FormHatalari,
    MetinGirdisi,
    OnayKutusu,
    ParaGirdisi,
    SayiGirdisi,
    Secim,
    TarihSecici,
  ],
  templateUrl: './sale-create-form.html',
  styleUrl: '../finance-documents.scss',
})
export class SaleCreateForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly branches = input<readonly SecimOgesi[] | undefined>(undefined);
  readonly saved = output<DocumentResult>();
  readonly dirtyChange = output<boolean>();

  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly channels = SALE_CHANNELS;
  protected readonly vatOptions: readonly SecenekOgesi<VatRate>[] = VAT_RATES.map((v) => ({
    deger: v,
    etiket: VAT_LABELS[v],
  }));
  protected readonly currencyOptions: readonly SecenekOgesi<string>[] = CURRENCIES.map((c) => ({
    deger: c,
    etiket: c,
  }));
  protected readonly notice = signal<FormNotice | null>(null);

  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    alici: new FormControl<SecimSecenegi | null>(null, Validators.required),
    satisNet: new FormControl<string | null>(null, Validators.required),
    kdvOrani: new FormControl<VatRate | null>(EMPTY.kdvOrani, Validators.required),
    tarih: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>(EMPTY.doviz),
    kur: new FormControl<string | null>(null),
    noterNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    hedefFiyat: new FormControl<string | null>(null),
    satisKm: new FormControl<number | null>(null, Validators.min(0)),
    satisKanali: new FormControl<string | null>(null, Validators.maxLength(128)),
    devir: new FormControl<string | null>(null, Validators.maxLength(128)),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
    ilanKm: new FormControl<number | null>(null, Validators.min(0)),
    listeDoviz: new FormControl<string | null>(null),
    satisNoktasi: new FormControl<string | null>(null, Validators.maxLength(128)),
    uygulananKampanya: new FormControl<string | null>(null, Validators.maxLength(128)),
    ihaleFirmasi: new FormControl<string | null>(null, Validators.maxLength(128)),
    ihaleTarihi: new FormControl<string | null>(null),
    ihaleSayisi: new FormControl<string | null>(null, Validators.maxLength(64)),
    noterSatisTarihi: new FormControl<string | null>(null),
    yevmiyeNumarasi: new FormControl<string | null>(null, Validators.maxLength(64)),
    aciklama2: new FormControl<string | null>(null, Validators.maxLength(512)),
    kirayaVerme: new FormControl<boolean | null>(EMPTY.kirayaVerme),
    satisiVerildi: new FormControl<boolean | null>(EMPTY.satisiVerildi),
  });
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });
  protected readonly submission = formGonderimi();

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  protected async submit(): Promise<void> {
    if (this.submission.gonderiliyor()) return;
    // İstemci doğrulaması onaydan ÖNCE (eksik formla onay sorulmaz); gönderimde `formGonderimi` yeniden denetler.
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.satis.satBaslik'),
      mesaj: this.t('finansBelge.satis.satOnay', {
        plaka: this.form.controls.arac.value?.etiket ?? '',
      }),
    });
    if (!yes) return;
    this.notice.set(null);
    const body = saleRequest(this.form.getRawValue() as SaleForm);
    this.submission.gonder(
      this.form,
      (key) => this.api.post<DocumentResult>(SALES, body, { islemAnahtari: key }),
      {
        esleme: { aracId: 'arac', aliciCariId: 'alici' },
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.satis.kaydedildi', { no: r.no }));
          this.form.reset({ ...EMPTY });
          this.submission.kilit.yenile();
          this.dirtyChange.emit(false);
          this.saved.emit(r);
        },
        hata: (h) => this.notice.set(formNotice(h)),
      },
    );
  }
}
