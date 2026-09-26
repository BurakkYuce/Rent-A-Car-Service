import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import type { DayText } from '@core/form/tarih-girdisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { momentValue, textValue } from '@features/planlama-ortak/form-yardimcilari';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { policyColumns } from '../service-insurance-columns';
import {
  INSURANCE_TYPES,
  type InsuranceType,
  POLICY_CURRENCIES,
  POLICY_LIST,
  type PolicyDetail,
  type PolicyRequest,
  REGULATION,
} from '../service-insurance-model';
import { PolicyListStore, RegulationOptionsStore } from '../service-insurance.store';
import { RegulationTabs } from './regulation-tabs';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

/**
 * Sigorta poliçeleri (`/app/regulasyon`) — Blazor `/regulasyon` "Sigorta" bölümü: süzgeç, liste (Araç/İMM/Aksesuar
 * değeri ve Kalan BİLGİ), "Ekle" formu (OperationsWrite; deftere yazmaz). Ödeme ve zeyiller poliçe kaydında
 * (`/app/regulasyon/sigortalar/:id`). Okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; ARACIN şubesi süzer.
 */
@Component({
  selector: 'rc-policy-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    Icon,
    TextInput,
    MoneyInput,
    RegulationTabs,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, PolicyListStore, RegulationOptionsStore],
  templateUrl: './policy-list.html',
  styleUrl: '../service-insurance.scss',
})
export class PolicyList implements UnsavedChangesOwner {
  protected readonly store = inject(PolicyListStore);
  private readonly optionsStore = inject(RegulationOptionsStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(POLICY_LIST);
  protected readonly columns = policyColumns(this.t);
  protected readonly rowId = (r: { id: string }) => r.id;
  protected readonly vehicles = serverSelectionSource('arac');
  private readonly session = inject(SessionService);
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.canWrite() && (this.createToggle() ?? this.store.list.veri()?.toplam === 0),
  );

  protected readonly typeOptions: readonly SecenekOgesi<InsuranceType>[] = INSURANCE_TYPES.map(
    (x) => ({ deger: x, etiket: x }),
  );
  protected readonly paidOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'true', etiket: this.t('servisSigorta.evet') },
    { deger: 'false', etiket: this.t('servisSigorta.hayir') },
  ];
  protected readonly currencyOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.optionsStore.options.veri()?.dovizler ?? POLICY_CURRENCIES).map((d) => ({
      deger: d,
      etiket: d,
    })),
  );
  protected readonly companies = computed(() => this.optionsStore.options.veri()?.firmalar ?? []);

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tip: new FormControl<InsuranceType | null>(null),
    odendi: new FormControl<'true' | 'false' | null>(null),
  });

  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    tip: new FormControl<InsuranceType | null>('Trafik', Validators.required),
    baslangic: new FormControl<DayText | null>(null, Validators.required),
    bitis: new FormControl<DayText | null>(null, Validators.required),
    prim: new FormControl<string | null>(null),
    doviz: new FormControl<string | null>('TRY'),
    policeNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    firma: new FormControl<string | null>(null, Validators.maxLength(128)),
    acenta: new FormControl<string | null>(null, Validators.maxLength(128)),
    aracDegeri: new FormControl<string | null>(null),
    immDegeri: new FormControl<string | null>(null),
    aksesuarDegeri: new FormControl<string | null>(null),
  });
  protected readonly submission = formSubmission();
  protected readonly currency = toSignal(this.form.controls.doviz.valueChanges, {
    initialValue: this.form.controls.doviz.value,
  });

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          tip: f.tip ?? null,
          odendi: f.odendi ?? null,
        }),
      );
    });
    effect(() => {
      if (this.createOpen() && this.optionsStore.options.tur() === 'bos')
        untracked(() => this.optionsStore.options.yukle());
    });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: textValue(v.plaka) ?? undefined,
        tip: v.tip ?? undefined,
        odendi: v.odendi ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected toggleCreate(): void {
    this.createToggle.set(!this.createOpen());
  }

  protected create(): void {
    const v = this.form.getRawValue();
    const body: PolicyRequest = {
      vehicleId: v.arac?.id ?? null,
      tip: v.tip,
      baslangic: momentValue(v.baslangic, null),
      bitis: momentValue(v.bitis, null),
      prim: v.prim,
      doviz: v.doviz,
      policeNo: textValue(v.policeNo),
      firma: textValue(v.firma),
      acenta: textValue(v.acenta),
      aracDegeri: v.aracDegeri,
      immDegeri: v.immDegeri,
      aksesuarDegeri: v.aksesuarDegeri,
    };
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<PolicyDetail>(`${REGULATION}/sigortalar`, body, { islemAnahtari: key }),
      {
        esleme: { vehicleId: 'arac' },
        basarili: (d) => {
          this.toast.basari(this.t('servisSigorta.sigorta.eklendi', { plaka: d.police.plaka }));
          this.form.reset({ tip: 'Trafik', doviz: 'TRY' });
          this.store.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.form.reset({ tip: 'Trafik', doviz: 'TRY' });
            this.store.list.yenile();
          }
        },
      },
    );
  }
}
