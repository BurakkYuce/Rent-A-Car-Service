import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { PanelFinance as PanelFinansVerisi } from '@core/api/ui-tipleri';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';

import { monthLabel, count } from './panel-modeli';

/**
 * Finans özeti + filo KPI (ömür boyu) + son 6 ay gelir mini-trendi. Yalnız sunucu `finans` bloğunu
 * gönderdiğinde (ViewReports) çizilir; kapı sunucuda, burada rol/izin denetimi YOK.
 */
@Component({
  selector: 'rc-panel-finans',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, ...FORMAT_PIPES],
  templateUrl: './panel-finance.html',
  styleUrl: './panel-finance.scss',
})
export class PanelFinance {
  readonly finans = input.required<PanelFinansVerisi>();
  protected readonly sayi = count;

  /** Çubuk yüksekliği (en büyük ay = %100); negatif (iade netli) ay 0'da kalır. Tek seri, tek eksen. */
  protected readonly trend = computed(() => {
    const list = this.finans().gelirTrendi;
    const values = list.map((a) => count(a.gelir) ?? 0);
    const largest = Math.max(0, ...values);
    return list.map((a, i) => {
      const revenue = values[i] ?? 0;
      return {
        ay: monthLabel(a.ayBas),
        gelir: revenue,
        yukseklik: largest > 0 ? Math.max(0, Math.round((100 * revenue) / largest)) : 0,
      };
    });
  });
}
