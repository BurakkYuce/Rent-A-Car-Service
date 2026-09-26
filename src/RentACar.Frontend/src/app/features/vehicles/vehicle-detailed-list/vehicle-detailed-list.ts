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

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { Icon } from '@shared/ikon/icon';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

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
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { PlateChipComponent } from '@shared/plaka/plaka';

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
