import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { FetchPolicy } from '@core/veri/fetch-policy';
import { listQueryUrlSync } from '@core/veri/liste-sorgusu-url';
import type { DisaAktarma } from '@shared/tablo/disa-aktarma';
import { Table } from '@shared/tablo/table';
import { TableCell } from '@shared/tablo/table-cell';

import { VEHICLE_COLUMNS, SCENARIOS, type AracSatiri, type Scenario } from './arac-verisi';
import { VEHICLE_SHOWCASE_LIST, TableShowcaseStore } from './table-showcase.store';

// Yol v2 §1.2 filo durum sözlüğü: kirada yeşil, boşta nötr, serviste sarı, rezerve lacivert.
const STATUS_BADGE: Readonly<Record<string, string>> = {
  Müsait: 'rc-rozet--notr',
  Kirada: 'rc-rozet--basari',
  Serviste: 'rc-rozet--uyari',
  Rezerve: 'rc-rozet--vurgu',
};

/**
 * Tablo motoru vitrini (`/app/vitrin/tablo`): 49 sütun × 5.000 kayıt, sunucu sayfalama/sıralama
 * (F3.4 URL senkronu + `TemelStore` + `FetchPolicy`), kullanıcı düzeni ve dört durum. Özellik
 * ekranları için örnek kablolama budur.
 */
@Component({
  selector: 'rc-tablo-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Table, TableCell],
  providers: [FetchPolicy, TableShowcaseStore],
  templateUrl: './table-showcase.html',
  styleUrl: './table-showcase.scss',
})
export class TableShowcase {
  protected readonly store = inject(TableShowcaseStore);
  protected readonly liste = listQueryUrlSync(VEHICLE_SHOWCASE_LIST);
  protected readonly columns = VEHICLE_COLUMNS;
  protected readonly defaultSort = VEHICLE_SHOWCASE_LIST.varsayilanSirala;
  protected readonly scenarios = SCENARIOS;
  protected readonly identity = (a: AracSatiri) => a.id;
  /** Vitrin: "bugünün işi" krem satır vurgusu (her 7. araç; gerçek ekranda bugün dönen/çıkan kira). */
  protected readonly rowClass = (a: AracSatiri) =>
    Number(a.id.slice(-5)) % 7 === 0 ? 'rc-satir-bugun' : null;
  protected readonly secim = signal<readonly string[]>([]);
  protected readonly opened = signal<AracSatiri | null>(null);

  protected readonly scenario = computed<Scenario>(
    () => this.liste.sorgu().filtreler.senaryo ?? 'normal',
  );

  /** Dışa aktarma sunucu ucuyla: ekrandaki filtre + sıralama taşınır, sayfa taşınmaz. */
  protected readonly exportItem = computed<DisaAktarma>(() => ({
    yol: '/listeler/export/araclar',
    parametreler: this.liste.apiParametreleri(),
    bicimler: ['excel', 'csv', 'pdf'],
  }));

  constructor() {
    inject(FetchPolicy).connect({
      parametre: this.liste.apiParametreleri,
      yukle: (p) => this.store.liste.yukle(p),
      sifirla: () => this.store.liste.reset(),
    });
  }

  protected selectScenario(scenario: Scenario): void {
    void this.liste.degistir({
      filtreler: { senaryo: scenario === 'normal' ? undefined : scenario },
    });
  }

  protected rozet(status: unknown): string {
    return `rc-rozet ${STATUS_BADGE[String(status)] ?? ''}`;
  }
}
