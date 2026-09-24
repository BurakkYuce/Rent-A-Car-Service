import { of } from 'rxjs';

import { DEFINITION_PATHS, type DefinitionKind } from './definition-paths';
import { branchFields, definitionConfig, occupancyFields } from './definition-catalog';
import { ladderSteps } from './occupancy/occupancy-page';

/** Etiket çevirisi testte anahtarın kendisi (yapılandırma çeviriden bağımsız doğrulanır). */
const t = (k: string) => k;
const none = () => of([] as readonly string[]);
const suggest = { branch: none, location: none };
const names = (kind: DefinitionKind) => definitionConfig(kind, t, suggest).fields.map((f) => f.ad);

describe('tanım yapılandırması (Blazor form alanları + uç gövdesi)', () => {
  it('kök yol rota yoluyla aynı; Kod+Ad+Aktif tanımları satır yerleşiminde', () => {
    expect(definitionConfig('brand', t, suggest).root).toBe('/api/ui/v1/markalar');
    expect(definitionConfig('drop', t, suggest).root).toBe('/api/ui/v1/drop-tanimlari');
    for (const k of [
      'brand',
      'cancelReason',
      'country',
      'customerGroup',
      'department',
      'bank',
    ] as const) {
      expect(names(k)).toEqual(['kod', 'ad', 'aktif']);
      expect(definitionConfig(k, t, suggest).layout).toBe('row');
    }
    expect(Object.keys(DEFINITION_PATHS)).toHaveLength(12);
  });

  it('türe özgü alanlar (API istek adlarıyla birebir)', () => {
    expect(names('accessory')).toEqual(['kod', 'ad', 'aciklama', 'aktif']);
    expect(names('currency')).toEqual(['kod', 'ad', 'sembol', 'ulke', 'aktif']);
    expect(names('customCode')).toEqual(['kod', 'ad', 'aciklama', 'turu', 'aktif']);
    expect(names('expenseCategory')).toEqual(['kod', 'ad', 'tur', 'aktif']);
    expect(names('account')).toEqual([
      'kod',
      'ad',
      'tur',
      'doviz',
      'iban',
      'hesapNo',
      'banka',
      'sube',
      'ozelKod',
      'hediyeCek',
      'uyariMailListesi',
      'aktif',
    ]);
    expect(names('drop')).toEqual([
      'lokasyon',
      'sube',
      'cikisLokasyon',
      'minGun',
      'karsilamaSekli',
      'calismaSekli',
      'ozelIletisim',
      'ucret',
      'drop2',
      'manSuresi',
      'aktif',
    ]);
  });

  it('sınırlar uç sınırlarıyla aynı; aktif zorunlu ve yeni kayıtta true', () => {
    const currency = definitionConfig('currency', t, suggest).fields;
    expect(currency.find((f) => f.ad === 'kod')?.azamiUzunluk).toBe(3);
    expect(currency.find((f) => f.ad === 'sembol')?.azamiUzunluk).toBe(8);
    const active = currency.find((f) => f.ad === 'aktif');
    expect(active?.zorunlu).toBe(true);
    expect(active?.defaultValue).toBe(true);
    const drop = definitionConfig('drop', t, suggest).fields;
    expect(drop.find((f) => f.ad === 'lokasyon')?.tur).toBe('datalist');
    expect(drop.find((f) => f.ad === 'ucret')?.tur).toBe('para');
  });

  it("şube: Blazor formunun 29 alanı + Durum görünür, bağlı hesaplar gizli (tam PUT'ta silinmesin)", () => {
    const fields = branchFields(t, { il: none, ilce: none });
    expect(fields.filter((f) => !f.hidden)).toHaveLength(30);
    expect(fields.filter((f) => f.hidden).map((f) => f.ad)).toEqual([
      'nakitHesapId',
      'bankaHesapId',
    ]);
    expect(fields.find((f) => f.ad === 'komisyonOran')?.fraction).toBe(4);
  });

  it('doluluk: geçerlilik takvim günü, çarpan 2 hane', () => {
    const fields = occupancyFields(t, { group: none, branch: none });
    expect(fields.find((f) => f.ad === 'gecerlilikBas')?.tur).toBe('date');
    expect(fields.find((f) => f.ad === 'carpanYuzde')?.fraction).toBe(2);
  });
});

describe('toplu kademe', () => {
  it('boş satır atlanır, yarım satır hatalı sırayı döner', () => {
    const empty = { esik: null, carpan: null };
    expect(ladderSteps([{ esik: 70, carpan: 10 }, empty, { esik: 90, carpan: 25.5 }])).toEqual({
      steps: [
        { esikYuzde: 70, carpanYuzde: 10 },
        { esikYuzde: 90, carpanYuzde: 25.5 },
      ],
    });
    expect(ladderSteps([empty, { esik: 80, carpan: null }])).toEqual({ half: 2 });
    expect(ladderSteps([empty])).toEqual({ steps: [] });
  });
});
