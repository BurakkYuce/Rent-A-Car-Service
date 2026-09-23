import { TestBed } from '@angular/core/testing';
import type { ApiHatasi } from '../api/api-hatasi';
import { OturumServisi } from '../oturum/oturum-servisi';
import {
  TahsilatDenemeKaydi,
  TahsilatDenemesi,
  type TahsilatIcerigi,
  sonucuBilinmeyenHata,
  tutarTemizlenir,
} from './tahsilat-denemesi';

const K1 = 'aaaaaaaa-0000-4000-8000-000000000001';
const K2 = 'aaaaaaaa-0000-4000-8000-000000000002';

const hata = (kod: ApiHatasi['kod'], mevcut?: { tutar: number; ayniIcerik: boolean }): ApiHatasi =>
  ({
    status: kod === 'mukerrer' ? 409 : 0,
    kod,
    detay: 'x',
    ...(mevcut ? { mevcut: { id: 'c', belgeNo: 'T-1', doviz: 'TRY', ...mevcut } } : {}),
  }) as ApiHatasi;

const tl = (tutar: string, hesap = 'Kasa'): TahsilatIcerigi => ({ tutar, doviz: 'TRY', hesap });

/** Aynı kök kaydı paylaşan iki form (sabit panelde Nakit + Kart; ya da kira listesi + Panel). */
function kur() {
  let cikis: (() => void) | undefined;
  TestBed.configureTestingModule({
    providers: [
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
  });
  const kayit = TestBed.inject(TahsilatDenemeKaydi);
  return {
    a: new TahsilatDenemesi(kayit),
    b: new TahsilatDenemesi(kayit),
    cikis: () => cikis?.(),
  };
}

describe('TahsilatDenemesi (M-C / 5. tur)', () => {
  it('sonucu bilinmeyen: ağ, 5xx, kodsuz; kesin red değil', () => {
    expect(
      ['ag', 'sunucu', 'bilinmeyen'].map((k) => sonucuBilinmeyenHata(hata(k as 'ag'))),
    ).toEqual([true, true, true]);
    for (const k of ['dogrulama', 'cakisma', 'yetki_yok', 'oturum_yok', 'mukerrer'] as const)
      expect(sonucuBilinmeyenHata(hata(k))).toBe(false);
  });

  it('M-C: kaybolan 500 → aynı formda 600 → mevcut(500) eşleşir → "önceki denemeniz kaydedilmiş", tutar temizlenir', () => {
    const { a } = kur();
    expect(a.hataGeldi(a.basla(K1, tl('500.00')), hata('ag'))).toBeNull();
    const g = a.basla(K1, tl('600.00'));
    expect(g.onceki).toEqual([tl('500.00')]);
    const tur = a.hataGeldi(g, hata('mukerrer', { tutar: 500, ayniIcerik: false }));
    expect(tur).toBe('oncekiDenemeKaydedilmis');
    expect(tutarTemizlenir(tur!)).toBe(true);
  });

  it("5. tur MEDIUM-1: iz ANAHTARA bağlı — Nakit'te kaybolan 500, Kart'taki 600 gönderiminde görülür", () => {
    const { a: nakit, b: kart } = kur();
    nakit.hataGeldi(nakit.basla(K1, tl('500.00', 'Kasa')), hata('ag'));
    expect(kart.tekrarMi(K1)).toBe(true);
    const g = kart.basla(K1, tl('600.00', 'Banka'));
    expect(kart.hataGeldi(g, hata('mukerrer', { tutar: 500, ayniIcerik: false }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
    // 409 kaydı silmez: Nakit formu bayat kopyasıyla (K1) farklı tutar gönderse de belirsiz denemeyi görür.
    const n = nakit.basla(K1, tl('700.00'));
    expect(nakit.hataGeldi(n, hata('mukerrer', { tutar: 500, ayniIcerik: false }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
  });

  it('ekranlar arası: kira listesinde kaybolan yazım → Panel formu aynı anahtarla farklı tutar → tekrar sayılır', () => {
    const { a: liste, b: panel } = kur();
    liste.hataGeldi(liste.basla(K1, tl('1234.50')), hata('sunucu'));
    const g = panel.basla(K1, tl('1000.00'));
    expect(panel.hataGeldi(g, hata('mukerrer', { tutar: 1234.5, ayniIcerik: false }))).toBe(
      'oncekiDenemeKaydedilmis',
    );
  });

  it('LOW-1: belirsiz deneme var ama mevcut ONUN değil (başka sekme 300 yazdı) → yanlış atıf yok, yine tutar temizlenir', () => {
    const { a } = kur();
    a.hataGeldi(a.basla(K1, tl('500.00')), hata('sunucu'));
    const tur = a.hataGeldi(
      a.basla(K1, tl('600.00')),
      hata('mukerrer', { tutar: 300, ayniIcerik: false }),
    );
    expect(tur).toBe('baskaIslemDenemeYazilmadi');
    expect(tutarTemizlenir(tur!)).toBe(true);
  });

  it('M-A: belirsiz deneme yokken farklı içerik → form korunur (temizlenmez)', () => {
    const { a } = kur();
    const tur = a.hataGeldi(
      a.basla(K1, tl('600.00')),
      hata('mukerrer', { tutar: 300, ayniIcerik: false }),
    );
    expect(tur).toBe('baskaIslemYazildi');
    expect(tutarTemizlenir(tur!)).toBe(false);
  });

  it("LOW-2: mevcut'suz 409 (ilk deneme hâlâ işleniyor) kaydı SİLMEZ", () => {
    const { a } = kur();
    a.hataGeldi(a.basla(K1, tl('500.00')), hata('ag'));
    expect(a.hataGeldi(a.basla(K1, tl('500.00')), hata('mukerrer'))).toBe('bayatAnahtar');
    expect(a.tekrarMi(K1)).toBe(true);
    expect(
      a.hataGeldi(a.basla(K1, tl('600.00')), hata('mukerrer', { tutar: 500, ayniIcerik: false })),
    ).toBe('oncekiDenemeKaydedilmis');
  });

  it('kesin red kaydı değiştirmez; 2xx anahtarı kapatır (anahtar tek kayıt taşır); başka anahtar etkilenmez; çıkışta silinir', () => {
    const { a, b, cikis } = kur();
    a.hataGeldi(a.basla(K1, tl('500.00')), hata('sunucu'));
    b.hataGeldi(b.basla(K2, tl('10.00')), hata('ag'));
    expect(a.hataGeldi(a.basla(K1, tl('500.00')), hata('dogrulama'))).toBeNull();
    expect(a.tekrarMi(K1)).toBe(true);
    a.basarili(a.basla(K1, tl('500.00')));
    expect(a.tekrarMi(K1)).toBe(false);
    expect(b.tekrarMi(K2)).toBe(true);
    cikis();
    expect(b.tekrarMi(K2)).toBe(false);
  });

  it('409 sınıfları: mevcut yok → bayat; aynı içerik → zaten kaydedildi', () => {
    const { a } = kur();
    expect(a.hataGeldi(a.basla(K1, tl('1.00')), hata('mukerrer'))).toBe('bayatAnahtar');
    expect(
      a.hataGeldi(a.basla(K1, tl('1.00')), hata('mukerrer', { tutar: 1, ayniIcerik: true })),
    ).toBe('zatenKaydedildi');
  });

  it('L-2: tutar yenileme yalnız FARKLI anahtarlı ilk tazelemede, bir kez', () => {
    const { a } = kur();
    expect(a.tutarYenilensinMi(K2)).toBe(false); // istenmedi
    a.tutarYenilemesiIste(K1);
    expect(a.tutarYenilensinMi(K1)).toBe(false); // aynı (bayat) anahtar
    expect(a.tutarYenilensinMi(undefined)).toBe(false);
    expect(a.tutarYenilensinMi(K2)).toBe(true);
    expect(a.tutarYenilensinMi(K2)).toBe(false);
    a.tutarYenilemesiIste(K1);
    a.basarili(a.basla(K2, tl('1.00')));
    expect(a.tutarYenilensinMi(K2)).toBe(false);
  });
});
