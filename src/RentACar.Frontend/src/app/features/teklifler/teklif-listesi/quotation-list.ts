import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { translationFunction } from '@core/i18n/ceviri';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { Icon } from '@shared/ikon/icon';
import { CollapsibleFilter } from '@shared/katlanir-filtre/collapsible-filter';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { QuotationActions } from '../quotation-actions';
import {
  QUOTATION_STATUSES,
  isQuotationOpen,
  isQuotationStatus,
  quotationBadge,
  type QuotationStatus,
  type QuotationListRow,
} from '../teklif-modeli';
import { QUOTATION_LIST, QuotationListStore, quotationColumns } from './quotation-list.store';

/**
 * Teklif listesi (`/app/teklifler`) — Blazor `QuotationList.razor` paritesi (F5.2a): sütunlar, satır eylemleri
 * Gönder (Taslak) / Kabul → Rezervasyon / Reddet (Taslak ya da Gönderildi); Kabul edilmiş teklifin durumu oluşan
 * rezervasyona bağlanır (Blazor listeye gidiyordu; SPA doğrudan kaydı açar). Yeni teklif ayrı formda.
 */
@Component({
  selector: 'rc-teklif-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Icon,
    CollapsibleFilter,
    Selection,
    Table,
    TableCell,
  ],
  providers: [FetchPolicy, QuotationListStore, QuotationActions],
  templateUrl: './quotation-list.html',
  styleUrl: '../../rezervasyonlar/rezervasyon-listesi/reservation-list.scss',
})
export class QuotationList {
  protected readonly store = inject(QuotationListStore);
  protected readonly islemler = inject(QuotationActions);
  private readonly router = inject(Router);
  private readonly t = translationFunction();

  protected readonly liste = listQueryUrlSync(QUOTATION_LIST);
  protected readonly columns = quotationColumns(this.t);
  protected readonly defaultSort = QUOTATION_LIST.varsayilanSirala;
  protected readonly identity = (r: QuotationListRow) => r.id;
  protected readonly filterOpen = signal(false);

  protected readonly filterForm = new FormGroup({
    durum: new FormControl<QuotationStatus | null>(null),
  });
  protected readonly statusOptions: readonly SecenekOgesi<QuotationStatus>[] =
    QUOTATION_STATUSES.map((d) => ({ deger: d, etiket: this.t(`teklif.durumlar.${d}`) }));

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      untracked(() => this.filterForm.reset({ durum: f.durum ?? null }));
    });
  }

  protected filter(): void {
    void this.liste.degistir({
      filtreler: { durum: this.filterForm.getRawValue().durum ?? undefined },
    });
  }

  protected clear(): void {
    void this.liste.sifirla();
  }

  protected openRow(row: QuotationListRow): void {
    void this.router.navigate(['/teklifler', row.id]);
  }

  protected rozet(status: string): string {
    return quotationBadge(status);
  }

  protected statusLabel(status: string): string {
    return isQuotationStatus(status) ? this.t(`teklif.durumlar.${status}`) : status;
  }

  protected acik(row: QuotationListRow): boolean {
    return isQuotationOpen(row.durum);
  }

  private readonly yenile = () => this.store.liste.yenile();

  protected gonder(row: QuotationListRow): void {
    this.islemler.gonder({ id: row.id, no: row.no }, this.yenile);
  }

  protected kabul(row: QuotationListRow): void {
    void this.islemler.kabul({ id: row.id, no: row.no }, this.yenile);
  }

  protected reddet(row: QuotationListRow): void {
    void this.islemler.reddet({ id: row.id, no: row.no }, this.yenile);
  }
}
