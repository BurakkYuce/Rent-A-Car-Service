import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import {
  ListeSorgusu,
  apiParametreleri,
  etkinFiltreSayisi,
  listeTanimi,
  sorguyuCoz,
  sorguyuDegistir,
  urlParametreleri,
} from './liste-sorgusu';

/** Elle kurulmuş örnek katalog (kira listesi benzeri). */
const KIRALAR = listeTanimi({
  filtreler: {
    arama: { tur: 'metin' },
    durum: { tur: 'secim', degerler: ['acik', 'kapali', 'iptal'] },
    baslangic: { tur: 'tarih' },
    enAzTutar: { tur: 'ondalik' },
    gun: { tur: 'tamsayi', enAz: 1, enFazla: 365 },
    kurumsal: { tur: 'bayrak' },
    subeId: { tur: 'kimlik' },
  },
  siralanabilir: ['plaka', 'cikisTarihi', 'tutar'],
  varsayilanSirala: '-cikisTarihi',
});

type KiraSorgusu = ListeSorgusu<typeof KIRALAR.filtreler>;

const VARSAYILAN: KiraSorgusu = { sayfa: 1, boyut: 50, sirala: '-cikisTarihi', filtreler: {} };

describe('liste sorgusu — ayrıştırma (bozuk parametre → varsayılan)', () => {
  it('boş kaynak → varsayılanlar', () => {
    expect(sorguyuCoz(KIRALAR, {})).toEqual(VARSAYILAN);
  });

  it.each([
    ['abc', 1],
    ['0', 1],
    ['-3', 1],
    ['1.5', 1],
    ['1e3', 1],
    ['', 1],
    [' 4 ', 4],
    ['+2', 2],
    ['2147483647', 2147483647],
    ['2147483648', 1],
    ['99999999999999999999999', 1],
    ['7', 7],
  ])('sayfa=%j → %i', (ham, beklenen) => {
    expect(sorguyuCoz(KIRALAR, { sayfa: ham }).sayfa).toBe(beklenen);
  });

  it.each([
    ['500', 200],
    ['201', 200],
    ['200', 200],
    ['1', 1],
    ['0', 1],
    ['-10', 1],
    ['99999999999999999999999', 200],
    ['yirmi', 50],
    ['20.5', 50],
    ['', 50],
    ['25', 25],
  ])('boyut kırpma: boyut=%j → %i (sunucu kuralı 1..200)', (ham, beklenen) => {
    expect(sorguyuCoz(KIRALAR, { boyut: ham }).boyut).toBe(beklenen);
  });

  it.each([
    ['plaka', 'plaka'],
    ['-tutar', '-tutar'],
    [' -plaka ', '-plaka'],
    ['bilinmeyen', '-cikisTarihi'],
    ['-', '-cikisTarihi'],
    ['--plaka', '-cikisTarihi'],
    ['', '-cikisTarihi'],
    ['PLAKA', '-cikisTarihi'],
  ])('sirala=%j → %j (beyaz liste; bilinmeyen alan 400 üretmesin)', (ham, beklenen) => {
    expect(sorguyuCoz(KIRALAR, { sirala: ham }).sirala).toBe(beklenen);
  });

  it('filtre türleri: geçerliler okunur, geçersizler düşer', () => {
    const gecerli = sorguyuCoz(KIRALAR, {
      arama: '  İzmir  ',
      durum: 'kapali',
      baslangic: '2024-02-29',
      enAzTutar: '1250.75',
      gun: '30',
      kurumsal: '1',
      subeId: '3F2504E0-4F89-11D3-9A0C-0305E82C3301',
    });
    expect(gecerli.filtreler).toEqual({
      arama: 'İzmir',
      durum: 'kapali',
      baslangic: '2024-02-29',
      enAzTutar: 1250.75,
      gun: 30,
      kurumsal: true,
      subeId: '3F2504E0-4F89-11D3-9A0C-0305E82C3301',
    });

    const gecersiz = sorguyuCoz(KIRALAR, {
      arama: '   ',
      durum: 'Kapali',
      baslangic: '2026-02-30',
      enAzTutar: '1.250,75',
      gun: '366',
      kurumsal: 'evet',
      subeId: 'sube-1',
    });
    expect(gecersiz.filtreler).toEqual({});
  });

  it.each([
    ['2026-13-01'],
    ['2026-00-10'],
    ['2026-04-31'],
    ['0099-01-01'],
    ['26-01-01'],
    ['2026-1-1'],
    ['2026-01-01T00:00'],
  ])('geçersiz tarih %j düşer', (ham) => {
    expect(sorguyuCoz(KIRALAR, { baslangic: ham }).filtreler.baslangic).toBeUndefined();
  });

  it('tekrarlanan parametre (dizi) → ilk değer', () => {
    expect(sorguyuCoz(KIRALAR, { durum: ['acik', 'kapali'], sayfa: ['3', '9'] })).toMatchObject({
      sayfa: 3,
      filtreler: { durum: 'acik' },
    });
  });

  it('metin filtresi 200 karakterde kesilir, NFC’ye normalize edilir', () => {
    const uzun = 'ş'.repeat(250);
    expect(sorguyuCoz(KIRALAR, { arama: uzun }).filtreler.arama).toBe('ş'.repeat(200));
    // "ş" ayrık biçimde (s + U+0327) → birleşik "ş"
    expect(sorguyuCoz(KIRALAR, { arama: 'Kaş' }).filtreler.arama).toBe('Kaş');
  });

  it('tanım doğrulaması: ayrılmış ad ve listede olmayan varsayılan sıralama reddedilir', () => {
    expect(() => listeTanimi({ filtreler: { sayfa: { tur: 'metin' } } })).toThrow();
    expect(() =>
      listeTanimi({ filtreler: {}, siralanabilir: ['a'], varsayilanSirala: '-b' }),
    ).toThrow();
    expect(() => listeTanimi({ filtreler: {}, varsayilanBoyut: 500 })).toThrow();
  });
});

describe('liste sorgusu — URL gidiş-dönüş (gerçek Router kodlamasıyla)', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    router = TestBed.inject(Router);
  });

  /** Sorgu → URL parametreleri → Router ile URL METNİNE seri hâle → geri ayrıştır → sorgu. */
  function gidisDonus(sorgu: KiraSorgusu): { url: string; geri: KiraSorgusu } {
    const parametreler = Object.fromEntries(
      Object.entries(urlParametreleri(KIRALAR, sorgu)).filter(([, v]) => v !== null),
    );
    const url = router.serializeUrl(
      router.createUrlTree(['/kiralar'], { queryParams: parametreler }),
    );
    const geri = sorguyuCoz(KIRALAR, router.parseUrl(url).queryParamMap);
    return { url, geri };
  }

  const tablo: readonly [string, KiraSorgusu, string][] = [
    ['varsayılan → temiz URL', VARSAYILAN, '/kiralar'],
    [
      'sayfa + boyut + artan sıralama',
      { ...VARSAYILAN, sayfa: 3, boyut: 100, sirala: 'plaka' },
      '/kiralar?sayfa=3&boyut=100&sirala=plaka',
    ],
    [
      'Türkçe karakterli arama',
      { ...VARSAYILAN, filtreler: { arama: 'Şişli Çağlayan İĞDIR ığdır öü' } },
      '/kiralar?arama=%C5%9Ei%C5%9Fli%20%C3%87a%C4%9Flayan%20%C4%B0%C4%9EDIR%20%C4%B1%C4%9Fd%C4%B1r%20%C3%B6%C3%BC',
    ],
    [
      'URL için özel karakterler',
      { ...VARSAYILAN, filtreler: { arama: 'a&b=c?d#e/f+g%h' } },
      '/kiralar?arama=a%26b%3Dc%3Fd%23e%2Ff%2Bg%25h',
    ],
    [
      'tüm filtre türleri',
      {
        ...VARSAYILAN,
        filtreler: {
          durum: 'iptal',
          baslangic: '2026-09-21',
          enAzTutar: 99.5,
          gun: 7,
          kurumsal: false,
          subeId: '3f2504e0-4f89-11d3-9a0c-0305e82c3301',
        },
      },
      '/kiralar?durum=iptal&baslangic=2026-09-21&enAzTutar=99.5&gun=7&kurumsal=false&subeId=3f2504e0-4f89-11d3-9a0c-0305e82c3301',
    ],
  ];

  it.each(tablo)('%s', (_ad, sorgu, beklenenUrl) => {
    const { url, geri } = gidisDonus(sorgu);
    expect(url).toBe(beklenenUrl);
    expect(geri).toEqual(sorgu);
  });

  it('varsayılan değerler URL parametresinden SİLİNİR (null)', () => {
    expect(urlParametreleri(KIRALAR, VARSAYILAN)).toEqual({
      sayfa: null,
      boyut: null,
      sirala: null,
      arama: null,
      durum: null,
      baslangic: null,
      enAzTutar: null,
      gun: null,
      kurumsal: null,
      subeId: null,
    });
  });
});

describe('liste sorgusu — değişiklik ve API parametreleri', () => {
  const besinciSayfa: KiraSorgusu = { ...VARSAYILAN, sayfa: 5 };

  it('filtre değişince sayfa 1’e döner', () => {
    expect(sorguyuDegistir(KIRALAR, besinciSayfa, { filtreler: { arama: 'Ankara' } })).toEqual({
      ...VARSAYILAN,
      filtreler: { arama: 'Ankara' },
    });
  });

  it('sıralama ya da boyut değişince sayfa 1’e döner; yalnız sayfa değişince korunur', () => {
    expect(sorguyuDegistir(KIRALAR, besinciSayfa, { sirala: 'tutar' }).sayfa).toBe(1);
    expect(sorguyuDegistir(KIRALAR, besinciSayfa, { boyut: 20 }).sayfa).toBe(1);
    expect(sorguyuDegistir(KIRALAR, besinciSayfa, { sayfa: 6 }).sayfa).toBe(6);
  });

  it('aynı filtre değeri yeniden yazılırsa sayfa korunur', () => {
    const mevcut: KiraSorgusu = { ...besinciSayfa, filtreler: { arama: 'Ankara' } };
    expect(sorguyuDegistir(KIRALAR, mevcut, { filtreler: { arama: '  Ankara ' } }).sayfa).toBe(5);
  });

  it('undefined filtreyi kaldırır; diğer filtreler korunur', () => {
    const mevcut: KiraSorgusu = { ...VARSAYILAN, filtreler: { arama: 'x', durum: 'acik' } };
    expect(sorguyuDegistir(KIRALAR, mevcut, { filtreler: { arama: undefined } }).filtreler).toEqual(
      { durum: 'acik' },
    );
  });

  it('boyut değişikliği de kırpılır', () => {
    expect(sorguyuDegistir(KIRALAR, VARSAYILAN, { boyut: 1000 }).boyut).toBe(200);
  });

  it('API parametreleri: sayfa/boyut daima, sıralama ve dolu filtreler', () => {
    expect(
      apiParametreleri(KIRALAR, { ...VARSAYILAN, filtreler: { arama: 'İzmir', kurumsal: true } }),
    ).toEqual({ sayfa: 1, boyut: 50, sirala: '-cikisTarihi', arama: 'İzmir', kurumsal: 'true' });

    const siralamasiz = listeTanimi({ filtreler: {} });
    expect(apiParametreleri(siralamasiz, sorguyuCoz(siralamasiz, {}))).toEqual({
      sayfa: 1,
      boyut: 50,
    });
  });

  it('etkin filtre sayısı', () => {
    expect(etkinFiltreSayisi(KIRALAR, VARSAYILAN)).toBe(0);
    expect(
      etkinFiltreSayisi(KIRALAR, { ...VARSAYILAN, filtreler: { arama: 'a', kurumsal: false } }),
    ).toBe(2);
  });
});
