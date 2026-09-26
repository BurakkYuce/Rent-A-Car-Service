import { TestBed } from '@angular/core/testing';
import { provideRouter, UrlTree, type CanMatchFn, type Route } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Permission } from '@core/oturum/oturum-tipleri';

import { PAGES } from './sayfalar';

/**
 * F7.2 parite çiti — cari + CRM sayfalarının izni. Beklenen tablo ELLE yazıldı (uç izinleri, #283): cari okuma
 * OperationsWrite ∨ FinanceWrite ∨ ViewReports; yeni cari OperationsWrite; anket/şikayet/assistans/hukuk
 * OperationsWrite; CRM analiz ViewReports. Rota tablosundan TÜRETİLMEZ.
 */
const READ: Permission[][] = [['OperationsWrite'], ['FinanceWrite'], ['ViewReports']];
const EXPECTED: readonly (readonly [string, readonly Permission[][], readonly Permission[][]])[] = [
  ['cariler', READ, [['OperationsDelete'], ['ManageUsers']]],
  ['cariler/yeni', [['OperationsWrite']], [['FinanceWrite', 'ViewReports', 'ManageUsers']]],
  ['cariler/:id', READ, [['ManageUsers']]],
  ['cariler/:id/detay', READ, [['OperationsDelete']]],
  ['anketler', [['OperationsWrite']], [['FinanceWrite', 'ViewReports']]],
  ['sikayetler', [['OperationsWrite']], [['FinanceWrite', 'ViewReports']]],
  ['assistans', [['OperationsWrite']], [['FinanceWrite', 'ViewReports']]],
  ['hukuk', [['OperationsWrite']], [['FinanceWrite', 'ViewReports']]],
  ['crm', [['ViewReports']], [['OperationsWrite', 'FinanceWrite']]],
];

describe('F7.2 cari + CRM rotaları: uç izinleriyle aynı kapı', () => {
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

  it('cariler/yeni, cariler/:id’den ÖNCE eşleşir', () => {
    const order = PAGES.map((r) => r.path);
    expect(order.indexOf('cariler/yeni')).toBeLessThan(order.indexOf('cariler/:id'));
  });
});
