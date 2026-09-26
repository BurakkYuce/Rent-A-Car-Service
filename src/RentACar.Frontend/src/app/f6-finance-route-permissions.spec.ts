import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree, type CanMatchFn, type Route } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Permission } from '@core/oturum/oturum-tipleri';

import { PAGES } from './sayfalar';

/**
 * F6.2b parite çiti — araç finans sayfalarının izni. Beklenen tablo ELLE yazıldı (uç izinleri, #279): kredi ve sipariş
 * okuma OperationsWrite ∨ FinanceWrite ∨ ViewReports; müşteri taksit FinanceWrite ∨ ViewReports (Operatör GÖREMEZ);
 * yeni sipariş, BAF, hasar OperationsWrite; filo plan ViewReports ∨ OperationsWrite. Rota tablosundan TÜRETİLMEZ.
 */
const EXPECTED: readonly (readonly [string, readonly Permission[][], readonly Permission[][]])[] = [
  ['arac-kredi', [['OperationsWrite'], ['FinanceWrite'], ['ViewReports']], [['OperationsDelete']]],
  ['arac-kredi/:id', [['OperationsWrite'], ['FinanceWrite'], ['ViewReports']], [['ManageUsers']]],
  [
    'musteri-taksit',
    [['FinanceWrite'], ['ViewReports']],
    [['OperationsWrite', 'OperationsDelete']],
  ],
  [
    'arac-siparis',
    [['OperationsWrite'], ['FinanceWrite'], ['ViewReports']],
    [['OperationsDelete']],
  ],
  ['arac-siparis/yeni', [['OperationsWrite']], [['FinanceWrite', 'ViewReports']]],
  ['arac-siparis/:id', [['OperationsWrite'], ['FinanceWrite'], ['ViewReports']], [['ManageUsers']]],
  ['baf', [['OperationsWrite']], [['FinanceWrite', 'ViewReports', 'OperationsDelete']]],
  ['hasar', [['OperationsWrite']], [['FinanceWrite', 'ViewReports']]],
  ['filo-plan', [['ViewReports'], ['OperationsWrite']], [['FinanceWrite']]],
];

describe('F6.2b araç finans rotaları: uç izinleriyle aynı kapı', () => {
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

  it('arac-siparis/yeni, arac-siparis/:id’den ÖNCE eşleşir', () => {
    const order = PAGES.map((r) => r.path);
    expect(order.indexOf('arac-siparis/yeni')).toBeLessThan(order.indexOf('arac-siparis/:id'));
  });
});
