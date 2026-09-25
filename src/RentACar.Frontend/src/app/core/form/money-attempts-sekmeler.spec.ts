import {
  EnvironmentInjector,
  createEnvironmentInjector,
  signal,
  type WritableSignal,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { OturumBaglami } from '../oturum/oturum-baglami';
import { OturumServisi } from '../oturum/oturum-servisi';
import { MONEY_SESSION_CHANNEL, type MoneyAttempt, PendingMoneyAttempts } from './money-attempts';

/** Kanal mesajı eşzamansız: yük altında varsayılan 1 sn'den geniş bekle. */
const bekle = (fn: () => void) => vi.waitFor(fn, { timeout: 5000 });
/** Olmaması gereken mesaj için: kanalın teslim edebileceği kadar süre tanınır. */
const sus = () => new Promise((r) => setTimeout(r, 150));

// Anahtarlar elle yazılır (kiracı|kullanıcı|şube) — biçim üretim kodundan TÜRETİLMEZ.
const MERKEZ = 't1|u1|sube-merkez';
const KADIKOY = 't1|u1|sube-kadikoy';
const BASKA_KULLANICI = 't1|u2|sube-merkez';

/** Bir "sekme": kendi enjektörü ve kendi oturum sahtesi; kanal Node'un gerçek `BroadcastChannel`'ı. */
function sekme(kanalAdi: string, ilk: string) {
  const baglam: WritableSignal<OturumBaglami | null> = signal({ anahtar: ilk });
  const yukle = vi.fn(async () => null);
  const enj = createEnvironmentInjector(
    [
      PendingMoneyAttempts,
      { provide: MONEY_SESSION_CHANNEL, useValue: kanalAdi },
      {
        provide: OturumServisi,
        useValue: {
          temizlikKaydet: () => () => undefined,
          baglam: () => baglam(),
          yukle,
        },
      },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  return {
    kayit: enj.get(PendingMoneyAttempts),
    yukle,
    /** Sekmenin kendi `ben`'i değişti (ör. şube kapsamı sunucuda değişip bu sekme yeniden okudu). */
    degistir: (anahtar: string) => {
      baglam.set({ anahtar });
      TestBed.tick();
    },
    kapat: () => enj.destroy(),
  };
}

const deneme = (context: string): MoneyAttempt => ({
  target: 'kira-1',
  path: '/api/ui/v1/kiralar/1/tahsilat',
  method: 'post',
  key: 'k-1',
  body: { tutar: '500.00' },
  content: null,
  formValue: null,
  inFlight: false,
  notice: { tone: 'uyari', title: null, message: 'oturum.pilotDegil', params: {}, detail: null },
  context,
});

describe('PendingMoneyAttempts — sekmeler arası şube değişimi', () => {
  let kanalAdi = '';
  beforeEach(() => {
    kanalAdi = `rc-oturum-baglami-test-${crypto.randomUUID()}`;
  });
  const acik: ReturnType<typeof sekme>[] = [];
  const ac = (ilk: string) => {
    const s = sekme(kanalAdi, ilk);
    acik.push(s);
    TestBed.tick();
    return s;
  };
  afterEach(() => {
    for (const s of acik.splice(0)) s.kapat();
  });

  it('aynı kullanıcının şubesi A sekmesinde değişince B sekmesi `ben`i yeniden okur', async () => {
    const a = ac(MERKEZ);
    const b = ac(MERKEZ);

    a.degistir(KADIKOY);

    await bekle(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    expect(a.yukle).not.toHaveBeenCalled(); // kendi mesajını almaz
  });

  it('#320 M1 korunur: şube değişimi (yayında ve yeniden okumada) B sekmesinin donmuş denemesini DÜŞÜRMEZ', async () => {
    const a = ac(MERKEZ);
    const b = ac(MERKEZ);
    b.kayit.set('tahsilat', deneme('t1|u1'));
    expect(b.kayit.get('tahsilat')?.key).toBe('k-1');

    a.degistir(KADIKOY);
    await bekle(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    b.degistir(KADIKOY); // yeniden okuma B'nin bağlamını da kadıköy yaptı

    expect(b.kayit.get('tahsilat')?.key).toBe('k-1');
    expect(b.kayit.count()).toBe(1);
  });

  it('yakınsar: B eşitlendikten sonra A yeniden okuma yapmaz (sekmeler arası döngü yok)', async () => {
    const a = ac(MERKEZ);
    const b = ac(MERKEZ);

    a.degistir(KADIKOY);
    await bekle(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    b.degistir(KADIKOY); // B duyurur ama A zaten aynı bağlamda

    await sus();
    expect(a.yukle).not.toHaveBeenCalled();
    expect(b.yukle).toHaveBeenCalledTimes(1);
  });

  it('bağlam değişmeden hiçbir sekme yeniden okumaz', async () => {
    const a = ac(MERKEZ);
    const b = ac(MERKEZ);
    a.degistir(MERKEZ); // aynı değer: duyuru yok

    await sus();
    expect(a.yukle).not.toHaveBeenCalled();
    expect(b.yukle).not.toHaveBeenCalled();
  });

  it('regresyon: kimlik değişimi hâlâ öteki sekmeyi yeniden okutur ve kendi sekmesinde denemeleri düşürür', async () => {
    const a = ac(MERKEZ);
    const b = ac(MERKEZ);
    a.kayit.set('tahsilat', deneme('t1|u1'));

    a.degistir(BASKA_KULLANICI);

    await bekle(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    expect(a.kayit.count()).toBe(0);
  });
});
