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

import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
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
import { PlateChipComponent } from '@shared/plaka/plaka';
import { exportUrl, type DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import {
  FLEET_STATUSES,
  FLEET_LIST,
  type FleetStatus,
  type FleetListRow,
  fleetStatus,
} from './filo-modeli';
import { fleetColumns } from './fleet-columns';
import { FleetListStore } from './filo.store';

export const STATUS_BADGE: Readonly<Record<FleetStatus, string>> = {
  Aktif: 'rc-rozet--basari',
  Tamamlandi: 'rc-rozet--notr',
  Iptal: 'rc-rozet--hata',
};

/**
 * Filo / uzun dönem kiralama listesi (`/app/filo-kiralama`) — Blazor `FiloKiralamaList.razor` paritesi:
 * FAZ-21 arama paneli (müşteri, plaka, serbest arama, durum, başlangıç aralığı), "N sözleşme", sunucu
 * dışa aktarması (Excel/CSV/PDF, ViewReports — Blazor ucu süzgeç almaz), sütunlar. Satır → sözleşme
 * detayı (künye, taksit planı, tamamla/iptal). Bu ekran deftere/bakiyeye yazmaz.
 */
@Component({
  selector: 'rc-filo-listesi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    SearchSelection,
    Icon,
    TextInput,
    Selection,
    Table,
    TableCell,
    DatePicker,
    PlateChipComponent,
    PageBand,
  ],
  providers: [FetchPolicy, FleetListStore],
  templateUrl: './fleet-list.html',
  styleUrl: './filo.scss',
})
export class FleetList {
  protected readonly store = inject(FleetListStore);
  private readonly oturum = inject(SessionService);
  private readonly router = inject(Router);
  private readonly t = translationFunction();

  protected readonly liste = listQueryUrlSync(FLEET_LIST);
  protected readonly columns = fleetColumns(this.t);
  protected readonly identity = (r: FleetListRow) => r.id;
  protected readonly customers = serverSelectionSource('musteri');
  private readonly customerLabels = new Map<string, string>();

  protected readonly filterForm = new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null),
    plaka: new FormControl<string | null>(null),
    ara: new FormControl<string | null>(null),
    durum: new FormControl<FleetStatus | null>(null),
    bas: new FormControl<string | null>(null),
    bit: new FormControl<string | null>(null),
  });
  protected readonly statusOptions: readonly SecenekOgesi<FleetStatus>[] = FLEET_STATUSES.map(
    (d) => ({ deger: d, etiket: this.t(`filoKiralama.durumlar.${d}`) }),
  );

  /** Blazor liste dışa aktarması (ViewReports); uç ekran süzgeçlerini okumaz — tüm sözleşmeler. */
  protected readonly exportUrl = exportUrl;
  protected readonly exportItem = computed<DisaAktarma | null>(() =>
    this.oturum.izinVar('ViewReports')
      ? { yol: '/listeler/export/filo-kiralama', bicimler: ['excel', 'csv', 'pdf'] }
      : null,
  );

  protected readonly ozet = computed(() => {
    const l = this.store.liste.veri();
    return l ? this.t('filoKiralama.ozet', { toplam: l.toplam }) : null;
  });

  constructor() {
    const policy = inject(FetchPolicy);
    policy.connect({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.reset(),
      sekmeyeDonunce: 'yenile',
    });
    policy.connect({
      parametre: computed(() => this.liste.sorgu().filtreler.musteriId ?? null),
      yukle: (id) => {
        if (id !== null && !this.customerLabels.has(id)) this.store.musteri.yukle(id);
      },
    });
    effect(() => {
      const f = this.liste.sorgu().filtreler;
      const resolved = this.store.musteri.veri();
      if (resolved) this.customerLabels.set(resolved.id, resolved.etiket);
      untracked(() =>
        this.filterForm.reset({
          musteri:
            f.musteriId === undefined
              ? null
              : {
                  id: f.musteriId,
                  etiket:
                    this.customerLabels.get(f.musteriId) ?? this.t('filoKiralama.seciliMusteri'),
                },
          plaka: f.plaka ?? null,
          ara: f.ara ?? null,
          durum: f.durum ?? null,
          bas: f.bas ?? null,
          bit: f.bit ?? null,
        }),
      );
    });
  }

  protected filter(): void {
    const v = this.filterForm.getRawValue();
    if (v.musteri) this.customerLabels.set(v.musteri.id, v.musteri.etiket);
    void this.liste.degistir({
      filtreler: {
        musteriId: v.musteri?.id ?? undefined,
        plaka: v.plaka ?? undefined,
        ara: v.ara ?? undefined,
        durum: v.durum ?? undefined,
        bas: v.bas ?? undefined,
        bit: v.bit ?? undefined,
      },
    });
  }

  protected clear(): void {
    void this.liste.sifirla();
  }

  protected openRow(row: FleetListRow): void {
    void this.router.navigate(['/filo-kiralama', row.id]);
  }

  protected rozet(status: string): string {
    const d = fleetStatus(status);
    return `rc-rozet ${d ? STATUS_BADGE[d] : ''}`;
  }

  protected statusLabel(status: string): string {
    const d = fleetStatus(status);
    return d ? this.t(`filoKiralama.durumlar.${d}`) : status;
  }
}
