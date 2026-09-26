import { Injectable, inject } from '@angular/core';
import { tap } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { PanelSummaryResponse } from '@core/api/ui-tipleri';
import { ShellCounters } from '@core/sayac/shell-counters';
import { TemelStore } from '@core/veri/temel-store';

/**
 * Panel verisi: `GET /api/ui/v1/panel/ozet` TEK istek (KPI, vade, dönüş/çıkış kovaları, finans). Kapılar
 * sunucuda: finans bloğu yalnız ViewReports'ta, satırın `tahsilat` verisi yalnız FinanceWrite'ta dolu gelir.
 * Tazelemede önceki veri ekranda kalır (titreme yok); hata gelirse düşer ve hata bandı görünür.
 *
 * Aynı yanıt kenar çubuğunun kayıtlı görünüm sayaçlarını besler (`KabukSayaclari`) — kabuk ayrı istek atmaz.
 */
@Injectable()
export class PanelStore {
  private readonly api = inject(ApiIstemcisi);
  private readonly counters = inject(ShellCounters);

  readonly ozet = new TemelStore<PanelSummaryResponse, null>(
    () =>
      this.api.get<PanelSummaryResponse>('/api/ui/v1/panel/ozet').pipe(
        tap((o) =>
          this.counters.publish({
            kirada: Number(o.kpi.kirada),
            geciken: o.donusler.gecikmis.length,
            bugunCikan: o.cikislar.bugun.length,
            bugunDonecek: o.donusler.bugun.length,
          }),
        ),
      ),
    { oncekiVeriyiKoru: true },
  );
}
