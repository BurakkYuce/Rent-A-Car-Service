import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree, type CanMatchFn, type Route } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Izin } from '@core/oturum/oturum-tipleri';

import { SAYFALAR } from './sayfalar';

/**
 * F5.3 parite çiti — sayfa izni. Beklenen liste ELLE yazıldı (Blazor razor'larının `@attribute`'u:
 * ReservationList `[Authorize]` + uç grubu OperationsWrite; QuotationList, ReservationCalendar,
 * MusaitlikArama, RezSartList, FiloKiralamaList `izin:OperationsWrite`). Rota tablosundan TÜRETİLMEZ:
 * bir rota guard'ını kaybederse ya da yeni F5 rotası guard'sız eklenirse test kırılır.
 */
const F5_ROTALARI_OPERATIONS_WRITE = [
  'rezervasyonlar',
  'rezervasyonlar/yeni',
  'rezervasyonlar/:id',
  'teklifler',
  'teklifler/yeni',
  'teklifler/:id',
  'takvim',
  'musaitlik',
  'rez-sartlari',
  'filo-kiralama',
  'filo-kiralama/yeni',
  'filo-kiralama/:id',
] as const;

describe('F5 rotaları: Blazor sayfa izniyle aynı kapı (OperationsWrite)', () => {
  let izinler: readonly Izin[] = [];
  const sahteOturum = {
    ilkYukleme: () => Promise.resolve(null),
    girisYapildi: () => true,
    ben: () => ({ pilot: true }),
    izinVar: (izin: Izin) => izinler.includes(izin),
  };

  beforeEach(async () => {
    izinler = [];
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([]),
        { provide: OturumServisi, useValue: sahteOturum },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  function rota(yol: string): Route {
    const bulunan = SAYFALAR.filter((r) => r.path === yol);
    expect(bulunan, `rota tekil olmalı: ${yol}`).toHaveLength(1);
    return bulunan[0]!;
  }

  async function sonuc(r: Route): Promise<unknown[]> {
    const guardlar = (r.canMatch ?? []) as CanMatchFn[];
    const sonuclar: unknown[] = [];
    for (const g of guardlar) {
      sonuclar.push(await TestBed.runInInjectionContext(() => g(r, [])));
    }
    return sonuclar;
  }

  it.each(F5_ROTALARI_OPERATIONS_WRITE)(
    '%s: izinsiz rol reddedilir, OperationsWrite geçer',
    async (yol) => {
      const r = rota(yol);
      expect(r.canMatch?.length ?? 0, `${yol} canMatch guard'ı yok`).toBeGreaterThan(0);

      // Muhasebe benzeri rol: finans + rapor izni var, operasyon izni yok → reddedilir (ana sayfaya).
      izinler = ['FinanceWrite', 'ViewReports'];
      const red = await sonuc(r);
      expect(red.some((s) => s instanceof UrlTree)).toBe(true);

      izinler = ['OperationsWrite'];
      const gecer = await sonuc(r);
      expect(gecer.every((s) => s === true)).toBe(true);
    },
  );
});
