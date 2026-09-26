import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';

import { PAGE_SIZE, pagedDefinitionSource } from './paged-source';

/** Sayfalı F11.1b ucu: tanım CRUD'u tüm listeyi ister — `toplam`a kadar sırayla okunur. */
describe('pagedDefinitionSource', () => {
  function setup(total: number) {
    const calls: Record<string, unknown>[] = [];
    const api = {
      get: (_path: string, opts: { parametreler: Record<string, unknown> }) => {
        calls.push(opts.parametreler);
        const no = Number(opts.parametreler['sayfa']);
        const start = (no - 1) * PAGE_SIZE;
        const count = Math.max(0, Math.min(PAGE_SIZE, total - start));
        const records = Array.from({ length: count }, (_, i) => ({ id: String(start + i) }));
        return of({ kayitlar: records, toplam: total });
      },
    };
    TestBed.configureTestingModule({ providers: [{ provide: ApiIstemcisi, useValue: api }] });
    const source = TestBed.runInInjectionContext(() =>
      pagedDefinitionSource('/api/ui/v1/kdv-oranlari', 'ad'),
    );
    return { source, calls };
  }

  it('tek sayfa: tek istek, sıralama ve azami boyutla', async () => {
    const { source, calls } = setup(3);
    const rows = await firstValueFrom(source.listele());
    expect(rows.map((r) => r.id)).toEqual(['0', '1', '2']);
    expect(calls).toEqual([{ sayfa: 1, boyut: 200, sirala: 'ad' }]);
  });

  it('çok sayfa: 450 kayıt üç istekte, sırası korunarak', async () => {
    const { source, calls } = setup(450);
    const rows = await firstValueFrom(source.listele());
    expect(rows).toHaveLength(450);
    expect(rows[449]?.id).toBe('449');
    expect(calls.map((c) => c['sayfa'])).toEqual([1, 2, 3]);
  });

  it('boş liste: tek istek, boş dizi', async () => {
    const { source, calls } = setup(0);
    expect(await firstValueFrom(source.listele())).toEqual([]);
    expect(calls).toHaveLength(1);
  });
});
