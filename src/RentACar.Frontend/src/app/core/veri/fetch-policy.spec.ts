import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ActivatedRoute } from '@angular/router';

import { SESSION_CONTEXT, OturumBaglami } from '@core/oturum/oturum-baglami';
import { TabState } from '@core/sekme/tab-state';
import { FetchPolicy, FetchReason } from './fetch-policy';

describe('FetchPolicy', () => {
  const context = signal<OturumBaglami | null>({ anahtar: 'kiraci-a|kullanici-1' });
  const parameter = signal<{ readonly arama: string }>({ arama: '' });
  const active = signal(true);
  let loads: [{ readonly arama: string }, FetchReason][];
  let resets: number;
  let policy: FetchPolicy;

  beforeEach(() => {
    context.set({ anahtar: 'kiraci-a|kullanici-1' });
    parameter.set({ arama: '' });
    active.set(true);
    loads = [];
    resets = 0;
    TestBed.configureTestingModule({
      providers: [FetchPolicy, { provide: SESSION_CONTEXT, useValue: context.asReadonly() }],
    });
    policy = TestBed.inject(FetchPolicy);
  });

  function bind(activeSignal?: typeof active): void {
    policy.connect({
      parametre: parameter,
      yukle: (p, reason) => loads.push([p, reason]),
      sifirla: () => resets++,
      ...(activeSignal === undefined ? {} : { aktif: activeSignal }),
    });
    TestBed.tick();
  }

  function reasons(): FetchReason[] {
    return loads.map(([, reason]) => reason);
  }

  it('ilk açılışta TEK yükleme (ilk)', () => {
    bind();
    TestBed.tick();
    expect(loads).toEqual([[{ arama: '' }, 'ilk']]);
    expect(policy.lastReason()).toBe('ilk');
  });

  it('parametre değişince sorgu; aynı referans tekrar yazılınca yükleme yok', () => {
    bind();
    const newItem = { arama: 'İzmir' };
    parameter.set(newItem);
    TestBed.tick();
    parameter.set(newItem);
    TestBed.tick();
    expect(loads).toEqual([
      [{ arama: '' }, 'ilk'],
      [{ arama: 'İzmir' }, 'sorgu'],
    ]);
  });

  it('aynı tikteki çoklu değişiklik tek yükleme üretir (son değerle)', () => {
    bind();
    parameter.set({ arama: 'a' });
    parameter.set({ arama: 'ab' });
    parameter.set({ arama: 'abc' });
    TestBed.tick();
    expect(loads.slice(1)).toEqual([[{ arama: 'abc' }, 'sorgu']]);
  });

  it('oturum bağlamı değişince baglam nedeniyle yeniden yükler', () => {
    bind();
    context.set({ anahtar: 'kiraci-b|kullanici-2' });
    TestBed.tick();
    expect(reasons()).toEqual(['ilk', 'baglam']);
  });

  it('bağlam düşünce (çıkış) yüklemez, sifirla çağrılır; geri gelince ilk gibi yükler', () => {
    bind();
    context.set(null);
    TestBed.tick();
    expect(resets).toBe(1);
    expect(reasons()).toEqual(['ilk']);

    parameter.set({ arama: 'x' });
    TestBed.tick();
    expect(reasons()).toEqual(['ilk']);

    context.set({ anahtar: 'kiraci-a|kullanici-1' });
    TestBed.tick();
    expect(reasons()).toEqual(['ilk', 'ilk']);
    expect(loads[1]?.[0]).toEqual({ arama: 'x' });
  });

  it('başta bağlam yoksa ne yükler ne sıfırlar', () => {
    context.set(null);
    bind();
    expect(loads).toEqual([]);
    expect(resets).toBe(0);
  });

  it('görünür değilken değişiklikler birikir; görünür olunca TEK yükleme', () => {
    active.set(false);
    bind(active);
    expect(loads).toEqual([]);

    active.set(true);
    TestBed.tick();
    expect(reasons()).toEqual(['ilk']);

    active.set(false);
    TestBed.tick();
    parameter.set({ arama: '1' });
    TestBed.tick();
    parameter.set({ arama: '2' });
    TestBed.tick();
    expect(reasons()).toEqual(['ilk']);

    active.set(true);
    TestBed.tick();
    expect(loads.slice(1)).toEqual([[{ arama: '2' }, 'sorgu']]);
  });

  it('gizliyken değişip eski değerine dönen parametre yükleme üretmez', () => {
    const first = parameter();
    bind(active);
    active.set(false);
    TestBed.tick();
    parameter.set({ arama: 'gecici' });
    TestBed.tick();
    parameter.set(first);
    TestBed.tick();
    active.set(true);
    TestBed.tick();
    expect(reasons()).toEqual(['ilk']);
  });

  it('yenile aynı parametreyle elle nedeniyle yükler', () => {
    bind();
    policy.yenile();
    TestBed.tick();
    expect(loads).toEqual([
      [{ arama: '' }, 'ilk'],
      [{ arama: '' }, 'elle'],
    ]);
  });

  it('esit verilirse anlamca aynı parametre yükleme üretmez', () => {
    policy.connect({
      parametre: parameter,
      yukle: (p, reason) => loads.push([p, reason]),
      esit: (a, b) => a.arama === b.arama,
    });
    TestBed.tick();
    parameter.set({ arama: '' });
    TestBed.tick();
    expect(reasons()).toEqual(['ilk']);
  });
});

describe('FetchPolicy + sekmeli çalışma alanı (F3.2)', () => {
  const shellRoute = { path: '', data: { kabuk: true }, children: [] };
  const pageRoute = { path: 'kayit/:id', component: class {} };
  const snapshot = {
    routeConfig: pageRoute,
    params: { id: '5' },
    pathFromRoot: [{ routeConfig: null }, { routeConfig: shellRoute }, { routeConfig: pageRoute }],
  };
  const THIS_TAB = '/kayit/:id?id=5';
  const parameter = signal(1);
  let reasons: FetchReason[];
  let status: TabState;
  let policy: FetchPolicy;

  beforeEach(() => {
    parameter.set(1);
    reasons = [];
    TestBed.configureTestingModule({
      providers: [
        FetchPolicy,
        { provide: SESSION_CONTEXT, useValue: signal({ anahtar: 'k|u|*' }).asReadonly() },
        { provide: ActivatedRoute, useValue: { snapshot } },
      ],
    });
    status = TestBed.inject(TabState);
    policy = TestBed.inject(FetchPolicy);
  });

  function bind(onTabReturn?: 'yenile'): void {
    policy.connect({
      parametre: parameter,
      yukle: (_p, reason) => reasons.push(reason),
      ...(onTabReturn ? { sekmeyeDonunce: onTabReturn } : {}),
    });
    TestBed.tick();
  }

  it('aktif verilmezse sayfanın SEKMESİ belirler: arkadayken biriktirir, öne gelince tek yükleme', () => {
    status.activate(THIS_TAB);
    bind();
    expect(reasons).toEqual(['ilk']);

    status.activate('/diger');
    parameter.set(2);
    TestBed.tick();
    parameter.set(3);
    TestBed.tick();
    expect(reasons).toEqual(['ilk']);

    status.activate(THIS_TAB);
    TestBed.tick();
    expect(reasons).toEqual(['ilk', 'sorgu']);

    // Değişiklik yoksa dönüşte yükleme yok (varsayılan: degisirse).
    status.activate('/diger');
    TestBed.tick();
    status.activate(THIS_TAB);
    TestBed.tick();
    expect(reasons).toEqual(['ilk', 'sorgu']);
  });

  it("sekmeyeDonunce: 'yenile' her dönüşte yükler (sekme nedeni); aynı sekmede kalınca yüklemez", () => {
    status.activate(THIS_TAB);
    bind('yenile');
    status.activate(THIS_TAB);
    TestBed.tick();
    expect(reasons).toEqual(['ilk']);

    status.activate('/diger');
    TestBed.tick();
    status.activate(THIS_TAB);
    TestBed.tick();
    expect(reasons).toEqual(['ilk', 'sekme']);
  });

  it('kabuk yokken (etkin sekme null) sayfa her zaman görünür sayılır', () => {
    bind();
    parameter.set(2);
    TestBed.tick();
    expect(reasons).toEqual(['ilk', 'sorgu']);
  });
});
