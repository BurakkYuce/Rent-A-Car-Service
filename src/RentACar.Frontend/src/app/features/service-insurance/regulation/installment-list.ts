import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

import { ApiIstemcisi, type QueryParameters } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import type { DayText } from '@core/form/tarih-girdisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Sayfa } from '@core/api/sayfa';
import { FetchPolicy } from '@core/veri/fetch-policy';
import type { TemelStore } from '@core/veri/temel-store';
import type { FilterCatalog, ListeTanimi } from '@core/veri/liste-sorgusu';
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
import { NumberInput } from '@shared/form/kontroller/number-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { inspectionColumns, mtvColumns } from '../service-insurance-columns';
import {
  INSPECTION_LIST,
  type InspectionDetail,
  type InspectionRequest,
  MTV_LIST,
  type MtvDetail,
  type MtvRequest,
  REGULATION,
} from '../service-insurance-model';
import { InspectionListStore, MtvListStore } from '../service-insurance.store';
import type { InstallmentKind } from './installment-pay-panel';
import { RegulationTabs } from './regulation-tabs';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

interface Row {
  readonly id: string;
  readonly plaka: string;
}

/**
 * MTV (`/app/regulasyon/mtv`) ve muayene (`/app/regulasyon/muayene`) listeleri — Blazor `/regulasyon` MTV / Muayene
 * bölümleri: süzgeç, liste, "Ekle" (OperationsWrite; bilgi kaydı, deftere yazmaz). Kısmi ödeme ve ödeme geçmişi
 * kayıt sayfasında. Muayene ekleme Blazor'da FinanceWrite ile kapılıydı; uç OperationsWrite ister — SPA uca uyar.
 */
@Component({
  selector: 'rc-installment-list',
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
    NumberInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, MtvListStore, InspectionListStore],
  templateUrl: './installment-list.html',
  styleUrl: '../service-insurance.scss',
})
export class InstallmentList implements UnsavedChangesOwner {
  protected readonly kind: InstallmentKind =
    (inject(ActivatedRoute).snapshot.data['kind'] as InstallmentKind | undefined) ?? 'mtv';
  private readonly mtvStore = inject(MtvListStore);
  private readonly inspectionStore = inject(InspectionListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly session = inject(SessionService);
  private readonly t = translationFunction();

  protected readonly list = (this.kind === 'mtv'
    ? this.mtvStore.list
    : this.inspectionStore.list) as unknown as TemelStore<Sayfa<Row>, QueryParameters>;
  protected readonly query = listQueryUrlSync<FilterCatalog>(
    (this.kind === 'mtv' ? MTV_LIST : INSPECTION_LIST) as ListeTanimi<FilterCatalog>,
  );
  protected readonly columns = (this.kind === 'mtv'
    ? mtvColumns(this.t)
    : inspectionColumns(this.t)) as readonly TabloSutunu<Row>[];
  protected readonly rowId = (r: Row) => r.id;
  protected readonly detailRoot =
    this.kind === 'mtv' ? '/regulasyon/mtv' : '/regulasyon/muayeneler';
  protected readonly vehicles = serverSelectionSource('arac');
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.canWrite() && (this.createToggle() ?? this.list.veri()?.toplam === 0),
  );
  protected readonly paidOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'true', etiket: this.t('servisSigorta.evet') },
    { deger: 'false', etiket: this.t('servisSigorta.hayir') },
  ];

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    odendi: new FormControl<'true' | 'false' | null>(null),
    bas: new FormControl<DayText | null>(null),
    bit: new FormControl<DayText | null>(null),
  });

  /** MTV: dönem + tutar + vade. Muayene: muayene tarihi + bitiş + ücret + işlem km. */
  protected readonly form = new FormGroup({
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    donem: new FormControl<string | null>(
      null,
      this.kind === 'mtv' ? [Validators.required, Validators.maxLength(16)] : [],
    ),
    tutar: new FormControl<string | null>(null),
    tarih: new FormControl<DayText | null>(null, Validators.required),
    bitis: new FormControl<DayText | null>(
      null,
      this.kind === 'muayene' ? Validators.required : [],
    ),
    islemKm: new FormControl<number | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formSubmission();

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.query.apiParametreleri,
      yukle: (p: QueryParameters) => this.list.yukle(p),
      sifirla: () => this.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler as Readonly<Record<string, string | undefined>>;
      const [start, bit] = this.kind === 'mtv' ? ['vadeBas', 'vadeBit'] : ['bitisBas', 'bitisBit'];
      untracked(() =>
        this.filterForm.reset({
          plaka: f['plaka'] ?? null,
          odendi: (f['odendi'] as 'true' | 'false' | undefined) ?? null,
          bas: f[start] ?? null,
          bit: f[bit] ?? null,
        }),
      );
    });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    const range =
      this.kind === 'mtv'
        ? { vadeBas: v.bas ?? undefined, vadeBit: v.bit ?? undefined }
        : { bitisBas: v.bas ?? undefined, bitisBit: v.bit ?? undefined };
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: textValue(v.plaka) ?? undefined,
        odendi: v.odendi ?? undefined,
        ...range,
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
    const mtv: MtvRequest = {
      vehicleId: v.arac?.id ?? null,
      donem: textValue(v.donem),
      tutar: v.tutar,
      vade: momentValue(v.tarih, null),
      aciklama: textValue(v.aciklama),
    };
    const inspection: InspectionRequest = {
      vehicleId: v.arac?.id ?? null,
      muayeneTarihi: momentValue(v.tarih, null),
      bitis: momentValue(v.bitis, null),
      ucret: v.tutar,
      islemKm: v.islemKm,
      aciklama: textValue(v.aciklama),
    };
    const reset = () => this.form.reset();
    this.submission.gonder<unknown>(
      this.form,
      (key): Observable<unknown> =>
        this.kind === 'mtv'
          ? this.api.post<MtvDetail>(`${REGULATION}/mtv`, mtv, { islemAnahtari: key })
          : this.api.post<InspectionDetail>(`${REGULATION}/muayeneler`, inspection, {
              islemAnahtari: key,
            }),
      {
        esleme:
          this.kind === 'mtv'
            ? { vehicleId: 'arac', vade: 'tarih' }
            : { vehicleId: 'arac', muayeneTarihi: 'tarih', ucret: 'tutar' },
        basarili: () => {
          this.toast.basari(
            this.t(
              this.kind === 'mtv' ? 'servisSigorta.mtv.eklendi' : 'servisSigorta.muayene.eklendi',
            ),
          );
          reset();
          this.list.yenile();
        },
        hata: (h) => {
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) reset();
            this.list.yenile();
          }
        },
      },
    );
  }
}
