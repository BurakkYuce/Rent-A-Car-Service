import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { PanelFinans as PanelFinansVerisi } from '@core/api/ui-tipleri';
import { BICIM_PIPELARI } from '@shared/bicim/bicim-pipe';

import { ayEtiketi, sayi } from './panel-modeli';

/**
 * Finans özeti + filo KPI (ömür boyu) + son 6 ay gelir mini-trendi. Yalnız sunucu `finans` bloğunu
 * gönderdiğinde (ViewReports) çizilir; kapı sunucuda, burada rol/izin denetimi YOK.
 */
@Component({
  selector: 'rc-panel-finans',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, ...BICIM_PIPELARI],
  templateUrl: './panel-finans.html',
  styleUrl: './panel-finans.scss',
})
export class PanelFinans {
  readonly finans = input.required<PanelFinansVerisi>();
  protected readonly sayi = sayi;

  /** Çubuk yüksekliği (en büyük ay = %100); negatif (iade netli) ay 0'da kalır. Tek seri, tek eksen. */
  protected readonly trend = computed(() => {
    const liste = this.finans().gelirTrendi;
    const degerler = liste.map((a) => sayi(a.gelir) ?? 0);
    const enBuyuk = Math.max(0, ...degerler);
    return liste.map((a, i) => {
      const gelir = degerler[i] ?? 0;
      return {
        ay: ayEtiketi(a.ayBas),
        gelir,
        yukseklik: enBuyuk > 0 ? Math.max(0, Math.round((100 * gelir) / enBuyuk)) : 0,
      };
    });
  });
}
