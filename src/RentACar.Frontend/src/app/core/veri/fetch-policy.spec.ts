import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { OTURUM_BAGLAMI, OturumBaglami } from '@core/oturum/oturum-baglami';
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
