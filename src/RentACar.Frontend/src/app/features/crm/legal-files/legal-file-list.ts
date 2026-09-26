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
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { toNumber } from '@features/vehicles/vehicle-model';
import { MoneyPipe } from '@shared/bicim/bicim-pipe';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { MoneyInput } from '@shared/form/kontroller/money-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { legalColumns } from '../crm-columns';
import { emptyLegal, legalRequest, legalToForm } from '../crm-forms';
import {
  LEGAL_FILES,
  LEGAL_LIST,
  LEGAL_STATUSES,
  LEGAL_TYPES,
  legalExportParameters,
  recordPath,
  type LegalFile,
  type LegalFileCard,
} from '../crm-model';
import { LegalFileStore } from '../crm.store';
import { CustomerFilterLabel } from '../customer-filter-label';
import { RecordEditor } from '../record-editor';

type LegalType = (typeof LEGAL_TYPES)[number];
type LegalStatus = (typeof LEGAL_STATUSES)[number];

/**
 * Hukuk dosyaları (`/app/hukuk`) — Blazor `HukukList` paritesi: süzgeçler (müşteri, dosya no, fatura no, tarih, tür,
 * durum, arama), özet (N dosya · M açık — sunucu sayar), liste, satırda Düzenle / Sil, Yeni/Düzenle formu. Tutar /
 * tahsilat BİLGİ alanıdır — deftere ve cari bakiyeye YAZMAZ (ekranda yazılı çit). Kalan sunucunun değeri (SPA
 * hesaplamaz). Dışa aktarma ViewReports (süzgeç aynen taşınır). OperationsWrite.
 */
@Component({
  selector: 'rc-legal-file-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    FormErrors,
    TextInput,
    Checkbox,
    MoneyInput,
    MoneyPipe,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, LegalFileStore, CustomerFilterLabel],
  templateUrl: './legal-file-list.html',
  styleUrl: '../crm.scss',
})
export class LegalFileList implements UnsavedChangesOwner {
  protected readonly store = inject(LegalFileStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly toast = inject(ToastService);
  private readonly labels = inject(CustomerFilterLabel);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(LEGAL_LIST);
  protected readonly columns = legalColumns(this.t);
  protected readonly rowId = (r: LegalFile) => r.id;
  protected readonly num = toNumber;
  protected readonly customers = serverSelectionSource('musteri');

  protected readonly typeOptions: readonly SecenekOgesi<string>[] = LEGAL_TYPES.map((x) => ({
    deger: x,
    etiket: this.t(`crm.hukuk.turler.${x}`),
  }));
  protected readonly statusOptions: readonly SecenekOgesi<string>[] = LEGAL_STATUSES.map((x) => ({
    deger: x,
    etiket: this.t(`crm.hukuk.durumlar.${x}`),
  }));

  protected readonly export = computed<DisaAktarma | null>(() =>
    this.session.izinVar('ViewReports')
      ? {
          yol: '/listeler/export/hukuk',
          parametreler: legalExportParameters(this.query.sorgu().filtreler),
          bicimler: ['excel', 'csv', 'pdf'],
        }
      : null,
  );

  protected readonly filterForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    dosyaNo: new FormControl<string | null>(null),
    faturaNo: new FormControl<string | null>(null),
    tarihBas: new FormControl<DayText | null>(null),
    tarihBit: new FormControl<DayText | null>(null),
    tur: new FormControl<string | null>(null),
    durum: new FormControl<string | null>(null),
    ara: new FormControl<string | null>(null),
  });

  protected readonly form = new FormGroup({
    dosyaNo: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(64)]),
    faturaNoTemp: new FormControl<string | null>(null, Validators.maxLength(64)),
    tur: new FormControl<string | null>('Dava', Validators.required),
    cari: new FormControl<SecimSecenegi | null>(null),
    avukat: new FormControl<string | null>(null, Validators.maxLength(128)),
    avukatTel: new FormControl<string | null>(null, Validators.maxLength(32)),
    avukatMail: new FormControl<string | null>(null, [Validators.maxLength(256), Validators.email]),
    avukat2Ad: new FormControl<string | null>(null, Validators.maxLength(128)),
    avukat2Tel: new FormControl<string | null>(null, Validators.maxLength(32)),
    avukat2Mail: new FormControl<string | null>(null, [
      Validators.maxLength(256),
      Validators.email,
    ]),
    tutar: new FormControl<string | null>(null),
    tahsilat: new FormControl<string | null>(null),
    durum: new FormControl<string | null>('Acik', Validators.required),
    tarih: new FormControl<DayText | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1024)),
    aktif: new FormControl<boolean>(true, { nonNullable: true }),
  });
  protected readonly submission = formSubmission();

  protected readonly editor = new RecordEditor<LegalFile, LegalFileCard>({
    path: LEGAL_FILES,
    form: this.form,
    anchor: 'rc-hukuk-formu',
    lock: this.submission.kilit,
    empty: emptyLegal,
    toForm: legalToForm,
    rowOf: (c) => c.dosya,
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
      const label = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          cari: label,
          dosyaNo: f.dosyaNo ?? null,
          faturaNo: f.faturaNo ?? null,
          tarihBas: (f.tarihBas as DayText | undefined) ?? null,
          tarihBit: (f.tarihBit as DayText | undefined) ?? null,
          tur: f.tur ?? null,
          durum: f.durum ?? null,
          ara: f.ara ?? null,
        }),
      );
    });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected statusLabel(s: string): string {
    return (LEGAL_STATUSES as readonly string[]).includes(s)
      ? this.t(`crm.hukuk.durumlar.${s as LegalStatus}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        cariId: v.cari?.id ?? undefined,
        dosyaNo: v.dosyaNo?.trim() || undefined,
        faturaNo: v.faturaNo?.trim() || undefined,
        tarihBas: v.tarihBas ?? undefined,
        tarihBit: v.tarihBit ?? undefined,
        tur: (v.tur as LegalType | null) ?? undefined,
        durum: (v.durum as LegalStatus | null) ?? undefined,
        ara: v.ara?.trim() || undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected edit(row: LegalFile): void {
    void this.editor.edit(row, row.id);
  }

  protected remove(row: LegalFile): void {
    void this.editor.remove(
      row.id,
      this.t('crm.hukuk.silBaslik'),
      this.t('crm.hukuk.silMesaj', { no: row.dosyaNo }),
      this.t('crm.hukuk.silindi', { no: row.dosyaNo }),
    );
  }

  protected save(): void {
    const id = this.editor.recordId;
    if (this.editor.waiting) return;
    const card = this.editor.base();
    const body = legalRequest(
      this.form.getRawValue(),
      id === null || card === null ? null : { tarih: card.dosya.tarih, surum: card.surum },
    );
    this.submission.gonder(
      this.form,
      (key) =>
        id === null
          ? this.api.post<LegalFileCard>(LEGAL_FILES, body, { islemAnahtari: key })
          : this.api.put<LegalFileCard>(recordPath(LEGAL_FILES, id), body, { islemAnahtari: key }),
      {
        esleme: { cariId: 'cari' },
        basarili: (c) => {
          this.toast.basari(
            this.t(id === null ? 'crm.hukuk.eklendi' : 'crm.hukuk.kaydedildi', {
              no: c.dosya.dosyaNo,
            }),
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
