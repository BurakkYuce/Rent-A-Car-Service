import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import type { SecimOgesi } from '@core/api/ui-tipleri';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import { DocumentSubmission } from '../document-submission';
import { DocumentSubmitBar } from '../document-submit-bar';
import {
  CURRENCIES,
  type DocumentResult,
  SALE_CHANNELS,
  VAT_LABELS,
  VAT_RATES,
  type VatRate,
} from '../document-model';
import { type SaleForm, saleRequest } from '../document-requests';
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
    DocumentSubmitBar,
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
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  readonly branches = input<readonly SecimOgesi[] | undefined>(undefined);
  readonly saved = output<DocumentResult | null>();
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
  /** Para simgesi form değerinden (donmuş deneme `emitEvent:false` ile geri yüklenir — r300b N2). */
  protected readonly currency = signal<string | null>(EMPTY.doviz);
  /** Yapısal idempotency (araç başına tek satış) + aynı yaşam döngüsü: uçuşta kilit, belirsizde donmuş gövde. */
  protected readonly submission = new DocumentSubmission(this.form, () => 'yeni-satis', {
    aracId: 'arac',
    aliciCariId: 'alici',
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.form.valueChanges
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
    // Açık kur yalnız seçildiği dövize aittir (r300 M2: USD kuru EUR satışına gidiyordu).
    this.form.controls.doviz.valueChanges.pipe(takeUntilDestroyed(destroyRef)).subscribe((d) => {
      this.currency.set(d);
      if (this.form.controls.kur.value !== null) this.form.controls.kur.reset(null);
    });
    // r300b N4: sonucu bilinmeyen satış denemesi form yeniden açılınca kilitli, aynı gövde + anahtarla gelir.
    this.submission.restore();
    this.currency.set(this.form.controls.doviz.value);
  }

  protected async submit(): Promise<void> {
    if (this.submission.sending()) return;
    // İstemci doğrulaması onaydan ÖNCE (eksik formla onay sorulmaz). Donmuş denemede form kilitli, onay yine sorulur.
    if (!this.submission.frozen()) {
      this.form.markAllAsTouched();
      if (this.form.invalid) return;
    }
    const yes = await this.confirm.sor({
      baslik: this.t('finansBelge.satis.satBaslik'),
      mesaj: this.t('finansBelge.satis.satOnay', {
        plaka: this.form.controls.arac.value?.etiket ?? '',
      }),
    });
    if (!yes) return;
    this.submission.submit<DocumentResult>(
      SALES,
      () => saleRequest(this.form.getRawValue() as SaleForm),
      {
        succeeded: (r) => {
          this.toast.basari(this.t('finansBelge.satis.kaydedildi', { no: r.no }));
          this.reset();
          this.saved.emit(r);
        },
        recorded: () => this.reset(),
        reload: () => this.saved.emit(null),
        // r300b N4: satış yapısal idempotenttir (araç başına tek satış). Belirsiz denemenin tekrarı reddedildiyse
        // ("Araç zaten satılmış") bu büyük olasılıkla İLK denemenin yazıldığı anlamına gelir.
        rejected: (_error, retry) => {
          if (retry) {
            this.submission.showNotice({ tone: 'uyari', key: 'satisVar', params: {} });
            this.saved.emit(null);
          }
        },
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
    this.form.reset({ ...EMPTY });
    this.submission.renew();
    this.dirtyChange.emit(false);
  }
}
