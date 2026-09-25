import { Injectable, inject } from '@angular/core';
import { tap } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { PanelOzetiYaniti } from '@core/api/ui-tipleri';
import { KabukSayaclari } from '@core/sayac/kabuk-sayaclari';
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
  private readonly sayaclar = inject(KabukSayaclari);

  readonly ozet = new TemelStore<PanelOzetiYaniti, null>(
    () =>
      this.api.get<PanelOzetiYaniti>('/api/ui/v1/panel/ozet').pipe(
        tap((o) =>
          this.sayaclar.yayinla({
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
