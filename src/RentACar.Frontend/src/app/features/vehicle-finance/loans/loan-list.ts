import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { toNumber } from '@features/vehicles/vehicle-model';
import { MoneyPipe, DatePipe } from '@shared/bicim/bicim-pipe';
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
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { loanColumns } from '../finance-columns';
import {
  LOAN_EXPORT_NAMES,
  LOAN_LIST,
  LOAN_STATUSES,
  type BulkCancelResponse,
  type LoanCreated,
  type LoanRow,
  exportParameters,
} from '../finance-model';
import { LOANS, LoanListStore } from '../finance.store';
import { CustomerLabels } from '../labels';
import { activeSelection, emptyLoanForm, loanRequest, type LoanFormValue } from './loan-form-model';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

type LoanStatus = (typeof LOAN_STATUSES)[number];

/**
 * Araç kredisi takibi (`/app/arac-kredi`) — Blazor `AracKrediList.razor` paritesi: süzgeçler, 5 özet kart (filtreli
 * küme, deftere yazmaz), liste + dışa aktarma, "Yeni Kredi" (OperationsWrite — sunucu kuralı), seçili kredilerin
 * taksit iptali (OperationsDelete, onaylı). "Taksit Öde" ve iptal kredi kaydında (`/arac-kredi/:id`).
 */
@Component({
  selector: 'rc-loan-list',
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
    MoneyPipe,
    NumberInput,
    Selection,
    Table,
    TableCell,
    DatePipe,
    DatePicker,
  ],
  providers: [FetchPolicy, LoanListStore, CustomerLabels],
  templateUrl: './loan-list.html',
  styleUrl: '../vehicle-finance.scss',
})
export class LoanList implements UnsavedChangesOwner {
  protected readonly store = inject(LoanListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly labels = inject(CustomerLabels);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(LOAN_LIST);
  protected readonly columns = loanColumns(this.t);
  protected readonly rowId = (r: LoanRow) => r.id;
  protected readonly num = toNumber;
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly vehicles = serverSelectionSource('arac');

  protected readonly canCreate = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canCancel = computed(() => this.session.izinVar('OperationsDelete'));
  protected readonly canPay = computed(() => this.session.izinVar('FinanceWrite'));
  protected readonly selection = signal<readonly string[]>([]);
  protected readonly cancelling = signal(false);
  protected readonly createOpen = signal(false);

  protected readonly statusOptions: readonly SecenekOgesi<LoanStatus>[] = LOAN_STATUSES.map(
    (s) => ({
      deger: s,
      etiket: this.t(`aracFinans.kredi.durumlar.${s}`),
    }),
  );

  protected readonly filterForm = new FormGroup({
    cari: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null),
    dosyaNo: new FormControl<string | null>(null),
    durum: new FormControl<LoanStatus | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });

  protected readonly form = new FormGroup({
    bankaAdi: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(200),
    ]),
    cari: new FormControl<SecimSecenegi | null>(null),
    dosyaNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    arac: new FormControl<SecimSecenegi | null>(null),
    krediTutari: new FormControl<string | null>(null, Validators.required),
    faizOran: new FormControl<number | null>(0),
    taksitSayisi: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.max(360),
    ]),
    baslangic: new FormControl<string | null>(null),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly submission = formSubmission();

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/arac-kredileri',
    parametreler: exportParameters(this.query.sorgu().filtreler, LOAN_EXPORT_NAMES),
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => {
        this.store.list.yukle(p);
        this.store.board.yukle(p);
      },
      sifirla: () => {
        this.store.list.reset();
        this.store.board.reset();
      },
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const label = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          cari: label,
          plaka: f.plaka ?? null,
          dosyaNo: f.dosyaNo ?? null,
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    this.form.reset({ ...emptyLoanForm() });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.createOpen() && this.form.dirty;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        cariId: v.cari?.id ?? undefined,
        plaka: v.plaka ?? undefined,
        dosyaNo: v.dosyaNo ?? undefined,
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected openRow(row: LoanRow): void {
    void this.router.navigate(['/arac-kredi', row.id]);
  }

  protected statusLabel(s: string): string {
    return (LOAN_STATUSES as readonly string[]).includes(s)
      ? this.t(`aracFinans.kredi.durumlar.${s as LoanStatus}`)
      : s;
  }

  protected toggleCreate(): void {
    this.createOpen.update((v) => !v);
  }

  protected create(): void {
    if (!this.canCreate()) return;
    const body = loanRequest(this.form.getRawValue() as LoanFormValue);
    this.submission.gonder(
      this.form,
      (key) => this.api.post<LoanCreated>(LOANS, body, { islemAnahtari: key }),
      {
        esleme: { cariId: 'cari', vehicleId: 'arac', baslangicTarihi: 'baslangic' },
        basarili: (r) => {
          this.toast.basari(this.t('aracFinans.kredi.olusturuldu', { no: r.no }));
          this.form.reset({ ...emptyLoanForm() });
          this.createOpen.set(false);
          this.refresh();
        },
        hata: (h) => {
          // Aynı anahtarla yazılmış kredi (kaybolan yanıt): form temizlenir, liste yenilenir; yeniden gönderim yok.
          if (h.kod === 'mukerrer') {
            if (h.mevcut?.ayniIcerik) this.form.reset({ ...emptyLoanForm() });
            this.refresh();
          }
        },
      },
    );
  }

  protected async bulkCancel(): Promise<void> {
    if (this.cancelling()) return;
    const ids = activeSelection(this.selection(), this.store.list.veri()?.kayitlar ?? []);
    if (ids.length === 0) {
      this.toast.uyari(this.t('aracFinans.kredi.seciliYok'));
      return;
    }
    const yes = await this.confirm.ask({
      baslik: this.t('aracFinans.kredi.topluIptalBaslik'),
      mesaj: this.t('aracFinans.kredi.topluIptalMesaj', { sayi: ids.length }),
      onayEtiketi: this.t('aracFinans.kredi.topluIptal'),
      tehlikeli: true,
    });
    if (!yes || this.cancelling()) return;
    this.cancelling.set(true);
    this.api
      .post<BulkCancelResponse>(`${LOANS}/toplu-iptal`, { ids })
      .pipe(
        finalize(() => this.cancelling.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => {
          this.toast.basari(this.t('aracFinans.kredi.topluIptalEdildi', { sayi: r.iptalEdilen }));
          this.selection.set([]);
          this.refresh();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.refresh();
        },
      });
  }

  private refresh(): void {
    this.store.list.yenile();
    this.store.board.yenile();
  }
}
