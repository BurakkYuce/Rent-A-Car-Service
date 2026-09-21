import { type BenYaniti, izinVar, subeEtiketi } from '@core/api/ui-tipleri';

/** Elle kurulmuş örnek yanıt: alan adı/tipi API'de değişirse bu dosya da derlenmez. */
function ornekBen(subeKapsami: BenYaniti['subeKapsami']): BenYaniti {
  return {
    kullanici: { id: '00000000-0000-0000-0000-000000000001', kullaniciAdi: 'umit', adSoyad: null },
    kiraci: { id: '00000000-0000-0000-0000-000000000002', kod: 'demo', ad: 'Demo' },
    rol: 'Operator',
    izinler: ['OperationsWrite'],
    subeKapsami,
    moduller: { webSitesi: false },
    renkler: {},
    pilot: false,
  };
}

describe('ui-tipleri', () => {
  const tumu: BenYaniti['subeKapsami'] = { tumSubeler: true, subeId: null, subeAd: null };

  it('izin listesinde birebir eşleşme arar', () => {
    const ben = ornekBen(tumu);
    expect(izinVar(ben, 'OperationsWrite')).toBe(true);
    expect(izinVar(ben, 'FinanceWrite')).toBe(false);
  });

  it('şube etiketi kapsamdan türetilir', () => {
    expect(subeEtiketi(ornekBen(tumu))).toBe('Tüm şubeler');
    expect(
      subeEtiketi(
        ornekBen({
          tumSubeler: false,
          subeId: '00000000-0000-0000-0000-000000000003',
          subeAd: 'Merkez',
        }),
      ),
    ).toBe('Merkez');
    expect(subeEtiketi(ornekBen({ tumSubeler: false, subeId: null, subeAd: null }))).toBe(
      'Şube atanmamış',
    );
  });
});
