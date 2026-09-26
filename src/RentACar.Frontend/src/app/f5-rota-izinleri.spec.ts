import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree, type CanMatchFn, type Route } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Permission } from '@core/oturum/oturum-tipleri';

import { PAGES } from './sayfalar';

/**
 * F5.3 parite çiti — sayfa izni. Beklenen liste ELLE yazıldı (Blazor razor'larının `@attribute`'u:
 * ReservationList `[Authorize]` + uç grubu OperationsWrite; QuotationList, ReservationCalendar,
 * MusaitlikArama, RezSartList, FiloKiralamaList `izin:OperationsWrite`). Rota tablosundan TÜRETİLMEZ:
 * bir rota guard'ını kaybederse ya da yeni F5 rotası guard'sız eklenirse test kırılır.
 */
const F5_ROUTES_OPERATIONS_WRITE = [
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
  let permissions: readonly Permission[] = [];
  const fakeSession = {
    initialLoad: () => Promise.resolve(null),
    loggedIn: () => true,
    ben: () => ({ pilot: true }),
    izinVar: (permission: Permission) => permissions.includes(permission),
  };

  beforeEach(async () => {
    permissions = [];
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
        { provide: SessionService, useValue: fakeSession },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  function rota(path: string): Route {
    const found = PAGES.filter((r) => r.path === path);
    expect(found, `rota tekil olmalı: ${path}`).toHaveLength(1);
    return found[0]!;
  }

  async function sonuc(r: Route): Promise<unknown[]> {
    const guards = (r.canMatch ?? []) as CanMatchFn[];
    const results: unknown[] = [];
    for (const g of guards) {
      results.push(await TestBed.runInInjectionContext(() => g(r, [])));
    }
    return results;
  }

  it.each(F5_ROUTES_OPERATIONS_WRITE)(
    '%s: izinsiz rol reddedilir, OperationsWrite geçer',
    async (path) => {
      const r = rota(path);
      expect(r.canMatch?.length ?? 0, `${path} canMatch guard'ı yok`).toBeGreaterThan(0);

      // Muhasebe benzeri rol: finans + rapor izni var, operasyon izni yok → reddedilir (ana sayfaya).
      permissions = ['FinanceWrite', 'ViewReports'];
      const red = await sonuc(r);
      expect(red.some((s) => s instanceof UrlTree)).toBe(true);

      permissions = ['OperationsWrite'];
      const passes = await sonuc(r);
      expect(passes.every((s) => s === true)).toBe(true);
    },
  );
});
