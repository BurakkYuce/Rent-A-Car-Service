import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ActivatedRoute } from '@angular/router';

import { OTURUM_BAGLAMI, OturumBaglami } from '@core/oturum/oturum-baglami';
import { SekmeDurumu } from '@core/sekme/sekme-durumu';
import { FetchPolicy, GetirmeNedeni } from './fetch-policy';

describe('FetchPolicy', () => {
  const baglam = signal<OturumBaglami | null>({ anahtar: 'kiraci-a|kullanici-1' });
  const parametre = signal<{ readonly arama: string }>({ arama: '' });
  const aktif = signal(true);
  let yuklemeler: [{ readonly arama: string }, GetirmeNedeni][];
  let sifirlamalar: number;
  let politika: FetchPolicy;

  beforeEach(() => {
    baglam.set({ anahtar: 'kiraci-a|kullanici-1' });
    parametre.set({ arama: '' });
    aktif.set(true);
    yuklemeler = [];
    sifirlamalar = 0;
    TestBed.configureTestingModule({
      providers: [FetchPolicy, { provide: OTURUM_BAGLAMI, useValue: baglam.asReadonly() }],
    });
    politika = TestBed.inject(FetchPolicy);
  });

  function bagla(aktifSinyali?: typeof aktif): void {
    politika.baglan({
      parametre,
      yukle: (p, neden) => yuklemeler.push([p, neden]),
      sifirla: () => sifirlamalar++,
      ...(aktifSinyali === undefined ? {} : { aktif: aktifSinyali }),
    });
    TestBed.tick();
  }

  function nedenler(): GetirmeNedeni[] {
    return yuklemeler.map(([, neden]) => neden);
  }

  it('ilk açılışta TEK yükleme (ilk)', () => {
    bagla();
    TestBed.tick();
    expect(yuklemeler).toEqual([[{ arama: '' }, 'ilk']]);
    expect(politika.sonNeden()).toBe('ilk');
  });

  it('parametre değişince sorgu; aynı referans tekrar yazılınca yükleme yok', () => {
    bagla();
    const yeni = { arama: 'İzmir' };
    parametre.set(yeni);
    TestBed.tick();
    parametre.set(yeni);
    TestBed.tick();
    expect(yuklemeler).toEqual([
      [{ arama: '' }, 'ilk'],
      [{ arama: 'İzmir' }, 'sorgu'],
    ]);
  });

  it('aynı tikteki çoklu değişiklik tek yükleme üretir (son değerle)', () => {
    bagla();
    parametre.set({ arama: 'a' });
    parametre.set({ arama: 'ab' });
    parametre.set({ arama: 'abc' });
    TestBed.tick();
    expect(yuklemeler.slice(1)).toEqual([[{ arama: 'abc' }, 'sorgu']]);
  });

  it('oturum bağlamı değişince baglam nedeniyle yeniden yükler', () => {
    bagla();
    baglam.set({ anahtar: 'kiraci-b|kullanici-2' });
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk', 'baglam']);
  });

  it('bağlam düşünce (çıkış) yüklemez, sifirla çağrılır; geri gelince ilk gibi yükler', () => {
    bagla();
    baglam.set(null);
    TestBed.tick();
    expect(sifirlamalar).toBe(1);
    expect(nedenler()).toEqual(['ilk']);

    parametre.set({ arama: 'x' });
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk']);

    baglam.set({ anahtar: 'kiraci-a|kullanici-1' });
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk', 'ilk']);
    expect(yuklemeler[1]?.[0]).toEqual({ arama: 'x' });
  });

  it('başta bağlam yoksa ne yükler ne sıfırlar', () => {
    baglam.set(null);
    bagla();
    expect(yuklemeler).toEqual([]);
    expect(sifirlamalar).toBe(0);
  });

  it('görünür değilken değişiklikler birikir; görünür olunca TEK yükleme', () => {
    aktif.set(false);
    bagla(aktif);
    expect(yuklemeler).toEqual([]);

    aktif.set(true);
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk']);

    aktif.set(false);
    TestBed.tick();
    parametre.set({ arama: '1' });
    TestBed.tick();
    parametre.set({ arama: '2' });
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk']);

    aktif.set(true);
    TestBed.tick();
    expect(yuklemeler.slice(1)).toEqual([[{ arama: '2' }, 'sorgu']]);
  });

  it('gizliyken değişip eski değerine dönen parametre yükleme üretmez', () => {
    const ilk = parametre();
    bagla(aktif);
    aktif.set(false);
    TestBed.tick();
    parametre.set({ arama: 'gecici' });
    TestBed.tick();
    parametre.set(ilk);
    TestBed.tick();
    aktif.set(true);
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk']);
  });

  it('yenile aynı parametreyle elle nedeniyle yükler', () => {
    bagla();
    politika.yenile();
    TestBed.tick();
    expect(yuklemeler).toEqual([
      [{ arama: '' }, 'ilk'],
      [{ arama: '' }, 'elle'],
    ]);
  });

  it('esit verilirse anlamca aynı parametre yükleme üretmez', () => {
    politika.baglan({
      parametre,
      yukle: (p, neden) => yuklemeler.push([p, neden]),
      esit: (a, b) => a.arama === b.arama,
    });
    TestBed.tick();
    parametre.set({ arama: '' });
    TestBed.tick();
    expect(nedenler()).toEqual(['ilk']);
  });
});

describe('FetchPolicy + sekmeli çalışma alanı (F3.2)', () => {
  const kabukRotasi = { path: '', data: { kabuk: true }, children: [] };
  const sayfaRotasi = { path: 'kayit/:id', component: class {} };
  const snapshot = {
    routeConfig: sayfaRotasi,
    params: { id: '5' },
    pathFromRoot: [
      { routeConfig: null },
      { routeConfig: kabukRotasi },
      { routeConfig: sayfaRotasi },
    ],
  };
  const BU_SEKME = '/kayit/:id?id=5';
  const parametre = signal(1);
  let nedenler: GetirmeNedeni[];
  let durum: SekmeDurumu;
  let politika: FetchPolicy;

  beforeEach(() => {
    parametre.set(1);
    nedenler = [];
    TestBed.configureTestingModule({
      providers: [
        FetchPolicy,
        { provide: OTURUM_BAGLAMI, useValue: signal({ anahtar: 'k|u|*' }).asReadonly() },
        { provide: ActivatedRoute, useValue: { snapshot } },
      ],
    });
    durum = TestBed.inject(SekmeDurumu);
    politika = TestBed.inject(FetchPolicy);
  });

  function bagla(sekmeyeDonunce?: 'yenile'): void {
    politika.baglan({
      parametre,
      yukle: (_p, neden) => nedenler.push(neden),
      ...(sekmeyeDonunce ? { sekmeyeDonunce } : {}),
    });
    TestBed.tick();
  }

  it('aktif verilmezse sayfanın SEKMESİ belirler: arkadayken biriktirir, öne gelince tek yükleme', () => {
    durum.etkinlestir(BU_SEKME);
    bagla();
    expect(nedenler).toEqual(['ilk']);

    durum.etkinlestir('/diger');
    parametre.set(2);
    TestBed.tick();
    parametre.set(3);
    TestBed.tick();
    expect(nedenler).toEqual(['ilk']);

    durum.etkinlestir(BU_SEKME);
    TestBed.tick();
    expect(nedenler).toEqual(['ilk', 'sorgu']);

    // Değişiklik yoksa dönüşte yükleme yok (varsayılan: degisirse).
    durum.etkinlestir('/diger');
    TestBed.tick();
    durum.etkinlestir(BU_SEKME);
    TestBed.tick();
    expect(nedenler).toEqual(['ilk', 'sorgu']);
  });

  it("sekmeyeDonunce: 'yenile' her dönüşte yükler (sekme nedeni); aynı sekmede kalınca yüklemez", () => {
    durum.etkinlestir(BU_SEKME);
    bagla('yenile');
    durum.etkinlestir(BU_SEKME);
    TestBed.tick();
    expect(nedenler).toEqual(['ilk']);

    durum.etkinlestir('/diger');
    TestBed.tick();
    durum.etkinlestir(BU_SEKME);
    TestBed.tick();
    expect(nedenler).toEqual(['ilk', 'sekme']);
  });

  it('kabuk yokken (etkin sekme null) sayfa her zaman görünür sayılır', () => {
    bagla();
    parametre.set(2);
    TestBed.tick();
    expect(nedenler).toEqual(['ilk', 'sorgu']);
  });
});
