import { ChangeDetectionStrategy, Component, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { catchError, of } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import { Selection } from '@shared/form/kontroller/selection';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import { restDefinitionSource } from '@shared/form/tanim-crud/definition-source';
import { DatePicker } from '@shared/form/tarih/date-picker';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { occupancyFields, selectionSuggestions } from '../definition-catalog';

type BulkResult = Schema<'OccupancyBulkResult'>;

/** Blazor toplu formu 10 kademe (FAZ-73). */
export const LADDER_STEPS = 10;

export interface LadderStep {
  readonly esik: number | null;
  readonly carpan: number | null;
}

/**
 * Doldurulan kademeler: boş satır atlanır; yarım satır (eşik ya da çarpan tek başına) HATA — sunucu her kademede
 * ikisini ister, sessizce atlamak kullanıcının girdiğini kaybetmek olur. Dönen: kademeler ya da hatalı sıra (1'den).
 */
export function ladderSteps(
  rows: readonly LadderStep[],
): { readonly steps: { esikYuzde: number; carpanYuzde: number }[] } | { readonly half: number } {
  const steps: { esikYuzde: number; carpanYuzde: number }[] = [];
  for (const [i, r] of rows.entries()) {
    if (r.esik === null && r.carpan === null) continue;
    if (r.esik === null || r.carpan === null) return { half: i + 1 };
    steps.push({ esikYuzde: r.esik, carpanYuzde: r.carpan });
  }
  return { steps };
}

/**
 * F11.2a doluluk fiyat kuralları (Blazor `DolulukFiyatList`, OperationsWrite): genel tanım CRUD'u (panel) + toplu
 * kademe girişi (`POST /doluluk-kurallari/toplu`; servis önce TÜM kademeleri doğrular, yarım merdiven yazılmaz).
 */
@Component({
  selector: 'rc-occupancy-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    DefinitionCrud,
    Alan,
    FormErrors,
    TextInput,
    NumberInput,
    Selection,
    DatePicker,
    PageBand,
  ],
  styleUrl: '../definitions.scss',
  templateUrl: './occupancy-page.html',
})
export class OccupancyPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  protected readonly t = translationFunction();
  private readonly crud = viewChild(DefinitionCrud);

  private readonly groupSuggest = selectionSuggestions('/api/ui/v1/secim/arac-grubu', (o) => o.kod);
  private readonly branchSuggest = selectionSuggestions('/api/ui/v1/secim/sube');
  protected readonly fields = occupancyFields(this.t, {
    group: this.groupSuggest,
    branch: this.branchSuggest,
  });
  protected readonly source = restDefinitionSource('/api/ui/v1/doluluk-kurallari');

  /** Toplu formun öneri listeleri (tek seferlik, ≤ 20; alan serbest metin de kabul eder). */
  protected readonly groupOptions = toSignal(this.groupSuggest('').pipe(catchError(() => of([]))), {
    initialValue: [] as readonly string[],
  });
  protected readonly branchOptions = toSignal(
    this.branchSuggest('').pipe(catchError(() => of([]))),
    {
      initialValue: [] as readonly string[],
    },
  );
  protected readonly yesNo = [
    { deger: false, etiket: this.t('tanimlar.alan.hayir') },
    { deger: true, etiket: this.t('tanimlar.alan.evet') },
  ];

  protected readonly bulkForm = new FormGroup({
    kodOnEk: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(24)]),
    adOnEk: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(100)]),
    aracGrupKod: new FormControl<string | null>(null, Validators.maxLength(32)),
    sube: new FormControl<string | null>(null, Validators.maxLength(64)),
    sadeceKendiSubeleri: new FormControl<boolean | null>(false),
    gecerlilikBas: new FormControl<string | null>(null),
    gecerlilikBit: new FormControl<string | null>(null),
    kademeler: new FormArray(
      Array.from(
        { length: LADDER_STEPS },
        () =>
          new FormGroup({
            esik: new FormControl<number | null>(null, [Validators.min(1), Validators.max(100)]),
            carpan: new FormControl<number | null>(null, [Validators.min(0), Validators.max(50)]),
          }),
      ),
    ),
  });
  protected readonly bulkSubmit = formSubmission();
  protected readonly bulkError = signal<string | null>(null);
  protected readonly stepNumbers = Array.from({ length: LADDER_STEPS }, (_, i) => i);

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return (this.crud()?.hasUnsavedChanges() ?? false) || this.bulkForm.dirty;
  }

  protected saveLadder(): void {
    const v = this.bulkForm.getRawValue();
    const r = ladderSteps(v.kademeler);
    this.bulkError.set(null);
    if ('half' in r) {
      this.bulkError.set(this.t('tanimlar.occupancy.toplu.yarim', { n: r.half }));
      return;
    }
    if (r.steps.length === 0) {
      this.bulkError.set(this.t('tanimlar.occupancy.toplu.enAzBir'));
      return;
    }
    this.bulkSubmit.gonder(
      this.bulkForm,
      (key) =>
        this.api.post<BulkResult>(
          '/api/ui/v1/doluluk-kurallari/toplu',
          {
            kodOnEk: v.kodOnEk,
            adOnEk: v.adOnEk,
            aracGrupKod: v.aracGrupKod,
            sube: v.sube,
            sadeceKendiSubeleri: v.sadeceKendiSubeleri ?? false,
            gecerlilikBas: v.gecerlilikBas,
            gecerlilikBit: v.gecerlilikBit,
            kademeler: r.steps,
            aktif: true,
          },
          { islemAnahtari: key },
        ),
      {
        basarili: (res) => {
          this.toast.basari(
            this.t('tanimlar.occupancy.toplu.kaydedildi', { sayi: res.kimlikler.length }),
          );
          this.bulkForm.reset({ sadeceKendiSubeleri: false });
          this.crud()?.reload();
        },
      },
    );
  }
}
