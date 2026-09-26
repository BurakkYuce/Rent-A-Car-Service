import {
  EnvironmentInjector,
  createEnvironmentInjector,
  signal,
  type WritableSignal,
} from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { OturumBaglami } from '../oturum/oturum-baglami';
import { SessionService } from '../oturum/session-service';
import { MONEY_SESSION_CHANNEL, type MoneyAttempt, PendingMoneyAttempts } from './money-attempts';

/** Kanal mesajı eşzamansız: yük altında varsayılan 1 sn'den geniş bekle. */
const wait = (fn: () => void) => vi.waitFor(fn, { timeout: 5000 });
/** Olmaması gereken mesaj için: kanalın teslim edebileceği kadar süre tanınır. */
const sus = () => new Promise((r) => setTimeout(r, 150));

// Anahtarlar elle yazılır (kiracı|kullanıcı|şube) — biçim üretim kodundan TÜRETİLMEZ.
const HEAD_OFFICE = 't1|u1|sube-merkez';
const KADIKOY = 't1|u1|sube-kadikoy';
const OTHER_USER = 't1|u2|sube-merkez';

/** Bir "sekme": kendi enjektörü ve kendi oturum sahtesi; kanal Node'un gerçek `BroadcastChannel`'ı. */
function sekme(channelName: string, first: string) {
  const context: WritableSignal<OturumBaglami | null> = signal({ anahtar: first });
  const load = vi.fn(async () => null);
  const enj = createEnvironmentInjector(
    [
      PendingMoneyAttempts,
      { provide: MONEY_SESSION_CHANNEL, useValue: channelName },
      {
        provide: SessionService,
        useValue: {
          registerCleanup: () => () => undefined,
          context: () => context(),
          yukle: load,
        },
      },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  return {
    kayit: enj.get(PendingMoneyAttempts),
    yukle: load,
    /** Sekmenin kendi `ben`'i değişti (ör. şube kapsamı sunucuda değişip bu sekme yeniden okudu). */
    degistir: (key: string) => {
      context.set({ anahtar: key });
      TestBed.tick();
    },
    kapat: () => enj.destroy(),
  };
}

const attempt = (context: string): MoneyAttempt => ({
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
  let channelName = '';
  beforeEach(() => {
    channelName = `rc-oturum-baglami-test-${crypto.randomUUID()}`;
  });
  const open: ReturnType<typeof sekme>[] = [];
  const openItem = (first: string) => {
    const s = sekme(channelName, first);
    open.push(s);
    TestBed.tick();
    return s;
  };
  afterEach(() => {
    for (const s of open.splice(0)) s.kapat();
  });

  it('aynı kullanıcının şubesi A sekmesinde değişince B sekmesi `ben`i yeniden okur', async () => {
    const a = openItem(HEAD_OFFICE);
    const b = openItem(HEAD_OFFICE);

    a.degistir(KADIKOY);

    await wait(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    expect(a.yukle).not.toHaveBeenCalled(); // kendi mesajını almaz
  });

  it('#320 M1 korunur: şube değişimi (yayında ve yeniden okumada) B sekmesinin donmuş denemesini DÜŞÜRMEZ', async () => {
    const a = openItem(HEAD_OFFICE);
    const b = openItem(HEAD_OFFICE);
    b.kayit.set('tahsilat', attempt('t1|u1'));
    expect(b.kayit.get('tahsilat')?.key).toBe('k-1');

    a.degistir(KADIKOY);
    await wait(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    b.degistir(KADIKOY); // yeniden okuma B'nin bağlamını da kadıköy yaptı

    expect(b.kayit.get('tahsilat')?.key).toBe('k-1');
    expect(b.kayit.count()).toBe(1);
  });

  it('yakınsar: B eşitlendikten sonra A yeniden okuma yapmaz (sekmeler arası döngü yok)', async () => {
    const a = openItem(HEAD_OFFICE);
    const b = openItem(HEAD_OFFICE);

    a.degistir(KADIKOY);
    await wait(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    b.degistir(KADIKOY); // B duyurur ama A zaten aynı bağlamda

    await sus();
    expect(a.yukle).not.toHaveBeenCalled();
    expect(b.yukle).toHaveBeenCalledTimes(1);
  });

  it('bağlam değişmeden hiçbir sekme yeniden okumaz', async () => {
    const a = openItem(HEAD_OFFICE);
    const b = openItem(HEAD_OFFICE);
    a.degistir(HEAD_OFFICE); // aynı değer: duyuru yok

    await sus();
    expect(a.yukle).not.toHaveBeenCalled();
    expect(b.yukle).not.toHaveBeenCalled();
  });

  it('regresyon: kimlik değişimi hâlâ öteki sekmeyi yeniden okutur ve kendi sekmesinde denemeleri düşürür', async () => {
    const a = openItem(HEAD_OFFICE);
    const b = openItem(HEAD_OFFICE);
    a.kayit.set('tahsilat', attempt('t1|u1'));

    a.degistir(OTHER_USER);

    await wait(() => expect(b.yukle).toHaveBeenCalledTimes(1));
    expect(a.kayit.count()).toBe(0);
  });
});
