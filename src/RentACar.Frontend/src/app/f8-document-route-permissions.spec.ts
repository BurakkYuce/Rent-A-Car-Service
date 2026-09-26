import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree, type CanMatchFn, type Route } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Permission } from '@core/oturum/oturum-tipleri';

import { PAGES } from './sayfalar';

/**
 * F8.2b parite çiti — finans belge sayfalarının izni. Beklenen tablo ELLE yazıldı (uç izinleri, #286): fatura okuma
 * FinanceWrite ∨ ViewReports; fatura detay listesi ve yazdır ViewReports; ceza okuma OperationsWrite ∨ FinanceWrite ∨
 * ViewReports; gider okuma FinanceWrite ∨ ViewReports; gelen e-fatura FinanceWrite; satış okuma FinanceWrite ∨
 * ViewReports ∨ OperationsWrite. Rota tablosundan TÜRETİLMEZ.
 */
const EXPECTED: readonly (readonly [string, readonly Permission[][], readonly Permission[][]])[] = [
  ['faturalar', [['FinanceWrite'], ['ViewReports']], [['OperationsWrite', 'OperationsDelete']]],
  ['faturalar/detay-listesi', [['ViewReports']], [['FinanceWrite', 'OperationsWrite']]],
  ['faturalar/:id/yazdir', [['ViewReports']], [['FinanceWrite', 'OperationsWrite']]],
  [
    'cezalar',
    [['OperationsWrite'], ['FinanceWrite'], ['ViewReports']],
    [['OperationsDelete', 'ManageUsers']],
  ],
  ['giderler', [['FinanceWrite'], ['ViewReports']], [['OperationsWrite', 'OperationsDelete']]],
  ['gelen-efatura', [['FinanceWrite']], [['ViewReports', 'OperationsWrite']]],
  ['satislar', [['FinanceWrite'], ['ViewReports'], ['OperationsWrite']], [['OperationsDelete']]],
];

describe('F8.2b finans belge rotaları: uç izinleriyle aynı kapı', () => {
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

  it('faturalar/detay-listesi, faturalar/:id/yazdir’dan ÖNCE tanımlı', () => {
    const order = PAGES.map((r) => r.path);
    expect(order.indexOf('faturalar/detay-listesi')).toBeLessThan(
      order.indexOf('faturalar/:id/yazdir'),
    );
  });
});
