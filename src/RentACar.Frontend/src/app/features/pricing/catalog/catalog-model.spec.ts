import { describe, expect, it } from 'vitest';

import { channelDeleteVisible, exceedsImportLimit } from '../import/rate-import';
import { parseCodes } from '../quote/quote-calculator';
import { CATALOGS } from './catalog-configs';
import {
  type CatalogField,
  type CatalogRow,
  catalogListDefinition,
  emptyFormValue,
  formToBody,
  rowToForm,
} from './catalog-model';
import { formatWeekdays, parseWeekdays } from './weekday-picker';

const F = (
  name: string,
  kind: CatalogField['kind'],
  extra: Partial<CatalogField> = {},
): CatalogField => ({
  name,
  label: 'fiyatTarife.alan.kod',
  kind,
  ...extra,
});

/** Elle kurulmuş tarife satırı: 4 haneli eski fiyat, İstanbul gece yarısı geçerlilik, sürüm "s-1". */
const ROW: CatalogRow = {
  id: 'r1',
  kod: 'B-STD',
  gunlukUcret: 1250.1234,
  gecerliBas: '2026-09-30T21:00:00Z',
  minGun: 3,
  aktif: true,
  sifre: null,
  onaylayan: 'ayse',
  surum: 's-1',
};

const FIELDS: readonly CatalogField[] = [
  F('kod', 'text', { required: true }),
  F('gunlukUcret', 'money'),
  F('gecerliBas', 'date'),
  F('minGun', 'int'),
  F('aktif', 'bool'),
  F('sifre', 'password'),
  F('onaylayan', 'readonly'),
];

describe('tanım formu ↔ DTO', () => {
  it('kayıt → form: tutar 4 hane invariant metin, tarih İstanbul günü, şifre boş, salt okunur alan formda yok', () => {
    expect(rowToForm(FIELDS, ROW)).toEqual({
      kod: 'B-STD',
      gunlukUcret: '1250.1234',
      gecerliBas: '2026-10-01',
      minGun: 3,
      aktif: true,
      sifre: null,
    });
  });

  it('dokunmadan kaydet: tutar ve tarih AYNEN geri gider (kayma yok), surum eklenir, onaylayan gönderilmez', () => {
    const body = formToBody(FIELDS, rowToForm(FIELDS, ROW), ROW);
    expect(body).toEqual({
      kod: 'B-STD',
      gunlukUcret: '1250.1234',
      gecerliBas: '2026-09-30T21:00:00Z',
      minGun: 3,
      aktif: true,
      sifre: null,
      surum: 's-1',
    });
  });

  it('değişen gün İstanbul gece yarısı UTC anı olur; boş metin null; yeni kayıtta surum yok', () => {
    const body = formToBody(
      FIELDS,
      {
        kod: '  X  ',
        gunlukUcret: '',
        gecerliBas: '2026-11-15',
        minGun: null,
        aktif: false,
        sifre: '',
      },
      null,
    );
    expect(body).toEqual({
      kod: 'X',
      gunlukUcret: null,
      gecerliBas: '2026-11-14T21:00:00.000Z',
      minGun: null,
      aktif: false,
      sifre: null,
    });
  });

  it('yeni form varsayılanları: bool false, tanımlı varsayılan', () => {
    expect(
      emptyFormValue([F('aktif', 'bool', { defaultValue: true }), F('x', 'bool'), F('m', 'money')]),
    ).toEqual({
      aktif: true,
      x: false,
      m: null,
    });
  });
});

describe('tarife içe aktarım dosya sınırı (kabul testi: 6 MB → ham "Content Too Large")', () => {
  it('5 MB (5.242.880 bayt) dahil kabul, üstü yüklenmeden reddedilir', () => {
    expect(exceedsImportLimit({ size: 5_242_880 })).toBe(false);
    expect(exceedsImportLimit({ size: 5_242_881 })).toBe(true);
    expect(exceedsImportLimit({ size: 6 * 1024 * 1024 })).toBe(true);
  });
});

describe('KDV oranı girişi tutarlı (kabul testi): ekranda %, API sözleşmesi aynı', () => {
  const extra = CATALOGS['ek-hizmetler']!;
  const coverage = CATALOGS['sigorta-urunleri']!;
  const vat = (c: typeof extra) => c.fields.find((f) => f.name === 'kdvOrani')!;

  it('ek hizmet: API kesir (0,20) ↔ ekran yüzde (20); kayma yok', () => {
    const f = vat(extra);
    expect(f.label).toBe('fiyatTarife.alan.kdvYuzde');
    const row = { id: 'e1', kdvOrani: 0.2, surum: 's' } as CatalogRow;
    expect(rowToForm([f], row)).toEqual({ kdvOrani: 20 });
    expect(formToBody([f], { kdvOrani: 20 }, row)).toEqual({ kdvOrani: 0.2, surum: 's' });
    expect(formToBody([f], { kdvOrani: 8.25 }, null)).toEqual({ kdvOrani: 0.0825 });
    expect(formToBody([f], { kdvOrani: 18 }, null)).toEqual({ kdvOrani: 0.18 });
    expect(rowToForm([f], { id: 'e2', kdvOrani: 0.1 } as CatalogRow)).toEqual({ kdvOrani: 10 });
    expect(formToBody([f], { kdvOrani: null }, null)).toEqual({ kdvOrani: null });
    // Yeni kayıt varsayılanı da yüzde: %20.
    expect(emptyFormValue([f])).toEqual({ kdvOrani: 20 });
  });

  it('sigorta ürünü zaten yüzde (0–100): dönüşüm yok, etiket aynı', () => {
    const f = vat(coverage);
    expect(f.label).toBe('fiyatTarife.alan.kdvYuzde');
    expect(rowToForm([f], { id: 's1', kdvOrani: 20 } as CatalogRow)).toEqual({ kdvOrani: 20 });
    expect(formToBody([f], { kdvOrani: 20 }, null)).toEqual({ kdvOrani: 20 });
  });
});

describe('liste tanımı', () => {
  it('sıralanabilir sütunlar sunucu Sort haritasıyla aynı (tarife matrisi); q + ekran süzgeçleri', () => {
    const def = catalogListDefinition(CATALOGS['tarife-matris']!);
    expect(def.siralanabilir).toEqual([
      'kod',
      'ad',
      'kanal',
      'aracGrupKod',
      'gun1',
      'onayDurumu',
      'aktif',
    ]);
    expect(Object.keys(def.filtreler)).toEqual(['q']);
    const rules = catalogListDefinition(CATALOGS['kira-kurallari']!);
    expect(rules.siralanabilir).toEqual(['kod', 'ad', 'kanal', 'gecerlilikBas', 'kampanyaDurum']);
    expect(Object.keys(rules.filtreler)).toEqual([
      'q',
      'durum',
      'tarihTipi',
      'kampanyaMi',
      'kanal',
      'gecerliBas',
      'gecerliBit',
    ]);
  });

  it('sekiz tanım ekranı tanımlı; kira kuralları arama ucundan listelenir', () => {
    expect(Object.keys(CATALOGS).sort()).toEqual([
      'broker-yasaklari',
      'ek-hizmetler',
      'kira-kurallari',
      'servis-tanimlari',
      'sigorta-urunleri',
      'tarife-gruplari',
      'tarife-matris',
      'tarifeler',
    ]);
    expect(CATALOGS['kira-kurallari']!.listPath).toBe('/api/ui/v1/kira-kurallari/ara');
  });
});

describe('küçük saf kurallar', () => {
  it('hafta günleri: "1,3,0" ↔ küme; boş = kısıt yok; sıralı yazılır', () => {
    expect([...parseWeekdays('1, 3,0,9,x')]).toEqual([1, 3, 0]);
    expect(formatWeekdays(new Set([6, 0, 2]))).toBe('0,2,6');
    expect(formatWeekdays(new Set())).toBeNull();
  });

  it('sigorta kodları: kırpılır, boşlar ve tekrarlar düşer', () => {
    expect(parseCodes(' SCDW, IMM ,,SCDW ')).toEqual(['SCDW', 'IMM']);
    expect(parseCodes(null)).toEqual([]);
  });

  it('kanal toplu silme yalnız "kanal seçili, başka daraltma yok" (ya da onay=Bekliyor) hâlinde görünür', () => {
    expect(channelDeleteVisible({ kanal: 'WEB' })).toBe(true);
    expect(channelDeleteVisible({ kanal: 'WEB', durum: 'Bekliyor' })).toBe(true);
    expect(channelDeleteVisible({ kanal: 'WEB', durum: 'Onayli' })).toBe(false);
    expect(channelDeleteVisible({ kanal: 'WEB', sube: 'Merkez' })).toBe(false);
    expect(channelDeleteVisible({})).toBe(false);
  });
});
