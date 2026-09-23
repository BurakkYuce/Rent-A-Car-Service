import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ApiHatasi } from '../api/api-hatasi';
import { OturumServisi } from '../oturum/oturum-servisi';
import {
  TAHSILAT_DENEME_KANALI,
  TahsilatDenemeKaydi,
  TahsilatDenemesi,
  type TahsilatIcerigi,
} from './tahsilat-denemesi';

const K1 = 'bbbbbbbb-0000-4000-8000-000000000001';
const K2 = 'bbbbbbbb-0000-4000-8000-000000000002';
const tl = (tutar: string, hesap = 'Kasa'): TahsilatIcerigi => ({ tutar, doviz: 'TRY', hesap });
const hata = (kod: ApiHatasi['kod'], mevcut?: { tutar: number }): ApiHatasi =>
  ({
    status: kod === 'mukerrer' ? 409 : 0,
    kod,
    detay: 'x',
    ...(mevcut
      ? { mevcut: { id: 'c', belgeNo: 'T-1', doviz: 'TRY', ayniIcerik: false, ...mevcut } }
      : {}),
  }) as ApiHatasi;

/** Kanal mesajı eşzamansız: yük altında (CI, soğuk derleme) varsayılan 1 sn'den geniş bekle. */
const bekle = (fn: () => void) => vi.waitFor(fn, { timeout: 5000 });

/**
 * Bir "sekme": kendi enjektörü, kendi kök kaydı (aynı tarayıcıda her sekmenin ayrı uygulaması olur). Kanal Node'un
 * gerçek `BroadcastChannel`'ıdır — mesajlar eşzamansız ulaşır (`vi.waitFor`).
 */
function sekme(kanalAdi: string) {
  let cikis: (() => void) | undefined;
  const enj = createEnvironmentInjector(
    [
      TahsilatDenemeKaydi,
      { provide: TAHSILAT_DENEME_KANALI, useValue: kanalAdi },
      {
        provide: OturumServisi,
        useValue: {
          temizlikKaydet: (fn: () => void) => {
            cikis = fn;
            return () => undefined;
          },
        },
      },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  const kayit = enj.get(TahsilatDenemeKaydi);
  return {
    kayit,
    form: new TahsilatDenemesi(kayit),
    cikis: () => cikis?.(),
    kapat: () => enj.destroy(),
  };
}

describe('TahsilatDenemeKaydi — sekmeler arası (BroadcastChannel)', () => {
  // Test başına ayrı kanal: önceki testin (ya da aynı süreçteki başka dosyanın) trafiği karışmaz.
  let kanalAdi = '';
  beforeEach(() => {
    kanalAdi = `rc-tahsilat-denemesi-test-${crypto.randomUUID()}`;
  });
  const acik: ReturnType<typeof sekme>[] = [];
  const ac = () => {
    const s = sekme(kanalAdi);
    acik.push(s);
    return s;
  };
  afterEach(() => {
    for (const s of acik.splice(0)) s.kapat();
  });

  it("A sekmesinde kaybolan 500, B sekmesinde 600 basılınca TEKRAR sayılır ve mevcut(500) 'önceki deneme' olur", async () => {
    const a = ac();
    const b = ac();
    expect(a.form.hataGeldi(a.form.basla(K1, tl('500.00')), hata('sunucu'))).toBeNull();

    await bekle(() => expect(b.kayit.bilinmeyen(K1)).toEqual([tl('500.00')]));
    expect(b.form.tekrarMi(K1)).toBe(true);
    const g = b.form.basla(K1, tl('600.00'));
    expect(b.form.hataGeldi(g, hata('mukerrer', { tutar: 500 }))).toBe('oncekiDenemeKaydedilmis');
    expect(b.kayit.bilinmeyen(K2)).toEqual([]); // başka anahtar etkilenmez
  });

  it('B sekmesindeki 2xx anahtarı A sekmesinde de kapatır', async () => {
    const a = ac();
    const b = ac();
    a.kayit.ekle(K1, tl('500.00'));
    await bekle(() => expect(b.form.tekrarMi(K1)).toBe(true));
    b.form.basarili(b.form.basla(K1, tl('500.00')));
    await bekle(() => expect(a.form.tekrarMi(K1)).toBe(false));
  });

  it('sonradan açılan sekme mevcut durumu alır; iki sekme yanıtlasa da içerik bir kez eklenir', async () => {
    const a = ac();
    const b = ac();
    a.kayit.ekle(K1, tl('500.00'));
    await bekle(() => expect(b.kayit.bilinmeyen(K1)).toHaveLength(1));
    const c = ac(); // a ve b ikisi de "durum" yanıtlar
    await bekle(() => expect(c.kayit.bilinmeyen(K1)).toEqual([tl('500.00')]));
    await new Promise((r) => setTimeout(r, 30));
    expect(c.kayit.bilinmeyen(K1)).toHaveLength(1);
  });

  it('çıkış tüm sekmeleri temizler', async () => {
    const a = ac();
    const b = ac();
    a.kayit.ekle(K1, tl('500.00'));
    await bekle(() => expect(b.form.tekrarMi(K1)).toBe(true));
    b.cikis();
    expect(b.form.tekrarMi(K1)).toBe(false);
    await bekle(() => expect(a.form.tekrarMi(K1)).toBe(false));
  });

  it('kanala yalnız anahtar + tutar/döviz/hesap yayılır; fazla alan ve biçimsiz mesaj alınmaz', async () => {
    const dinleyici = new BroadcastChannel(kanalAdi);
    const gelen: unknown[] = [];
    dinleyici.onmessage = (e: MessageEvent<unknown>) => gelen.push(e.data);
    try {
      const a = ac();
      const kisisel = { ...tl('500.00'), musteriAd: 'Ece Kaya' } as TahsilatIcerigi;
      a.kayit.ekle(K1, kisisel);
      await bekle(() =>
        expect(gelen).toContainEqual({ tur: 'ekle', anahtar: K1, icerik: tl('500.00') }),
      );
      expect(JSON.stringify(gelen)).not.toContain('Ece');

      // Dışarıdan biçimsiz/fazla alanlı mesaj: fazla alan saklanmaz, biçimsiz yok sayılır.
      dinleyici.postMessage({
        tur: 'ekle',
        anahtar: K2,
        icerik: { ...tl('7.00'), tc: '12345678901' },
      });
      dinleyici.postMessage({
        tur: 'ekle',
        anahtar: K2,
        icerik: { tutar: {}, doviz: 'TRY', hesap: null },
      });
      dinleyici.postMessage('bozuk');
      await bekle(() => expect(a.kayit.bilinmeyen(K2)).toEqual([tl('7.00')]));
    } finally {
      dinleyici.close();
    }
  });
});
