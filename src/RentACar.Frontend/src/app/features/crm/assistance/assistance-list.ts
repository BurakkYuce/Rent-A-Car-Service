import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { DayText } from '@core/form/tarih-girdisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DateTimePicker } from '@shared/form/tarih/date-time-picker';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { assistanceColumns } from '../crm-columns';
import {
  assistanceHiddenContact,
  assistanceRequest,
  assistanceToForm,
  emptyAssistance,
} from '../crm-forms';
import {
  ASSISTANCE,
  ASSISTANCE_LIST,
  recordPath,
  type Assistance,
  type AssistanceCard,
} from '../crm-model';
import { AssistanceStore, rentalPickSource } from '../crm.store';
import { RecordEditor } from '../record-editor';

/** Blazor tek "Durum" seçimi → uç bayrakları. */
export type AssistanceView = 'acik' | 'kapali' | 'cekici' | 'lastik';

export function viewFilters(v: AssistanceView | null): {
  kapandi?: boolean;
  hareketEdemiyor?: boolean;
  yedekLastik?: boolean;
} {
  switch (v) {
    case 'acik':
      return { kapandi: false };
    case 'kapali':
      return { kapandi: true };
    case 'cekici':
      return { hareketEdemiyor: true };
    case 'lastik':
      return { yedekLastik: true };
    default:
      return {};
  }
}

export function viewOf(f: {
  readonly kapandi?: boolean;
  readonly hareketEdemiyor?: boolean;
  readonly yedekLastik?: boolean;
}): AssistanceView | null {
  if (f.hareketEdemiyor) return 'cekici';
  if (f.yedekLastik) return 'lastik';
  if (f.kapandi === true) return 'kapali';
  if (f.kapandi === false) return 'acik';
  return null;
}

/**
 * Assistans (yol yardım) talepleri (`/app/assistans`) — Blazor `AssistansTalepList` paritesi: süzgeçler (plaka, tarih
 * aralığı, arama, durum), özet (N talep · açık · hareket edemiyor — sunucu sayar), liste, satırda Düzenle / Sil,
 * Yeni/Düzenle formu. Kira seçilirse boş plaka/ad/telefon SÖZLEŞMEDEN kopyalanır (olay tutanağı; elle yazılan değer
 * ezilmez). Ad/telefon bağlı müşteri KVKK ile anonimse sunucudan `null` gelir. OperationsWrite.
 */
@Component({
  selector: 'rc-assistance-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FilterPanelComponent,
    PageBand,
    PlateChipComponent,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    TextInput,
    Checkbox,
    Selection,
    Table,
    TableCell,
    DateTimePicker,
    DatePicker,
  ],
  providers: [FetchPolicy, AssistanceStore],
  templateUrl: './assistance-list.html',
  styleUrl: '../crm.scss',
})
export class AssistanceList implements UnsavedChangesOwner {
  protected readonly store = inject(AssistanceStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(ASSISTANCE_LIST);
  protected readonly columns = assistanceColumns(this.t);
  protected readonly rowId = (r: Assistance) => r.id;
  protected readonly rentals = rentalPickSource();

  protected readonly viewOptions: readonly SecenekOgesi<AssistanceView>[] = (
    ['acik', 'kapali', 'cekici', 'lastik'] as const
  ).map((x) => ({ deger: x, etiket: this.t(`crm.assistans.gorunumler.${x}`) }));

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null),
    tarihBas: new FormControl<DayText | null>(null),
    tarihBit: new FormControl<DayText | null>(null),
    ara: new FormControl<string | null>(null),
    gorunum: new FormControl<AssistanceView | null>(null),
  });

  protected readonly form = new FormGroup({
    kira: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    adSoyad: new FormControl<string | null>(null, Validators.maxLength(256)),
    cepTel: new FormControl<string | null>(null, Validators.maxLength(32)),
    zaman: new FormControl<string | null>(null),
    sebep: new FormControl<string | null>(null, Validators.maxLength(512)),
    yedekLastikMi: new FormControl<boolean>(false, { nonNullable: true }),
    aracHareketMi: new FormControl<boolean>(false, { nonNullable: true }),
    kapandi: new FormControl<boolean>(false, { nonNullable: true }),
    mesaj: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(2048)]),
    cozum: new FormControl<string | null>(null, Validators.maxLength(1024)),
    clearName: new FormControl<boolean>(false, { nonNullable: true }),
    clearPhone: new FormControl<boolean>(false, { nonNullable: true }),
  });
  protected readonly submission = formSubmission();
  /** Düzenlenen kayıtta KVKK ile gizlenen ad/telefon ("Temizle" kutusu yalnız bunlarda). */
  protected readonly hiddenContact = computed(() =>
    assistanceHiddenContact(this.editor.base()?.talep ?? null),
  );

  protected readonly editor = new RecordEditor<Assistance, AssistanceCard>({
    path: ASSISTANCE,
    form: this.form,
    anchor: 'rc-assistans-formu',
    lock: this.submission.kilit,
    empty: emptyAssistance,
    toForm: assistanceToForm,
    rowOf: (c) => c.talep,
    reload: () => this.reload(),
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        this.store.list.yukle(p);
        this.store.counts.yukle(p);
      },
      sifirla: () => {
        this.store.list.reset();
        this.store.counts.reset();
      },
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          tarihBas: (f.tarihBas as DayText | undefined) ?? null,
          tarihBit: (f.tarihBit as DayText | undefined) ?? null,
          ara: f.ara ?? null,
          gorunum: viewOf(f),
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
    const flags = viewFilters(v.gorunum);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: v.plaka?.trim() || undefined,
        tarihBas: v.tarihBas ?? undefined,
        tarihBit: v.tarihBit ?? undefined,
        ara: v.ara?.trim() || undefined,
        kapandi: flags.kapandi,
        hareketEdemiyor: flags.hareketEdemiyor,
        yedekLastik: flags.yedekLastik,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected edit(row: Assistance): void {
    void this.editor.edit(row, row.id);
  }

  protected remove(row: Assistance): void {
    void this.editor.remove(
      row.id,
      this.t('crm.assistans.silBaslik'),
      this.t('crm.assistans.silMesaj'),
      this.t('crm.assistans.silindi'),
    );
  }

  protected save(): void {
    const id = this.editor.recordId;
    if (this.editor.waiting) return;
    const card = this.editor.base();
    const body = assistanceRequest(
      this.form.getRawValue(),
      id === null || card === null ? null : { surum: card.surum, row: card.talep },
    );
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<AssistanceCard>(ASSISTANCE, body, { islemAnahtari: key })
          : this.api.put<AssistanceCard>(recordPath(ASSISTANCE, id), body, { islemAnahtari: key }),
      {
        esleme: { rentalId: 'kira' },
        basarili: () => {
          this.toast.basari(
            this.t(id === null ? 'crm.assistans.eklendi' : 'crm.assistans.kaydedildi'),
          );
          this.editor.saved();
        },
        hata: (h) => this.editor.failed(h.kod),
      },
    );
  }

  private reload(): void {
    this.store.list.yenile();
    this.store.counts.yenile();
  }
}
