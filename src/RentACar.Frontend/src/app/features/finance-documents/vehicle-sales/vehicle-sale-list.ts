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
import { TranslocoPipe } from '@jsverse/transloco';

import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { PendingMoneyAttempts } from '@core/form/money-attempts';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { CustomerLabels } from '@features/vehicle-finance/labels';
import { Alan } from '@shared/form/alan/alan';
import { SearchSelection } from '@shared/form/arama-secim/search-selection';
import {
  type SecimSecenegi,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { saleColumns } from '../document-columns';
import { SALE_LIST, SALE_STATUSES, type VehicleSaleRow } from '../document-model';
import { BranchNames, SaleStore } from '../document.store';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';

import { SaleCreateForm } from './sale-create-form';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { PlateChipComponent } from '@shared/plaka/plaka';

type SaleStatus = (typeof SALE_STATUSES)[number];

/**
 * Araç satışları (`/app/satislar`) — Blazor `VehicleSaleList.razor` paritesi: süzgeç (plaka, alıcı, durum, devir, ofis,
 * tarih), yeni satış (FinanceWrite), liste (+ dışa aktarma). Okuma FinanceWrite ∨ ViewReports ∨ OperationsWrite; satış
 * aracın şubesi kapsamında (sunucu). Tutarlar SUNUCUDAN.
 */
@Component({
  selector: 'rc-vehicle-sale-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    SearchSelection,
    Icon,
    TextInput,
    SaleCreateForm,
    Selection,
    Table,
    TableCell,
    DatePicker,
  ],
  providers: [FetchPolicy, SaleStore, BranchNames, CustomerLabels, PendingMoneyAttempts],
  templateUrl: './vehicle-sale-list.html',
  styleUrl: '../finance-documents.scss',
})
export class VehicleSaleList implements UnsavedChangesOwner {
  protected readonly store = inject(SaleStore);
  protected readonly branches = inject(BranchNames);
  private readonly session = inject(SessionService);
  private readonly labels = inject(CustomerLabels);
  protected readonly pending = inject(PendingMoneyAttempts);
  private readonly confirm = inject(ConfirmService);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(SALE_LIST);
  protected readonly columns = saleColumns(this.t);
  protected readonly rowId = (r: VehicleSaleRow) => r.id;
  protected readonly customers = serverSelectionSource('musteri');
  protected readonly canWrite = computed(() => this.session.izinVar('FinanceWrite'));
  private readonly createToggle = signal<boolean | null>(null);
  protected readonly createOpen = computed(
    () => this.createToggle() ?? this.store.list.veri()?.toplam === 0,
  );
  private formDirty = false;

  protected readonly statusOptions: readonly SecenekOgesi<SaleStatus>[] = SALE_STATUSES.map(
    (s) => ({
      deger: s,
      etiket: this.t(`finansBelge.satis.durumlar.${s}`),
    }),
  );
  protected readonly transferOptions: readonly SecenekOgesi<'true' | 'false'>[] = [
    { deger: 'true', etiket: this.t('finansBelge.satis.verildi') },
    { deger: 'false', etiket: this.t('finansBelge.satis.isaretlenmemis') },
  ];

  protected readonly filterForm = new FormGroup({
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    alici: new FormControl<SecimSecenegi | null>(null),
    durum: new FormControl<SaleStatus | null>(null),
    satisiVerildi: new FormControl<'true' | 'false' | null>(null),
    ofis: new FormControl<string | null>(null, Validators.maxLength(128)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/arac-satislari',
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const recipient = this.labels.label(f.aliciCariId);
      untracked(() =>
        this.filterForm.reset({
          plaka: f.plaka ?? null,
          alici: recipient,
          durum: f.durum ?? null,
          satisiVerildi: f.satisiVerildi ?? null,
          ofis: f.ofis ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
    effect(() => {
      if (this.canWrite() && this.createOpen()) untracked(() => this.branches.list.yukle());
    });
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.formDirty || this.pending.count() > 0;
  }

  protected dirtyChanged(dirty: boolean): void {
    this.formDirty = dirty;
  }

  protected statusLabel(s: string): string {
    return (SALE_STATUSES as readonly string[]).includes(s)
      ? this.t(`finansBelge.satis.durumlar.${s as SaleStatus}`)
      : s;
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.alici);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        plaka: v.plaka?.trim() || undefined,
        aliciCariId: v.alici?.id ?? undefined,
        durum: v.durum ?? undefined,
        satisiVerildi: v.satisiVerildi ?? undefined,
        ofis: v.ofis?.trim() || undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  /** Uçuştaki satış varken form kapanmaz; kirli ya da sonucu bilinmeyen form onaysız kapanmaz (r300b N1/N4). */
  protected async toggleCreate(): Promise<void> {
    if (this.pending.inFlight()) return;
    if (this.createOpen() && (this.formDirty || this.pending.get('yeni-satis') !== undefined)) {
      const yes = await this.confirm.ask({
        baslik: this.t('finansBelge.ayrilBaslik'),
        mesaj: this.t('finansBelge.ayrilMesaj'),
      });
      if (!yes) return;
      this.formDirty = false;
    }
    this.createToggle.set(!this.createOpen());
  }

  protected saved(): void {
    this.store.list.yenile();
  }
}
