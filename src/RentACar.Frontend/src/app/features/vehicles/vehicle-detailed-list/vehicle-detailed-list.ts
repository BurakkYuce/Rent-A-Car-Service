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
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listeSorgusuUrlSenkronu } from '@core/veri/liste-sorgusu-url';
import { Alan } from '@shared/form/alan/alan';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';
import { Tablo } from '@shared/tablo/tablo';
import { TabloHucre } from '@shared/tablo/tablo-hucre';

import { suggestionList } from '../suggestions';
import {
  DETAILED_LIST,
  STATUS_BADGE,
  VEHICLE_STATUSES,
  vehicleStatus,
  type DetailedRow,
  type VehicleStatus,
} from '../vehicle-model';
import { DetailedListStore, secimSuggestionFetch } from '../vehicle.store';
import { detailedColumns } from './detailed-columns';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
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
    SayfaBandi,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Ikon,
    MetinGirdisi,
    Secim,
    Tablo,
    TabloHucre,
  ],
  providers: [FetchPolicy, DetailedListStore],
  templateUrl: './vehicle-detailed-list.html',
  styleUrl: '../vehicle-screens.scss',
})
export class VehicleDetailedList {
  protected readonly store = inject(DetailedListStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly router = inject(Router);
  private readonly t = ceviriFonksiyonu();

  protected readonly query = listeSorgusuUrlSenkronu(DETAILED_LIST);
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
    secimSuggestionFetch(this.api, 'sube'),
    () => this.session.izinVar('OperationsWrite'),
  );
  protected readonly summary = computed(() => {
    const l = this.store.list.veri();
    return l ? this.t('arac.detayli.sayac', { sayi: l.toplam }) : null;
  });

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: this.query.apiParametreleri,
      yukle: (p) => this.store.list.yukle(p),
      sifirla: () => this.store.list.sifirla(),
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
