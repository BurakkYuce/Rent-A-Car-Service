import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';

import type { KpiKarti } from './panel-modeli';

/**
 * Panel KPI şeridi: dört filo kartı (toplamdan yüzde çubuğu) ve her birinin altında vade kademesi /
 * uyarı kutuları (Blazor `Home.razor` + `VadeTier`). Kutular Blazor ekranlarına tam sayfa bağlanır.
 */
@Component({
  selector: 'rc-panel-kpi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, ...BICIM_PIPELARI],
  templateUrl: './panel-kpi.html',
  styleUrl: './panel-kpi.scss',
})
export class PanelKpi {
  readonly kartlar = input.required<readonly KpiKarti[]>();
}
