import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';

import { REPORTS } from './report-catalog';
import { OPS_OR_VIEW, VIEW_REPORTS, columnsFor, defineView, type RowOf } from './report-model';
import { reportListDefinition } from './report-query';
import { REPORT_ROUTE_TABLE } from './reports.routes';

/**
 * Yapılandırma doğrulaması. Beklenen değerler ELLE (bağımsız oracle): sunucu `ReportApi` uç listesi ve
 * `SiralamaHaritasi` beyaz listeleri C# kaynağından kopyalandı — katalog koddan türetilmez. Sütun alanının satır
 * şemasında olması DERLEMEDE kilitli (`@ts-expect-error` satırları aşağıda).
 */

/** `ReportApi.*.cs` `MapGet` uçları (F10.1, #287) → bu PR'daki rapor ekranları. */
const ENDPOINTS: Readonly<Record<string, readonly string[]>> = {
  'gelir-gider': ['gelir-gider'],
  'kasa-banka': ['kasa-banka'],
  'finans-analiz': ['finans-analiz'],
  'virman-gecmisi': ['virman-gecmisi'],
  'kdv-listesi': ['kdv-listesi', 'kdv-listesi/genis'],
  'cari-bakiye': ['cari-bakiye', 'cari-bakiye/yaslandirma'],
  'extre-ozeti': ['extre-ozeti'],
  'tahsilat-fatura': ['tahsilat-fatura', 'tahsilat-fatura/mutabakat'],
  'fatura-donem': ['fatura-donem', 'fatura-donem/kira-durum'],
  karlilik: ['karlilik', 'karlilik/ozet'],
  'ek-hizmet': ['ek-hizmet', 'ek-hizmet/arac-pivot', 'ek-hizmet/detay'],
  gunluk: ['gunluk'],
};

/** C# `SiralamaHaritasi` alanları (uç → beyaz liste); sıralanabilir sütun yalnız bunlardan. */
const SORT_WHITELIST: Readonly<Record<string, readonly string[]>> = {
  'kasa-banka': [],
  'virman-gecmisi': ['tarih', 'tutar', 'tutarTl', 'doviz', 'sube'],
  'kdv-listesi/genis': ['tarih', 'no', 'tur', 'cari', 'toplamNet', 'toplamKdv'],
  'cari-bakiye': ['ad', 'bakiye', 'toplamBorc', 'toplamAlacak', 'doviz', 'sinif'],
  'cari-bakiye/yaslandirma': ['ad', 'toplam', 'b0_30', 'b31_60', 'b61_90', 'b90Plus'],
  'extre-ozeti': ['tarih', 'vadeTarihi', 'faturaNo', 'cariAd', 'tutar'],
  'tahsilat-fatura/mutabakat': [
    'basTar',
    'sozlesmeNo',
    'musteriAd',
    'genelToplam',
    'bakiye',
    'faturaFarki',
  ],
  'fatura-donem': ['tarih', 'vadeTarihi', 'no', 'cari', 'genelToplam'],
  'fatura-donem/kira-durum': ['basTar', 'sozlesmeNo', 'cari', 'plaka', 'faturalananTutar'],
  karlilik: ['plaka', 'gelir', 'gider', 'netKar', 'sube', 'grup', 'dolulukYuzde'],
  'ek-hizmet/detay': ['eklenmeTarihi', 'sozlesmeNo', 'ad', 'plaka', 'brut'],
};

/** Uç izin grubu (`vr` = ViewReports, `ops` = OperationsWrite ∨ ViewReports). */
const ACCESS: Readonly<Record<string, 'vr' | 'ops'>> = Object.fromEntries(
  Object.keys(ENDPOINTS).map((k) => [k, 'vr']),
);

const endpointOf = (uc: string) => uc.replace('/api/ui/v1/raporlar/', '');

describe('rapor kataloğu', () => {
  it('her rapor uçlarıyla birebir; her uç tam bir görünümde', () => {
    const actual = Object.fromEntries(
      REPORTS.map((r) => [r.kod, r.gorunumler.map((v) => endpointOf(v.uc))]),
    );
    expect(actual).toEqual(ENDPOINTS);
  });

  it('izinler ve rota tablosu uçlarla birebir', () => {
    for (const r of REPORTS) {
      const expected = ACCESS[r.kod] === 'ops' ? OPS_OR_VIEW : VIEW_REPORTS;
      expect(r.izinler, r.kod).toEqual(expected);
    }
    expect(
      Object.fromEntries(REPORT_ROUTE_TABLE.map(([code, , access]) => [code, access])),
    ).toEqual(ACCESS);
  });

  it('sıralanabilir sütunlar sunucu beyaz listesinde; tanımın listesi sunucununkiyle aynı', () => {
    for (const r of REPORTS) {
      for (const v of r.gorunumler) {
        if (!v.satirlar) continue;
        const key = endpointOf(v.uc);
        const server = SORT_WHITELIST[key];
        expect(server, `${key} oracle listesi`).toBeDefined();
        expect([...v.satirlar.siralanabilir].sort(), key).toEqual([...(server ?? [])].sort());
        for (const c of v.satirlar.sutunlar.filter((x) => x.sirala)) {
          expect(server, `${key}.${c.kod}`).toContain(c.kod);
        }
      }
    }
  });

  it('görünüm kodları ve sütun kodları rapor içinde benzersiz; URL kataloğu kurulur', () => {
    for (const r of REPORTS) {
      const views = r.gorunumler.map((v) => v.kod);
      expect(new Set(views).size, r.kod).toBe(views.length);
      for (const v of r.gorunumler) {
        const cols = v.satirlar?.sutunlar.map((c) => c.kod) ?? [];
        expect(new Set(cols).size, `${r.kod}/${v.kod}`).toBe(cols.length);
      }
      expect(() => reportListDefinition(r)).not.toThrow();
    }
  });

  it('kullanılan her çeviri anahtarı rapor bloğunda var', async () => {
    TestBed.configureTestingModule({ providers: [...provideCeviri()] });
    const transloco = TestBed.inject(TranslocoService);
    await firstValueFrom(transloco.load('tr'));
    const keys = new Set<string>();
    for (const r of REPORTS) {
      keys.add(r.baslik);
      if (r.aciklama) keys.add(r.aciklama);
      for (const v of r.gorunumler) {
        if (v.baslik) keys.add(v.baslik);
        for (const f of v.filtreler) {
          if (f.tur !== 'donem') keys.add(f.baslik);
          if (f.ipucu) keys.add(f.ipucu);
          if (f.tur === 'secim') f.secenekler.forEach((s) => keys.add(s.etiket));
        }
        v.kartlar.forEach((k) => keys.add(k.baslik));
        v.uyarilar.forEach((u) => keys.add(u.metin));
        v.bolumler.forEach((b) => keys.add(b.baslik));
        v.satirlar?.sutunlar.forEach((c) => keys.add(c.baslik));
      }
    }
    const missing = [...keys].filter((k) => !transloco.getTranslation('tr')[k]);
    expect(missing).toEqual([]);
  });
});

describe('tanımlar üretilen tiplere bağlı (derleme kilidi)', () => {
  it('satır şemasında olmayan alan ve uçta olmayan süzgeç derlenmez', () => {
    const cols = columnsFor<RowOf<'/api/ui/v1/raporlar/km-detay'>>();
    // @ts-expect-error — KmDetayRow'da `tutar` yok.
    cols.field('tutar', 'para');
    defineView('/api/ui/v1/raporlar/gelir-gider', {
      // @ts-expect-error — gelir-gider ucunun `plaka` parametresi yok.
      filtreler: [{ tur: 'metin', ad: 'plaka', baslik: 'rapor.alan.plaka' }],
    });
    expect(cols.field('plaka', 'metin').kod).toBe('plaka');
  });
});
