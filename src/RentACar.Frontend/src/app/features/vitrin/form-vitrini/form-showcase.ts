import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { invariantDecimal, formatDecimal } from '@core/form/ondalik';
import type { GunAraligi } from '@core/form/tarih-girdisi';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimUcuOgesi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/kontroller/text-area';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Anahtar, Checkbox } from '@shared/form/kontroller/checkbox';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import { RadioGroup } from '@shared/form/kontroller/radio-group';
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { TabPanel, TabbedForm, type SekmeTanimi } from '@shared/form/sekmeli-form/tabbed-form';
import { DateTimePicker } from '@shared/form/tarih/date-time-picker';
import { DatePicker } from '@shared/form/tarih/date-picker';

type Durum = 'aktif' | 'pasif';
type PaymentType = 'nakit' | 'kart';

/**
 * Form seti vitrini: her CVA kontrolü, sekmeli yerleşim + sabit yan panel, alan hatası, gönderim
 * kilidi ve kaydedilmemiş değişiklik koruması tek sayfada. e2e (sahte arka uçla) bunu sürer; F4
 * kira formu aynı parçalarla kurulur.
 */
@Component({
  selector: 'rc-form-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    Alan,
    SearchSelection,
    FormErrors,
    TextArea,
    TextInput,
    Anahtar,
    Checkbox,
    MoneyInput,
    RadioGroup,
    NumberInput,
    Selection,
    TabbedForm,
    TabPanel,
    DateTimePicker,
    DatePicker,
  ],
  templateUrl: './form-showcase.html',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
      min-width: 0;
      padding: var(--rc-bosluk-4);
    }
    .ozet {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--rc-bosluk-1) var(--rc-bosluk-3);
      margin: 0;
      padding: var(--rc-bosluk-3);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-yuzey);
    }
    .ozet dt {
      color: var(--rc-metin-ikincil);
    }
    .ozet dd {
      margin: 0;
      text-align: end;
    }
  `,
})
export class FormShowcase implements UnsavedChangesOwner {
  private readonly api = inject(ApiIstemcisi);
  private readonly sekmeli = viewChild.required(TabbedForm);

  protected readonly tabs: readonly SekmeTanimi[] = [
    { kimlik: 'genel', etiket: 'Genel' },
    { kimlik: 'tarih-tutar', etiket: 'Tarih ve tutar' },
    { kimlik: 'secenekler', etiket: 'Seçenekler' },
  ];
  protected readonly statuses: readonly SecenekOgesi<Durum>[] = [
    { deger: 'aktif', etiket: 'Aktif' },
    { deger: 'pasif', etiket: 'Pasif' },
  ];
  protected readonly paymentTypes: readonly SecenekOgesi<PaymentType>[] = [
    { deger: 'nakit', etiket: 'Nakit' },
    { deger: 'kart', etiket: 'Kredi kartı' },
  ];
  protected readonly customers = serverSelectionSource('musteri');

  protected readonly form = new FormGroup({
    plaka: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(12)]),
    musteri: new FormControl<SecimUcuOgesi<'musteri'> | null>(null, Validators.required),
    durum: new FormControl<Durum | null>('aktif', Validators.required),
    adet: new FormControl<number | null>(1, [Validators.required, Validators.min(1)]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(500)),
    tutar: new FormControl<string | number | null>(null, Validators.required),
    cikisTarihi: new FormControl<string | null>(null, Validators.required),
    donusAni: new FormControl<string | null>(null),
    donem: new FormControl<GunAraligi | null>(null),
    kasko: new FormControl<boolean | null>(false),
    odemeTuru: new FormControl<PaymentType | null>('nakit'),
    otomatikFatura: new FormControl<boolean | null>(true),
    kosullarOnay: new FormControl<boolean | null>(false, Validators.requiredTrue),
  });

  protected readonly submission = formSubmission();
  protected readonly recordNo = signal<string | null>(null);

  constructor() {
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  /** Özet: değer invariant metin; kayan noktaya girmeden Türkçe yazım. */
  protected summaryAmount(): string {
    const amount = formatDecimal(invariantDecimal(this.form.controls.tutar.value, { kesir: 2 }), 2);
    return amount === '' ? '—' : `${amount} ₺`;
  }

  protected kaydet(): void {
    this.recordNo.set(null);
    const d = this.form.getRawValue();
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<{ no: string }>(
          '/api/ui/v1/vitrin/form',
          {
            plaka: d.plaka,
            musteriId: d.musteri?.id ?? null,
            durum: d.durum,
            adet: d.adet,
            aciklama: d.aciklama,
            tutar: d.tutar,
            cikisTarihi: d.cikisTarihi,
            donusAni: d.donusAni,
            donemBaslangic: d.donem?.baslangic ?? null,
            donemBitis: d.donem?.bitis ?? null,
            kasko: d.kasko,
            odemeTuru: d.odemeTuru,
            otomatikFatura: d.otomatikFatura,
          },
          { islemAnahtari: key },
        ),
      {
        gecersiz: () => this.sekmeli().goToFirstInvalid(),
        basarili: (result) => this.recordNo.set(result.no),
      },
    );
  }
}
