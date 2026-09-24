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

import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { CustomerLabels } from '@features/vehicle-finance/labels';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { Ikon } from '@shared/ikon/ikon';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Tablo } from '@shared/tablo/tablo';

import { invoiceLineColumns } from '../document-columns';
import {
  INVOICE_LINE_LIST,
  type InvoiceLineRow,
  invoiceLineExportParameters,
} from '../document-model';
import { InvoiceLineStore } from '../document.store';

/**
 * Fatura detay listesi (`/app/faturalar/detay-listesi`) — Blazor `InvoiceLineList.razor` paritesi: fatura SATIRI
 * düzeyinde liste (kesildiği andaki değerler), süzgeç ve dışa aktarma (`fatura-detaylari`, süzgeç aynen). Salt okuma,
 * ViewReports. İşaretli TL toplamı sunucudan (iadede eksi); istemci toplamaz.
 */
@Component({
  selector: 'rc-invoice-lines',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    AramaSecim,
    Ikon,
    MetinGirdisi,
    OnayKutusu,
    Tablo,
    TarihSecici,
  ],
  providers: [FetchPolicy, InvoiceLineStore, CustomerLabels],
  templateUrl: './invoice-lines.html',
  styleUrl: '../finance-documents.scss',
})
export class InvoiceLines {
  protected readonly store = inject(InvoiceLineStore);
  private readonly labels = inject(CustomerLabels);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(INVOICE_LINE_LIST);
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
  protected readonly customers = sunucuSecimKaynagi('musteri');

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
    policy.baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
    });
    effect(() => {
      const f = this.query.sorgu().filtreler;
      const cari = this.labels.label(f.cariId);
      untracked(() =>
        this.filterForm.reset({
          q: f.q ?? null,
          cari,
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
