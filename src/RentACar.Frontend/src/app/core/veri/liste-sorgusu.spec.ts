import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import {
  ListeSorgusu,
  apiParams,
  activeFilterCount,
  listDefinition,
  parseQuery,
  changeQuery,
  urlParameters,
} from './liste-sorgusu';

/** Elle kurulmuş örnek katalog (kira listesi benzeri). */
const KIRALAR = listDefinition({
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

type RentalQuery = ListeSorgusu<typeof KIRALAR.filtreler>;

const DEFAULT: RentalQuery = { sayfa: 1, boyut: 50, sirala: '-cikisTarihi', filtreler: {} };

describe('liste sorgusu — ayrıştırma (bozuk parametre → varsayılan)', () => {
  it('boş kaynak → varsayılanlar', () => {
    expect(parseQuery(KIRALAR, {})).toEqual(DEFAULT);
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
  ])('sayfa=%j → %i', (raw, expected) => {
    expect(parseQuery(KIRALAR, { sayfa: raw }).sayfa).toBe(expected);
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
  ])('boyut kırpma: boyut=%j → %i (sunucu kuralı 1..200)', (raw, expected) => {
    expect(parseQuery(KIRALAR, { boyut: raw }).boyut).toBe(expected);
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
  ])('sirala=%j → %j (beyaz liste; bilinmeyen alan 400 üretmesin)', (raw, expected) => {
    expect(parseQuery(KIRALAR, { sirala: raw }).sirala).toBe(expected);
  });

  it('filtre türleri: geçerliler okunur, geçersizler düşer', () => {
    const valid = parseQuery(KIRALAR, {
      arama: '  İzmir  ',
      durum: 'kapali',
      baslangic: '2024-02-29',
      enAzTutar: '1250.75',
      gun: '30',
      kurumsal: '1',
      subeId: '3F2504E0-4F89-11D3-9A0C-0305E82C3301',
    });
    expect(valid.filtreler).toEqual({
      arama: 'İzmir',
      durum: 'kapali',
      baslangic: '2024-02-29',
      enAzTutar: 1250.75,
      gun: 30,
      kurumsal: true,
      subeId: '3F2504E0-4F89-11D3-9A0C-0305E82C3301',
    });

    const invalid = parseQuery(KIRALAR, {
      arama: '   ',
      durum: 'Kapali',
      baslangic: '2026-02-30',
      enAzTutar: '1.250,75',
      gun: '366',
      kurumsal: 'evet',
      subeId: 'sube-1',
    });
    expect(invalid.filtreler).toEqual({});
  });

  it.each([
    ['2026-13-01'],
    ['2026-00-10'],
    ['2026-04-31'],
    ['0099-01-01'],
    ['26-01-01'],
    ['2026-1-1'],
    ['2026-01-01T00:00'],
  ])('geçersiz tarih %j düşer', (raw) => {
    expect(parseQuery(KIRALAR, { baslangic: raw }).filtreler.baslangic).toBeUndefined();
  });

  it('tekrarlanan parametre (dizi) → ilk değer', () => {
    expect(parseQuery(KIRALAR, { durum: ['acik', 'kapali'], sayfa: ['3', '9'] })).toMatchObject({
      sayfa: 3,
      filtreler: { durum: 'acik' },
    });
  });

  it('metin filtresi 200 karakterde kesilir, NFC’ye normalize edilir', () => {
    const longText = 'ş'.repeat(250);
    expect(parseQuery(KIRALAR, { arama: longText }).filtreler.arama).toBe('ş'.repeat(200));
    // "ş" ayrık biçimde (s + U+0327) → birleşik "ş"
    expect(parseQuery(KIRALAR, { arama: 'Kaş' }).filtreler.arama).toBe('Kaş');
  });

  it('tanım doğrulaması: ayrılmış ad ve listede olmayan varsayılan sıralama reddedilir', () => {
    expect(() => listDefinition({ filtreler: { sayfa: { tur: 'metin' } } })).toThrow();
    expect(() =>
      listDefinition({ filtreler: {}, siralanabilir: ['a'], varsayilanSirala: '-b' }),
    ).toThrow();
    expect(() => listDefinition({ filtreler: {}, varsayilanBoyut: 500 })).toThrow();
  });
});

describe('liste sorgusu — URL gidiş-dönüş (gerçek Router kodlamasıyla)', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    router = TestBed.inject(Router);
  });

  /** Sorgu → URL parametreleri → Router ile URL METNİNE seri hâle → geri ayrıştır → sorgu. */
  function roundTrip(query: RentalQuery): { url: string; geri: RentalQuery } {
    const parameters = Object.fromEntries(
      Object.entries(urlParameters(KIRALAR, query)).filter(([, v]) => v !== null),
    );
    const url = router.serializeUrl(
      router.createUrlTree(['/kiralar'], { queryParams: parameters }),
    );
    const back = parseQuery(KIRALAR, router.parseUrl(url).queryParamMap);
    return { url, geri: back };
  }

  const table: readonly [string, RentalQuery, string][] = [
    ['varsayılan → temiz URL', DEFAULT, '/kiralar'],
    [
      'sayfa + boyut + artan sıralama',
      { ...DEFAULT, sayfa: 3, boyut: 100, sirala: 'plaka' },
      '/kiralar?sayfa=3&boyut=100&sirala=plaka',
    ],
    [
      'Türkçe karakterli arama',
      { ...DEFAULT, filtreler: { arama: 'Şişli Çağlayan İĞDIR ığdır öü' } },
      '/kiralar?arama=%C5%9Ei%C5%9Fli%20%C3%87a%C4%9Flayan%20%C4%B0%C4%9EDIR%20%C4%B1%C4%9Fd%C4%B1r%20%C3%B6%C3%BC',
    ],
    [
      'URL için özel karakterler',
      { ...DEFAULT, filtreler: { arama: 'a&b=c?d#e/f+g%h' } },
      '/kiralar?arama=a%26b%3Dc%3Fd%23e%2Ff%2Bg%25h',
    ],
    [
      'tüm filtre türleri',
      {
        ...DEFAULT,
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

  it.each(table)('%s', (_name, query, expectedUrl) => {
    const { url, geri } = roundTrip(query);
    expect(url).toBe(expectedUrl);
    expect(geri).toEqual(query);
  });

  it('varsayılan değerler URL parametresinden SİLİNİR (null)', () => {
    expect(urlParameters(KIRALAR, DEFAULT)).toEqual({
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
  const fifthPage: RentalQuery = { ...DEFAULT, sayfa: 5 };

  it('filtre değişince sayfa 1’e döner', () => {
    expect(changeQuery(KIRALAR, fifthPage, { filtreler: { arama: 'Ankara' } })).toEqual({
      ...DEFAULT,
      filtreler: { arama: 'Ankara' },
    });
  });

  it('sıralama ya da boyut değişince sayfa 1’e döner; yalnız sayfa değişince korunur', () => {
    expect(changeQuery(KIRALAR, fifthPage, { sirala: 'tutar' }).sayfa).toBe(1);
    expect(changeQuery(KIRALAR, fifthPage, { boyut: 20 }).sayfa).toBe(1);
    expect(changeQuery(KIRALAR, fifthPage, { sayfa: 6 }).sayfa).toBe(6);
  });

  it('aynı filtre değeri yeniden yazılırsa sayfa korunur', () => {
    const existing: RentalQuery = { ...fifthPage, filtreler: { arama: 'Ankara' } };
    expect(changeQuery(KIRALAR, existing, { filtreler: { arama: '  Ankara ' } }).sayfa).toBe(5);
  });

  it('undefined filtreyi kaldırır; diğer filtreler korunur', () => {
    const existing: RentalQuery = { ...DEFAULT, filtreler: { arama: 'x', durum: 'acik' } };
    expect(changeQuery(KIRALAR, existing, { filtreler: { arama: undefined } }).filtreler).toEqual({
      durum: 'acik',
    });
  });

  it('boyut değişikliği de kırpılır', () => {
    expect(changeQuery(KIRALAR, DEFAULT, { boyut: 1000 }).boyut).toBe(200);
  });

  it('API parametreleri: sayfa/boyut daima, sıralama ve dolu filtreler', () => {
    expect(
      apiParams(KIRALAR, { ...DEFAULT, filtreler: { arama: 'İzmir', kurumsal: true } }),
    ).toEqual({ sayfa: 1, boyut: 50, sirala: '-cikisTarihi', arama: 'İzmir', kurumsal: 'true' });

    const unsorted = listDefinition({ filtreler: {} });
    expect(apiParams(unsorted, parseQuery(unsorted, {}))).toEqual({
      sayfa: 1,
      boyut: 50,
    });
  });

  it('etkin filtre sayısı', () => {
    expect(activeFilterCount(KIRALAR, DEFAULT)).toBe(0);
    expect(
      activeFilterCount(KIRALAR, { ...DEFAULT, filtreler: { arama: 'a', kurumsal: false } }),
    ).toBe(2);
  });
});
