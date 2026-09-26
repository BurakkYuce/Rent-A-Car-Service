import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Observable, Subject, finalize, tap, throwError } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import { Sayfa } from '@core/api/sayfa';
import { TemelStore, TemelStoreSecenekleri, noRecord } from './temel-store';

interface Satir {
  readonly plaka: string;
}

/** Elle kontrol edilen sahte kaynak: her `yukle` bir Subject açar; test sonucu ne zaman döneceğini seçer. */
class FakeSource<P, T> {
  readonly requests: { parametre: P; cevap: Subject<T>; iptal: boolean }[] = [];

  /** `iptal`: yanıt (değer/hata) gelmeden abonelikten çıkıldı — switchMap'in iptali. */
  readonly fetch = (parameter: P): Observable<T> => {
    const record = { parametre: parameter, cevap: new Subject<T>(), iptal: false };
    this.requests.push(record);
    let responded = false;
    return record.cevap.pipe(
      tap({ next: () => (responded = true), error: () => (responded = true) }),
      finalize(() => {
        if (!responded) record.iptal = true;
      }),
    );
  };

  istek(i: number): { parametre: P; cevap: Subject<T>; iptal: boolean } {
    const record = this.requests[i];
    if (record === undefined) throw new Error(`İstek ${i} yok`);
    return record;
  }
}

function sayfa(plates: readonly string[]): Sayfa<Satir> {
  return {
    kayitlar: plates.map((plate) => ({ plaka: plate })),
    toplam: plates.length,
    sayfaNo: 1,
    boyut: 50,
  };
}

function createStore<P, T>(
  fetch: (p: P) => Observable<T>,
  options?: TemelStoreSecenekleri,
): TemelStore<T, P> {
  return TestBed.runInInjectionContext(() => new TemelStore<T, P>(fetch, options));
}

const ERROR_500 = new HttpErrorResponse({
  status: 500,
  error: { title: 'Sunucu hatası', status: 500, detail: 'Beklenmeyen bir hata oluştu.' },
});

describe('TemelStore', () => {
  it('başlangıç bos; yukle → yukleniyor; yanıt → hazir', () => {
    const source = new FakeSource<string, Sayfa<Satir>>();
    const store = createStore(source.fetch);

    expect(store.tur()).toBe('bos');
    expect(store.veri()).toBeUndefined();

    store.yukle('ilk');
    expect(store.tur()).toBe('yukleniyor');
    expect(store.isLoading()).toBe(true);
    expect(store.veri()).toBeUndefined();

    source.istek(0).cevap.next(sayfa(['34 ABC 01']));
    expect(store.tur()).toBe('hazir');
    expect(store.veri()?.kayitlar).toEqual([{ plaka: '34 ABC 01' }]);
    expect(store.hata()).toBeUndefined();
  });

  it('HATA BOŞ LİSTE DEĞİLDİR: hata durumunda veri yok, kayitYok false', () => {
    const store = createStore<void, Sayfa<Satir>>(() => throwError(() => ERROR_500));
    store.yukle();

    expect(store.tur()).toBe('hata');
    expect(store.veri()).toBeUndefined();
    expect(store.hata()).toBeInstanceOf(ApiHatasi);
    expect(store.hata()?.kod).toBe('sunucu');
    expect(noRecord(store.durum())).toBe(false);
  });

  it('kayitYok yalnız başarılı + sıfır kayıtta true', () => {
    const source = new FakeSource<void, Sayfa<Satir>>();
    const store = createStore(source.fetch);
    expect(noRecord(store.durum())).toBe(false); // bos

    store.yukle();
    expect(noRecord(store.durum())).toBe(false); // yukleniyor

    source.istek(0).cevap.next(sayfa([]));
    expect(store.tur()).toBe('hazir');
    expect(noRecord(store.durum())).toBe(true);
  });

  it('İPTAL: ikinci yukle birinciyi iptal eder; yalnız son sonuç yazılır', () => {
    const source = new FakeSource<string, Sayfa<Satir>>();
    const store = createStore(source.fetch);

    store.yukle('eski-filtre');
    store.yukle('yeni-filtre');

    expect(source.istek(0).iptal).toBe(true);
    expect(source.istek(1).iptal).toBe(false);

    // Yavaş eski yanıt SONRA gelir: yok sayılmalı (Revlo yarışı: eski filtrenin sonucu ekranda kalırdı).
    source.istek(1).cevap.next(sayfa(['06 YEN 06']));
    source.istek(0).cevap.next(sayfa(['34 ESK 34']));

    expect(store.veri()?.kayitlar).toEqual([{ plaka: '06 YEN 06' }]);
  });

  it('İPTAL: eski istek hatayla dönse bile yeni sonucu bozmaz', () => {
    const source = new FakeSource<string, Sayfa<Satir>>();
    const store = createStore(source.fetch);

    store.yukle('a');
    store.yukle('b');
    source.istek(0).cevap.error(ERROR_500);
    expect(store.tur()).toBe('yukleniyor');

    source.istek(1).cevap.next(sayfa(['B']));
    expect(store.tur()).toBe('hazir');
  });

  it('hata sonrası yenile aynı parametreyle yeniden ister ve toparlar', () => {
    const source = new FakeSource<number, Sayfa<Satir>>();
    const store = createStore(source.fetch);

    store.yukle(7);
    source.istek(0).cevap.error(ERROR_500);
    expect(store.tur()).toBe('hata');

    store.yenile();
    expect(source.istek(1).parametre).toBe(7);
    expect(store.tur()).toBe('yukleniyor');
    source.istek(1).cevap.next(sayfa(['X']));
    expect(store.tur()).toBe('hazir');
  });

  it('yenile hiç yüklenmemiş store’da bir şey yapmaz', () => {
    const source = new FakeSource<number, Sayfa<Satir>>();
    const store = createStore(source.fetch);
    store.yenile();
    expect(source.requests.length).toBe(0);
    expect(store.tur()).toBe('bos');
  });

  it('oncekiVeriyiKoru: yeniden yüklerken eski veri görünür; hata gelince düşer', () => {
    const source = new FakeSource<string, Sayfa<Satir>>();
    const store = createStore(source.fetch, { oncekiVeriyiKoru: true });

    store.yukle('1');
    source.istek(0).cevap.next(sayfa(['A']));
    store.yukle('2');

    expect(store.tur()).toBe('yukleniyor');
    expect(store.veri()?.kayitlar).toEqual([{ plaka: 'A' }]);
    expect(noRecord(store.durum())).toBe(false);

    source.istek(1).cevap.error(ERROR_500);
    expect(store.tur()).toBe('hata');
    expect(store.veri()).toBeUndefined();
  });

  it('varsayılan (koru kapalı): yeniden yüklerken veri görünmez', () => {
    const source = new FakeSource<string, Sayfa<Satir>>();
    const store = createStore(source.fetch);
    store.yukle('1');
    source.istek(0).cevap.next(sayfa(['A']));
    store.yukle('2');
    expect(store.veri()).toBeUndefined();
  });

  it('sifirla süren isteği iptal eder, bos’a döner, geç gelen yanıt yazılmaz', () => {
    const source = new FakeSource<string, Sayfa<Satir>>();
    const store = createStore(source.fetch);
    store.yukle('x');
    store.reset();

    expect(source.istek(0).iptal).toBe(true);
    source.istek(0).cevap.next(sayfa(['GEC']));
    expect(store.tur()).toBe('bos');
    expect(store.veri()).toBeUndefined();

    store.yenile(); // sıfırlanan store son parametreyi unutmuş olmalı
    expect(source.requests.length).toBe(1);
  });

  it('getir eşzamanlı fırlatırsa hata durumu; store sonraki yuklemede çalışmaya devam eder', () => {
    let corrupt = true;
    const source = new FakeSource<void, Sayfa<Satir>>();
    const store = createStore((p: void) => {
      if (corrupt) throw new TypeError('adaptör hatası');
      return source.fetch(p);
    });

    store.yukle();
    expect(store.tur()).toBe('hata');
    expect(store.hata()?.kod).toBe('bilinmeyen');

    corrupt = false;
    store.yukle();
    source.istek(0).cevap.next(sayfa(['OK']));
    expect(store.tur()).toBe('hazir');
  });

  it('değer üretmeden tamamlanan kaynak yukleniyor’da takılı kalmaz → hata', () => {
    const store = createStore<void, Sayfa<Satir>>(() => EMPTY);
    store.yukle();
    expect(store.tur()).toBe('hata');
  });

  it('bileşen/injector yok edilince abonelik kapanır (sızıntı yok)', () => {
    const source = new FakeSource<void, Sayfa<Satir>>();
    const store = createStore(source.fetch);
    store.yukle();
    TestBed.resetTestingModule();
    expect(source.istek(0).iptal).toBe(true);
  });
});
