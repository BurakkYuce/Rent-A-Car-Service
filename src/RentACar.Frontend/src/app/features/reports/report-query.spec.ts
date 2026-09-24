import { sorguyuCoz } from '@core/veri/liste-sorgusu';

import { findReport } from './report-catalog';
import { defineReport, defineView } from './report-model';
import {
  activeView,
  exportFromLinks,
  formatValue,
  reportListDefinition,
  viewParams,
  viewUrl,
} from './report-query';

const LABELS = { evet: 'Evet', hayir: 'Hayır' };

function queryFor(code: string, url: Record<string, string>) {
  const def = findReport(code);
  const tanim = reportListDefinition(def);
  const query = sorguyuCoz(tanim, url);
  return { def, query, view: activeView(def, query.filtreler as Record<string, unknown>) };
}

describe('rapor sorgusu (URL → API)', () => {
  it('dönem bas/bit olduğu gibi gider; bayrak yalnız true iken; sayfalı görünüm sayfa/boyut taşır', () => {
    const { view, query } = queryFor('kasa-banka', {
      bas: '2026-09-01',
      bit: '2026-09-30',
      devir: 'true',
      doviz: 'USD',
      sayfa: '2',
    });
    expect(viewParams(view, query, false)).toEqual({
      bas: '2026-09-01',
      bit: '2026-09-30',
      devir: true,
      doviz: 'USD',
      sayfa: 2,
      boyut: 50,
    });
  });

  it('satırsız görünüm sayfalama parametresi göndermez', () => {
    const { view, query } = queryFor('gelir-gider', { sayfa: '3' });
    expect(viewParams(view, query, false)).toEqual({});
  });

  it('URL adı ≠ API adı: kârlılık kırılımı URL `kirilim`, API `boyut` (sayfa boyutuyla çakışmaz)', () => {
    const { view, query } = queryFor('karlilik', { gorunum: 'boyut', kirilim: 'sube' });
    expect(view.uc).toBe('/api/ui/v1/raporlar/karlilik/ozet');
    expect(viewParams(view, query, false)).toEqual({ boyut: 'sube' });
  });

  it('görünüm değişince önceki görünümün sıralaması (beyaz liste dışı) gönderilmez', () => {
    const { view, query } = queryFor('cari-bakiye', { gorunum: 'yaslandirma', sirala: '-sinif' });
    expect(view.kod).toBe('yaslandirma');
    expect(viewParams(view, query, false)['sirala']).toBeUndefined();
    const ok = queryFor('cari-bakiye', { sirala: '-sinif' });
    expect(viewParams(ok.view, ok.query, false)['sirala']).toBe('-sinif');
  });

  it('şube kapsamlı kullanıcının şube süzgeci gönderilmez (sunucu kendi şubesine zorlar)', () => {
    const { view, query } = queryFor('karlilik', { sube: 'Havalimanı' });
    expect(viewParams(view, query, false)['sube']).toBe('Havalimanı');
    expect(viewParams(view, query, true)['sube']).toBeUndefined();
  });

  it('bozuk URL değeri varsayılana düşer (400 üretmez)', () => {
    const { view, query } = queryFor('cari-bakiye', { tip: 'hepsi', min: 'abc', gorunum: 'yok' });
    expect(view.kod).toBe('bakiye');
    expect(viewParams(view, query, false)).toEqual({ sayfa: 1, boyut: 50 });
  });

  it('kimlikli uç: {id} kaçışlanarak dolar', () => {
    const view = defineView('/api/ui/v1/raporlar/arac-karne/{id}', {})('x');
    expect(viewUrl(view, 'a/b')).toBe('/api/ui/v1/raporlar/arac-karne/a%2Fb');
  });

  it('aynı URL anahtarı iki farklı türle tanımlanamaz (tanım anında hata)', () => {
    const bad = defineReport({
      kod: 'x',
      baslik: 'rapor.toplam',
      grup: 'finans',
      izinler: ['ViewReports'],
      firmaGeneli: true,
      gorunumler: [
        defineView('/api/ui/v1/raporlar/cari-bakiye', {
          kod: 'a',
          filtreler: [{ tur: 'metin', ad: 'ara', baslik: 'rapor.alan.ara' }],
        }),
        defineView('/api/ui/v1/raporlar/extre-ozeti', {
          kod: 'b',
          filtreler: [
            { tur: 'bayrak', ad: 'gecikmis', param: 'gecikmis', baslik: 'rapor.alan.ara' },
          ],
        }),
        defineView('/api/ui/v1/raporlar/extre-ozeti', {
          kod: 'c',
          filtreler: [{ tur: 'bayrak', ad: 'ara', param: 'gecikmis', baslik: 'rapor.alan.ara' }],
        }),
      ],
    });
    expect(() => reportListDefinition(bad)).toThrow(/"ara" süzgeci iki farklı türle/);
  });
});

describe('export bağlantısı', () => {
  it('bağlantı yoksa düğme yok', () => {
    expect(exportFromLinks(null)).toBeNull();
    expect(exportFromLinks(undefined)).toBeNull();
  });

  it('sunucu bağlantısı yol + parametrelere ayrılır; format düşer; PDF yalnız verildiyse', () => {
    expect(
      exportFromLinks({
        excel: '/raporlar/export/kasa-banka?format=excel&hesap=Kasa&from=2026-09-01',
        csv: '/raporlar/export/kasa-banka?format=csv&hesap=Kasa&from=2026-09-01',
        pdf: null,
      }),
    ).toEqual({
      yol: '/raporlar/export/kasa-banka',
      parametreler: { hesap: 'Kasa', from: '2026-09-01' },
      bicimler: ['excel', 'csv'],
    });
    expect(
      exportFromLinks({ excel: '/raporlar/export/x?format=excel', csv: '', pdf: '/p' })?.bicimler,
    ).toEqual(['excel', 'csv', 'pdf']);
  });

  it('beklenmeyen yol (dış adres) kabul edilmez', () => {
    expect(exportFromLinks({ excel: '//kotu.example/raporlar/export/x', csv: '' })).toBeNull();
  });
});

describe('değer biçimi', () => {
  it('para/sayı/yüzde/tarih/bayrak; decimal JSON metni de sayıdır', () => {
    expect(formatValue('para', '1234.5', LABELS)).toBe('1.234,50 ₺');
    expect(formatValue('para', 10, LABELS, 'EUR')).toBe('10,00 €');
    expect(formatValue('tamsayi', 1200, LABELS)).toBe('1.200');
    expect(formatValue('yuzde', 62.54, LABELS)).toBe('%62,5');
    expect(formatValue('tarih', '2026-09-01', LABELS)).toBe('01.09.2026');
    expect(formatValue('bayrak', true, LABELS)).toBe('Evet');
    expect(formatValue('bayrak', false, LABELS)).toBe('Hayır');
    expect(formatValue('para', null, LABELS)).toBe('—');
    expect(formatValue('metin', '', LABELS)).toBe('—');
  });
});
