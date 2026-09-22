import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { PanelOzetiYaniti } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';

/**
 * Panel verisi: `GET /api/ui/v1/panel/ozet` TEK istek (KPI, vade, dönüş/çıkış kovaları, finans). Kapılar
 * sunucuda: finans bloğu yalnız ViewReports'ta, satırın `tahsilat` verisi yalnız FinanceWrite'ta dolu gelir.
 * Tazelemede önceki veri ekranda kalır (titreme yok); hata gelirse düşer ve hata bandı görünür.
 */
@Injectable()
export class PanelStore {
  private readonly api = inject(ApiIstemcisi);

  readonly ozet = new TemelStore<PanelOzetiYaniti, null>(
    () => this.api.get<PanelOzetiYaniti>('/api/ui/v1/panel/ozet'),
    { oncekiVeriyiKoru: true },
  );
}
