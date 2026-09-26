import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ApiHatasi } from '../api/api-hatasi';
import { SessionService } from '../oturum/session-service';
import {
  COLLECTION_ATTEMPT_CHANNEL,
  CollectionAttemptRecord,
  CollectionAttempt,
  type TahsilatIcerigi,
} from './collection-attempt';

const K1 = 'bbbbbbbb-0000-4000-8000-000000000001';
const K2 = 'bbbbbbbb-0000-4000-8000-000000000002';
const tl = (amount: string, account = 'Kasa'): TahsilatIcerigi => ({
  tutar: amount,
  doviz: 'TRY',
  hesap: account,
});
const hata = (code: ApiHatasi['kod'], existing?: { tutar: number }): ApiHatasi =>
  ({
    status: code === 'mukerrer' ? 409 : 0,
    kod: code,
    detay: 'x',
    ...(existing
      ? { mevcut: { id: 'c', belgeNo: 'T-1', doviz: 'TRY', ayniIcerik: false, ...existing } }
      : {}),
  }) as ApiHatasi;

/** Kanal mesajı eşzamansız: yük altında (CI, soğuk derleme) varsayılan 1 sn'den geniş bekle. */
const wait = (fn: () => void) => vi.waitFor(fn, { timeout: 5000 });

/**
 * Bir "sekme": kendi enjektörü, kendi kök kaydı (aynı tarayıcıda her sekmenin ayrı uygulaması olur). Kanal Node'un
 * gerçek `BroadcastChannel`'ıdır — mesajlar eşzamansız ulaşır (`vi.waitFor`).
 */
function sekme(channelName: string) {
  let pickup: (() => void) | undefined;
  const enj = createEnvironmentInjector(
    [
      CollectionAttemptRecord,
      { provide: COLLECTION_ATTEMPT_CHANNEL, useValue: channelName },
      {
        provide: SessionService,
        useValue: {
          registerCleanup: (fn: () => void) => {
            pickup = fn;
            return () => undefined;
          },
        },
      },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  const record = enj.get(CollectionAttemptRecord);
  return {
    kayit: record,
    form: new CollectionAttempt(record),
    cikis: () => pickup?.(),
    kapat: () => enj.destroy(),
  };
}

describe('TahsilatDenemeKaydi — sekmeler arası (BroadcastChannel)', () => {
  // Test başına ayrı kanal: önceki testin (ya da aynı süreçteki başka dosyanın) trafiği karışmaz.
  let channelName = '';
  beforeEach(() => {
    channelName = `rc-tahsilat-denemesi-test-${crypto.randomUUID()}`;
  });
  const open: ReturnType<typeof sekme>[] = [];
  const openItem = () => {
    const s = sekme(channelName);
    open.push(s);
    return s;
  };
  afterEach(() => {
    for (const s of open.splice(0)) s.kapat();
  });

  it("A sekmesinde kaybolan 500, B sekmesinde 600 basılınca TEKRAR sayılır ve mevcut(500) 'önceki deneme' olur", async () => {
    const a = openItem();
    const b = openItem();
    expect(a.form.errorReceived(a.form.start(K1, tl('500.00')), hata('sunucu'))).toBeNull();

    await wait(() => expect(b.kayit.bilinmeyen(K1)).toEqual([tl('500.00')]));
    expect(b.form.isRepeat(K1)).toBe(true);
    const g = b.form.start(K1, tl('600.00'));
    expect(b.form.errorReceived(g, hata('mukerrer', { tutar: 500 }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
    expect(b.kayit.bilinmeyen(K2)).toEqual([]); // başka anahtar etkilenmez
  });

  it('B sekmesindeki 2xx anahtarı A sekmesinde de kapatır', async () => {
    const a = openItem();
    const b = openItem();
    a.kayit.ekle(K1, tl('500.00'));
    await wait(() => expect(b.form.isRepeat(K1)).toBe(true));
    b.form.successful(b.form.start(K1, tl('500.00')));
    await wait(() => expect(a.form.isRepeat(K1)).toBe(false));
  });

  it('sonradan açılan sekme mevcut durumu alır; iki sekme yanıtlasa da içerik bir kez eklenir', async () => {
    const a = openItem();
    const b = openItem();
    a.kayit.ekle(K1, tl('500.00'));
    await wait(() => expect(b.kayit.bilinmeyen(K1)).toHaveLength(1));
    const c = openItem(); // a ve b ikisi de "durum" yanıtlar
    await wait(() => expect(c.kayit.bilinmeyen(K1)).toEqual([tl('500.00')]));
    await new Promise((r) => setTimeout(r, 30));
    expect(c.kayit.bilinmeyen(K1)).toHaveLength(1);
  });

  it('çıkış tüm sekmeleri temizler', async () => {
    const a = openItem();
    const b = openItem();
    a.kayit.ekle(K1, tl('500.00'));
    await wait(() => expect(b.form.isRepeat(K1)).toBe(true));
    b.cikis();
    expect(b.form.isRepeat(K1)).toBe(false);
    await wait(() => expect(a.form.isRepeat(K1)).toBe(false));
  });

  it('kanala yalnız anahtar + tutar/döviz/hesap yayılır; fazla alan ve biçimsiz mesaj alınmaz', async () => {
    const listener = new BroadcastChannel(channelName);
    const incoming: unknown[] = [];
    listener.onmessage = (e: MessageEvent<unknown>) => incoming.push(e.data);
    try {
      const a = openItem();
      const personal = { ...tl('500.00'), musteriAd: 'Ece Kaya' } as TahsilatIcerigi;
      a.kayit.ekle(K1, personal);
      await wait(() =>
        expect(incoming).toContainEqual({ tur: 'ekle', anahtar: K1, icerik: tl('500.00') }),
      );
      expect(JSON.stringify(incoming)).not.toContain('Ece');

      // Dışarıdan biçimsiz/fazla alanlı mesaj: fazla alan saklanmaz, biçimsiz yok sayılır.
      listener.postMessage({
        tur: 'ekle',
        anahtar: K2,
        icerik: { ...tl('7.00'), tc: '12345678901' },
      });
      listener.postMessage({
        tur: 'ekle',
        anahtar: K2,
        icerik: { tutar: {}, doviz: 'TRY', hesap: null },
      });
      listener.postMessage('bozuk');
      await wait(() => expect(a.kayit.bilinmeyen(K2)).toEqual([tl('7.00')]));
    } finally {
      listener.close();
    }
  });
});
