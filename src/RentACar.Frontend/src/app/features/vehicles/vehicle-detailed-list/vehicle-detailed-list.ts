import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-client';
import { translationFunction } from '@core/i18n/translation';
import { SessionService } from '@core/session/session-service';
import { FetchPolicy } from '@core/data/fetch-policy';
import { listQueryUrlSync } from '@core/data/list-query-url';
import { Alan } from '@shared/form/field/field';
import { TextInput } from '@shared/form/controls/text-input';
import type { SecenekOgesi } from '@shared/form/controls/option';
import { Selection } from '@shared/form/controls/selection';
import { Icon } from '@shared/icon/icon';
import { Table } from '@shared/table/table';
import { TableCell } from '@shared/table/table-cell';
import type { DisaAktarma } from '@shared/table/export';

import { suggestionList } from '../suggestions';
import {
  DETAILED_LIST,
  STATUS_BADGE,
  VEHICLE_STATUSES,
  vehicleStatus,
  type DetailedRow,
  type VehicleStatus,
} from '../vehicle-model';
import { DetailedListStore, selectionSuggestionFetch } from '../vehicle.store';
import { detailedColumns } from './detailed-columns';
import { PageBand } from '../../../shell/page-band/page-band';
import { FilterPanelComponent } from '@shared/filter-panel/filter-panel';
import { PlateChipComponent } from '@shared/plate/plate';

/**
 * Detaylı araç listesi (`/app/araclar/detayli`, ViewReports) — Blazor `VehicleDetayList.razor` paritesi:
 * ara (plaka/marka/tip/belge no), şube, durum; 49 sütun (kimlik, alım, kredi, muayene, sigorta, aktif kira
 * CANLI, "not" anlık görüntüleri, satış, özel kodlar). Uç şube kapsamını uygular; müşteri adı KVKK kuralıyla.
 */
@Component({
  selector: 'rc-vehicle-detailed-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    FilterPanelComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Icon,
    TextInput,
    Selection,
    Table,
    TableCell,
  ],
  providers: [FetchPolicy, DetailedListStore],
  templateUrl: './vehicle-detailed-list.html',
  styleUrl: '../vehicle-screens.scss',
})
export class VehicleDetailedList {
  protected readonly store = inject(DetailedListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly t = translationFunction();

  protected readonly query = listQueryUrlSync(DETAILED_LIST);
  protected readonly columns = detailedColumns(this.t);
  protected readonly rowId = (r: DetailedRow) => r.id;
  /**
   * Kabul testi: detaylı listede dışa aktarma yoktu. Sunucunun araç export'u zaten geniş sütunlu (kimlik, alım, belge,
   * bakım, özel kodlar — 58 sütun; `ListExportCatalog.Vehicles`); sayfa ViewReports ister, uç da (ayrı izin yok).
   */
  protected readonly export: DisaAktarma = {
    yol: '/listeler/export/araclar',
    bicimler: ['excel', 'csv', 'pdf'],
  };

  protected readonly filterForm = new FormGroup({
    ara: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
    durum: new FormControl<VehicleStatus | null>(null),
  });
  protected readonly statusOptions: readonly SecenekOgesi<VehicleStatus>[] = VEHICLE_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`arac.durumlar.${s}`) }),
  );
  protected readonly branchSuggestions = suggestionList(
    this.filterForm.controls.sube,
    selectionSuggestionFetch(this.api, 'sube'),
    () => this.session.izinVar('OperationsWrite'),
  );
  protected readonly summary = computed(() => {
    const l = this.store.list.veri();
    return l ? this.t('arac.detayli.sayac', { sayi: l.toplam }) : null;
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
        this.filterForm.reset({ ara: f.ara ?? null, sube: f.sube ?? null, durum: f.durum ?? null }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    void this.query.degistir({
      sayfa: 1,
      filtreler: {
        ara: v.ara ?? undefined,
        sube: v.sube ?? undefined,
        durum: v.durum ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.query.sifirla();
  }

  protected openRow(row: DetailedRow): void {
    void this.router.navigate(['/araclar', row.id, 'detay']);
  }

  protected badge(status: string): string {
    const s = vehicleStatus(status);
    return `rc-rozet ${s ? STATUS_BADGE[s] : ''}`;
  }

  protected statusLabel(status: string): string {
    const s = vehicleStatus(status);
    return s ? this.t(`arac.durumlar.${s}`) : status;
  }
}
