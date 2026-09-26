import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree, type CanMatchFn, type Route } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Permission } from '@core/oturum/oturum-tipleri';

import { PAGES } from './sayfalar';

/**
 * F6.2a parite çiti — araç sayfalarının izni. Beklenen tablo ELLE yazıldı (uçların izinleri, #278:
 * okuma OperationsWrite VEYA ViewReports; detaylı liste ViewReports; yeni araç, durum panosu ve tanımlar
 * OperationsWrite). Rota tablosundan TÜRETİLMEZ: guard kaybolursa ya da gevşerse test kırılır.
 */
const EXPECTED: readonly (readonly [string, readonly Permission[][], readonly Permission[][]])[] = [
  // [rota, geçen izin kümeleri, reddedilen izin kümeleri]
  ['araclar', [['OperationsWrite'], ['ViewReports']], [['FinanceWrite']]],
  ['araclar/:id', [['OperationsWrite'], ['ViewReports']], [['FinanceWrite']]],
  ['araclar/:id/detay', [['OperationsWrite'], ['ViewReports']], [['FinanceWrite']]],
  ['araclar/detayli', [['ViewReports']], [['OperationsWrite']]],
  ['araclar/yeni', [['OperationsWrite']], [['ViewReports', 'FinanceWrite']]],
  ['arac-durum', [['OperationsWrite']], [['ViewReports', 'FinanceWrite']]],
  ['arac-sahipleri', [['OperationsWrite']], [['ViewReports', 'FinanceWrite']]],
  ['segmentler', [['OperationsWrite']], [['ViewReports', 'FinanceWrite']]],
  ['arac-tipleri', [['OperationsWrite']], [['ViewReports', 'FinanceWrite']]],
];

describe('F6 araç rotaları: uç izinleriyle aynı kapı', () => {
  let permissions: readonly Permission[] = [];
  const fakeSession = {
    initialLoad: () => Promise.resolve(null),
    loggedIn: () => true,
    ben: () => ({ pilot: true }),
    izinVar: (p: Permission) => permissions.includes(p),
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

  async function results(r: Route): Promise<unknown[]> {
    const out: unknown[] = [];
    for (const g of (r.canMatch ?? []) as CanMatchFn[]) {
      out.push(await TestBed.runInInjectionContext(() => g(r, [])));
    }
    return out;
  }

  it.each(EXPECTED)('%s', async (path, allowed, denied) => {
    const found = PAGES.filter((r) => r.path === path);
    expect(found, `rota tekil olmalı: ${path}`).toHaveLength(1);
    const r = found[0]!;
    expect(r.canMatch?.length ?? 0, `${path} canMatch guard'ı yok`).toBeGreaterThan(0);
    for (const set of allowed) {
      permissions = set;
      expect(
        (await results(r)).every((s) => s === true),
        `${path} ${set.join('+')}`,
      ).toBe(true);
    }
    for (const set of denied) {
      permissions = set;
      expect(
        (await results(r)).some((s) => s instanceof UrlTree),
        `${path} ${set.join('+')}`,
      ).toBe(true);
    }
  });

  it('araclar/yeni ve araclar/detayli, araclar/:id’den ÖNCE eşleşir', () => {
    const order = PAGES.map((r) => r.path);
    expect(order.indexOf('araclar/yeni')).toBeLessThan(order.indexOf('araclar/:id'));
    expect(order.indexOf('araclar/detayli')).toBeLessThan(order.indexOf('araclar/:id'));
  });
});
