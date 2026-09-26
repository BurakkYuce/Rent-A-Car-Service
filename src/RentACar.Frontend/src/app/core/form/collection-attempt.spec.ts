import { TestBed } from '@angular/core/testing';
import type { ApiHatasi } from '../api/api-hatasi';
import { SessionService } from '../oturum/session-service';
import {
  COLLECTION_ATTEMPT_CHANNEL,
  CollectionAttemptRecord,
  CollectionAttempt,
  type TahsilatIcerigi,
  unknownOutcomeError,
  amountCleared,
} from './collection-attempt';

const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const hata = (
  code: ApiHatasi['kod'],
  existing?: { tutar: number; ayniIcerik: boolean },
): ApiHatasi =>
  ({
    status: code === 'mukerrer' ? 409 : 0,
    kod: code,
    detay: 'x',
    ...(existing ? { mevcut: { id: 'c', belgeNo: 'T-1', doviz: 'TRY', ...existing } } : {}),
  }) as ApiHatasi;

const tl = (amount: string, account = 'Kasa'): TahsilatIcerigi => ({
  tutar: amount,
  doviz: 'TRY',
  hesap: account,
});

/** Aynı kök kaydı paylaşan iki form (sabit panelde Nakit + Kart; ya da kira listesi + Panel). */
function exchangeRate() {
  let pickup: (() => void) | undefined;
  TestBed.configureTestingModule({
    providers: [
      // Sekme içi mantık: kanal kapalı (sekmeler arası davranış tahsilat-denemesi-sekmeler.spec.ts'te).
      { provide: COLLECTION_ATTEMPT_CHANNEL, useValue: null },
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
  });
  const record = TestBed.inject(CollectionAttemptRecord);
  return {
    a: new CollectionAttempt(record),
    b: new CollectionAttempt(record),
    cikis: () => pickup?.(),
  };
}

describe('TahsilatDenemesi (M-C / 5. tur)', () => {
  it('sonucu bilinmeyen: ağ, 5xx, kodsuz; kesin red değil', () => {
    expect(['ag', 'sunucu', 'bilinmeyen'].map((k) => unknownOutcomeError(hata(k as 'ag')))).toEqual(
      [true, true, true],
    );
    for (const k of ['dogrulama', 'cakisma', 'yetki_yok', 'oturum_yok', 'mukerrer'] as const)
      expect(unknownOutcomeError(hata(k))).toBe(false);
  });

  it('M-C: kaybolan 500 → aynı formda 600 → mevcut(500) eşleşir → "önceki denemeniz kaydedilmiş", tutar temizlenir', () => {
    const { a } = exchangeRate();
    expect(a.errorReceived(a.start(K1, tl('500.00')), hata('ag'))).toBeNull();
    const g = a.start(K1, tl('600.00'));
    expect(g.onceki).toEqual([tl('500.00')]);
    const type = a.errorReceived(g, hata('mukerrer', { tutar: 500, ayniIcerik: false }));
    expect(type).toBe('oncekiDenemeKaydedilmis');
    expect(amountCleared(type!)).toBe(true);
  });

  it("5. tur MEDIUM-1: iz ANAHTARA bağlı — Nakit'te kaybolan 500, Kart'taki 600 gönderiminde görülür", () => {
    const { a: cash, b: card } = exchangeRate();
    cash.errorReceived(cash.start(K1, tl('500.00', 'Kasa')), hata('ag'));
    expect(card.isRepeat(K1)).toBe(true);
    const g = card.start(K1, tl('600.00', 'Banka'));
    expect(card.errorReceived(g, hata('mukerrer', { tutar: 500, ayniIcerik: false }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
    // 409 kaydı silmez: Nakit formu bayat kopyasıyla (K1) farklı tutar gönderse de belirsiz denemeyi görür.
    const n = cash.start(K1, tl('700.00'));
    expect(cash.errorReceived(n, hata('mukerrer', { tutar: 500, ayniIcerik: false }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
  });

  it('ekranlar arası: kira listesinde kaybolan yazım → Panel formu aynı anahtarla farklı tutar → tekrar sayılır', () => {
    const { a: list, b: panel } = exchangeRate();
    list.errorReceived(list.start(K1, tl('1234.50')), hata('sunucu'));
    const g = panel.start(K1, tl('1000.00'));
    expect(panel.errorReceived(g, hata('mukerrer', { tutar: 1234.5, ayniIcerik: false }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
  });

  it('LOW-1: belirsiz deneme var ama mevcut ONUN değil (başka sekme 300 yazdı) → yanlış atıf yok, yine tutar temizlenir', () => {
    const { a } = exchangeRate();
    a.errorReceived(a.start(K1, tl('500.00')), hata('sunucu'));
    const type = a.errorReceived(
      a.start(K1, tl('600.00')),
      hata('mukerrer', { tutar: 300, ayniIcerik: false }),
    );
    expect(type).toBe('baskaIslemDenemeYazilmadi');
    expect(amountCleared(type!)).toBe(true);
  });

  it('M-A: belirsiz deneme yokken farklı içerik → form korunur (temizlenmez)', () => {
    const { a } = exchangeRate();
    const type = a.errorReceived(
      a.start(K1, tl('600.00')),
      hata('mukerrer', { tutar: 300, ayniIcerik: false }),
    );
    expect(type).toBe('baskaIslemYazildi');
    expect(amountCleared(type!)).toBe(false);
  });

  it("LOW-2: mevcut'suz 409 (ilk deneme hâlâ işleniyor) kaydı SİLMEZ", () => {
    const { a } = exchangeRate();
    a.errorReceived(a.start(K1, tl('500.00')), hata('ag'));
    expect(a.errorReceived(a.start(K1, tl('500.00')), hata('mukerrer'))).toBe('bayatAnahtar');
    expect(a.isRepeat(K1)).toBe(true);
    expect(
      a.errorReceived(
        a.start(K1, tl('600.00')),
        hata('mukerrer', { tutar: 500, ayniIcerik: false }),
      ),
    ).toBe('oncekiDenemeKaydedilmis');
  });

  it('kesin red kaydı değiştirmez; 2xx anahtarı kapatır (anahtar tek kayıt taşır); başka anahtar etkilenmez; çıkışta silinir', () => {
    const { a, b, cikis } = exchangeRate();
    a.errorReceived(a.start(K1, tl('500.00')), hata('sunucu'));
    b.errorReceived(b.start(K2, tl('10.00')), hata('ag'));
    expect(a.errorReceived(a.start(K1, tl('500.00')), hata('dogrulama'))).toBeNull();
    expect(a.isRepeat(K1)).toBe(true);
    a.successful(a.start(K1, tl('500.00')));
    expect(a.isRepeat(K1)).toBe(false);
    expect(b.isRepeat(K2)).toBe(true);
    cikis();
    expect(b.isRepeat(K2)).toBe(false);
  });

  it('409 sınıfları: mevcut yok → bayat; aynı içerik → zaten kaydedildi', () => {
    const { a } = exchangeRate();
    expect(a.errorReceived(a.start(K1, tl('1.00')), hata('mukerrer'))).toBe('bayatAnahtar');
    expect(
      a.errorReceived(a.start(K1, tl('1.00')), hata('mukerrer', { tutar: 1, ayniIcerik: true })),
    ).toBe('zatenKaydedildi');
  });

  it('L-2: tutar yenileme yalnız FARKLI anahtarlı ilk tazelemede, bir kez', () => {
    const { a } = exchangeRate();
    expect(a.shouldRefreshAmount(K2)).toBe(false); // istenmedi
    a.requestAmountRefresh(K1);
    expect(a.shouldRefreshAmount(K1)).toBe(false); // aynı (bayat) anahtar
    expect(a.shouldRefreshAmount(undefined)).toBe(false);
    expect(a.shouldRefreshAmount(K2)).toBe(true);
    expect(a.shouldRefreshAmount(K2)).toBe(false);
    a.requestAmountRefresh(K1);
    a.successful(a.start(K2, tl('1.00')));
    expect(a.shouldRefreshAmount(K2)).toBe(false);
  });
});
