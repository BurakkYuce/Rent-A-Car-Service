import { type MeResponse, izinVar, branchLabel } from '@core/api/ui-tipleri';

/** Elle kurulmuş örnek yanıt: alan adı/tipi API'de değişirse bu dosya da derlenmez. */
function sampleMe(branchScope: MeResponse['subeKapsami']): MeResponse {
  return {
    kullanici: { id: '00000000-0000-0000-0000-000000000001', kullaniciAdi: 'umit', adSoyad: null },
    kiraci: { id: '00000000-0000-0000-0000-000000000002', kod: 'demo', ad: 'Demo' },
    rol: 'Operator',
    izinler: ['OperationsWrite'],
    subeKapsami: branchScope,
    moduller: { webSitesi: false },
    renkler: {},
    pilot: false,
  };
}

describe('ui-tipleri', () => {
  const all: MeResponse['subeKapsami'] = { tumSubeler: true, subeId: null, subeAd: null };

  it('izin listesinde birebir eşleşme arar', () => {
    const ben = sampleMe(all);
    expect(izinVar(ben, 'OperationsWrite')).toBe(true);
    expect(izinVar(ben, 'FinanceWrite')).toBe(false);
  });

  it('şube etiketi kapsamdan türetilir', () => {
    expect(branchLabel(sampleMe(all))).toBe('Tüm şubeler');
    expect(
      branchLabel(
        sampleMe({
          tumSubeler: false,
          subeId: '00000000-0000-0000-0000-000000000003',
          subeAd: 'Merkez',
        }),
      ),
    ).toBe('Merkez');
    expect(branchLabel(sampleMe({ tumSubeler: false, subeId: null, subeAd: null }))).toBe(
      'Şube atanmamış',
    );
  });
});
