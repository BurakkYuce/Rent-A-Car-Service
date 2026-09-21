import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Observable, Subject, finalize, tap, throwError } from 'rxjs';

import { ApiHatasi } from '@core/api/api-hatasi';
import { Sayfa } from '@core/api/sayfa';
import { TemelStore, TemelStoreSecenekleri, kayitYok } from './temel-store';

interface Satir {
  readonly plaka: string;
}

/** Elle kontrol edilen sahte kaynak: her `yukle` bir Subject açar; test sonucu ne zaman döneceğini seçer. */
class SahteKaynak<P, T> {
  readonly istekler: { parametre: P; cevap: Subject<T>; iptal: boolean }[] = [];

  /** `iptal`: yanıt (değer/hata) gelmeden abonelikten çıkıldı — switchMap'in iptali. */
  readonly getir = (parametre: P): Observable<T> => {
    const kayit = { parametre, cevap: new Subject<T>(), iptal: false };
    this.istekler.push(kayit);
    let yanitlandi = false;
    return kayit.cevap.pipe(
      tap({ next: () => (yanitlandi = true), error: () => (yanitlandi = true) }),
      finalize(() => {
        if (!yanitlandi) kayit.iptal = true;
      }),
    );
  };

  istek(i: number): { parametre: P; cevap: Subject<T>; iptal: boolean } {
    const kayit = this.istekler[i];
    if (kayit === undefined) throw new Error(`İstek ${i} yok`);
    return kayit;
  }
}

function sayfa(plakalar: readonly string[]): Sayfa<Satir> {
  return {
    kayitlar: plakalar.map((plaka) => ({ plaka })),
    toplam: plakalar.length,
    sayfaNo: 1,
    boyut: 50,
  };
}

function storeKur<P, T>(
  getir: (p: P) => Observable<T>,
  secenekler?: TemelStoreSecenekleri,
): TemelStore<T, P> {
  return TestBed.runInInjectionContext(() => new TemelStore<T, P>(getir, secenekler));
}

const HATA_500 = new HttpErrorResponse({
  status: 500,
  error: { title: 'Sunucu hatası', status: 500, detail: 'Beklenmeyen bir hata oluştu.' },
});

describe('TemelStore', () => {
  it('başlangıç bos; yukle → yukleniyor; yanıt → hazir', () => {
    const kaynak = new SahteKaynak<string, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);

    expect(store.tur()).toBe('bos');
    expect(store.veri()).toBeUndefined();

    store.yukle('ilk');
    expect(store.tur()).toBe('yukleniyor');
    expect(store.yukleniyor()).toBe(true);
    expect(store.veri()).toBeUndefined();

    kaynak.istek(0).cevap.next(sayfa(['34 ABC 01']));
    expect(store.tur()).toBe('hazir');
    expect(store.veri()?.kayitlar).toEqual([{ plaka: '34 ABC 01' }]);
    expect(store.hata()).toBeUndefined();
  });

  it('HATA BOŞ LİSTE DEĞİLDİR: hata durumunda veri yok, kayitYok false', () => {
    const store = storeKur<void, Sayfa<Satir>>(() => throwError(() => HATA_500));
    store.yukle();

    expect(store.tur()).toBe('hata');
    expect(store.veri()).toBeUndefined();
    expect(store.hata()).toBeInstanceOf(ApiHatasi);
    expect(store.hata()?.kod).toBe('sunucu');
    expect(kayitYok(store.durum())).toBe(false);
  });

  it('kayitYok yalnız başarılı + sıfır kayıtta true', () => {
    const kaynak = new SahteKaynak<void, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);
    expect(kayitYok(store.durum())).toBe(false); // bos

    store.yukle();
    expect(kayitYok(store.durum())).toBe(false); // yukleniyor

    kaynak.istek(0).cevap.next(sayfa([]));
    expect(store.tur()).toBe('hazir');
    expect(kayitYok(store.durum())).toBe(true);
  });

  it('İPTAL: ikinci yukle birinciyi iptal eder; yalnız son sonuç yazılır', () => {
    const kaynak = new SahteKaynak<string, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);

    store.yukle('eski-filtre');
    store.yukle('yeni-filtre');

    expect(kaynak.istek(0).iptal).toBe(true);
    expect(kaynak.istek(1).iptal).toBe(false);

    // Yavaş eski yanıt SONRA gelir: yok sayılmalı (Revlo yarışı: eski filtrenin sonucu ekranda kalırdı).
    kaynak.istek(1).cevap.next(sayfa(['06 YEN 06']));
    kaynak.istek(0).cevap.next(sayfa(['34 ESK 34']));

    expect(store.veri()?.kayitlar).toEqual([{ plaka: '06 YEN 06' }]);
  });

  it('İPTAL: eski istek hatayla dönse bile yeni sonucu bozmaz', () => {
    const kaynak = new SahteKaynak<string, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);

    store.yukle('a');
    store.yukle('b');
    kaynak.istek(0).cevap.error(HATA_500);
    expect(store.tur()).toBe('yukleniyor');

    kaynak.istek(1).cevap.next(sayfa(['B']));
    expect(store.tur()).toBe('hazir');
  });

  it('hata sonrası yenile aynı parametreyle yeniden ister ve toparlar', () => {
    const kaynak = new SahteKaynak<number, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);

    store.yukle(7);
    kaynak.istek(0).cevap.error(HATA_500);
    expect(store.tur()).toBe('hata');

    store.yenile();
    expect(kaynak.istek(1).parametre).toBe(7);
    expect(store.tur()).toBe('yukleniyor');
    kaynak.istek(1).cevap.next(sayfa(['X']));
    expect(store.tur()).toBe('hazir');
  });

  it('yenile hiç yüklenmemiş store’da bir şey yapmaz', () => {
    const kaynak = new SahteKaynak<number, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);
    store.yenile();
    expect(kaynak.istekler.length).toBe(0);
    expect(store.tur()).toBe('bos');
  });

  it('oncekiVeriyiKoru: yeniden yüklerken eski veri görünür; hata gelince düşer', () => {
    const kaynak = new SahteKaynak<string, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir, { oncekiVeriyiKoru: true });

    store.yukle('1');
    kaynak.istek(0).cevap.next(sayfa(['A']));
    store.yukle('2');

    expect(store.tur()).toBe('yukleniyor');
    expect(store.veri()?.kayitlar).toEqual([{ plaka: 'A' }]);
    expect(kayitYok(store.durum())).toBe(false);

    kaynak.istek(1).cevap.error(HATA_500);
    expect(store.tur()).toBe('hata');
    expect(store.veri()).toBeUndefined();
  });

  it('varsayılan (koru kapalı): yeniden yüklerken veri görünmez', () => {
    const kaynak = new SahteKaynak<string, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);
    store.yukle('1');
    kaynak.istek(0).cevap.next(sayfa(['A']));
    store.yukle('2');
    expect(store.veri()).toBeUndefined();
  });

  it('sifirla süren isteği iptal eder, bos’a döner, geç gelen yanıt yazılmaz', () => {
    const kaynak = new SahteKaynak<string, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);
    store.yukle('x');
    store.sifirla();

    expect(kaynak.istek(0).iptal).toBe(true);
    kaynak.istek(0).cevap.next(sayfa(['GEC']));
    expect(store.tur()).toBe('bos');
    expect(store.veri()).toBeUndefined();

    store.yenile(); // sıfırlanan store son parametreyi unutmuş olmalı
    expect(kaynak.istekler.length).toBe(1);
  });

  it('getir eşzamanlı fırlatırsa hata durumu; store sonraki yuklemede çalışmaya devam eder', () => {
    let bozuk = true;
    const kaynak = new SahteKaynak<void, Sayfa<Satir>>();
    const store = storeKur((p: void) => {
      if (bozuk) throw new TypeError('adaptör hatası');
      return kaynak.getir(p);
    });

    store.yukle();
    expect(store.tur()).toBe('hata');
    expect(store.hata()?.kod).toBe('bilinmeyen');

    bozuk = false;
    store.yukle();
    kaynak.istek(0).cevap.next(sayfa(['OK']));
    expect(store.tur()).toBe('hazir');
  });

  it('değer üretmeden tamamlanan kaynak yukleniyor’da takılı kalmaz → hata', () => {
    const store = storeKur<void, Sayfa<Satir>>(() => EMPTY);
    store.yukle();
    expect(store.tur()).toBe('hata');
  });

  it('bileşen/injector yok edilince abonelik kapanır (sızıntı yok)', () => {
    const kaynak = new SahteKaynak<void, Sayfa<Satir>>();
    const store = storeKur(kaynak.getir);
    store.yukle();
    TestBed.resetTestingModule();
    expect(kaynak.istek(0).iptal).toBe(true);
  });
});
