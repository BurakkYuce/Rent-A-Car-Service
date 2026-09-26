import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { translationFunction } from '@core/i18n/ceviri';
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
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { DatePicker } from '@shared/form/tarih/date-picker';
import { Icon } from '@shared/ikon/icon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';

import { invoiceLineColumns } from '../document-columns';
import {
  INVOICE_LINE_LIST,
  type InvoiceLineRow,
  invoiceLineExportParameters,
} from '../document-model';
import { InvoiceLineStore } from '../document.store';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { TableCell } from '@shared/tablo/table-cell';

/**
 * Fatura detay listesi (`/app/faturalar/detay-listesi`) — Blazor `InvoiceLineList.razor` paritesi: fatura SATIRI
 * düzeyinde liste (kesildiği andaki değerler), süzgeç ve dışa aktarma (`fatura-detaylari`, süzgeç aynen). Salt okuma,
 * ViewReports. İşaretli TL toplamı sunucudan (iadede eksi); istemci toplamaz.
 */
@Component({
  selector: 'rc-invoice-lines',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    TableCell,
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    Icon,
    TextInput,
    Checkbox,
    Table,
    DatePicker,
  ],
  providers: [FetchPolicy, InvoiceLineStore, CustomerLabels],
  templateUrl: './invoice-lines.html',
  styleUrl: '../finance-documents.scss',
})
export class InvoiceLines {
  protected readonly store = inject(InvoiceLineStore);
  private readonly labels = inject(CustomerLabels);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(INVOICE_LINE_LIST);
  protected readonly columns = invoiceLineColumns(this.t);
  /** Satırın sunucu kimliği yok (aynı faturada birebir aynı iki kalem olabilir): nesne başına yerel sıra no. */
  private readonly rowIds = new WeakMap<InvoiceLineRow, string>();
  private rowSeq = 0;
  protected readonly rowId = (r: InvoiceLineRow): string => {
    let id = this.rowIds.get(r);
    if (id === undefined) {
      id = `${r.faturaId}#${++this.rowSeq}`;
      this.rowIds.set(r, id);
    }
    return id;
  };
  protected readonly customers = serverSelectionSource('musteri');

  protected readonly filterForm = new FormGroup({
    q: new FormControl<string | null>(null, Validators.maxLength(128)),
    cari: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null, Validators.maxLength(16)),
    ofis: new FormControl<string | null>(null, Validators.maxLength(128)),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
    iptalleriGizle: new FormControl<boolean | null>(false),
  });

  protected readonly export = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/fatura-detaylari',
    parametreler: invoiceLineExportParameters(this.query.sorgu().filtreler),
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.reset(),
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const account = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          cari: account,
          plaka: f.plaka ?? null,
          ofis: f.ofis ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
          iptalleriGizle: f.iptalleriGizle ?? false,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    this.labels.remember(v.cari);
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        q: v.q?.trim() || undefined,
        cariId: v.cari?.id ?? undefined,
        plaka: v.plaka?.trim() || undefined,
        ofis: v.ofis?.trim() || undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
        iptalleriGizle: v.iptalleriGizle ? true : undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }
}
