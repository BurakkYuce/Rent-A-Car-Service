import { exportUrl } from './disa-aktarma';

describe('Dışa aktarma (sunucu ucu adresi)', () => {
  it('biçim + ekrandaki filtre ve sıralama taşınır; sayfa/boyut ve boş değerler taşınmaz', () => {
    const address = exportUrl(
      {
        yol: '/listeler/export/araclar',
        parametreler: {
          sayfa: 3,
          boyut: 100,
          sirala: '-gunlukFiyat',
          ara: 'İstanbul & Şişli',
          sube: null,
          plaka: '',
          durum: ['Müsait', 'Kirada'],
          aktif: true,
        },
      },
      'csv',
    );
    expect(address).toBe(
      '/listeler/export/araclar?format=csv&sirala=-gunlukFiyat&ara=%C4%B0stanbul+%26+%C5%9Ei%C5%9Fli' +
        '&durum=M%C3%BCsait&durum=Kirada&aktif=true',
    );
  });

  it('parametresiz excel; format parametresi çağırandan ezilemez', () => {
    expect(exportUrl({ yol: '/raporlar/export/gelir-gider' }, 'excel')).toBe(
      '/raporlar/export/gelir-gider?format=excel',
    );
    expect(
      exportUrl({ yol: '/listeler/export/cariler', parametreler: { format: 'pdf' } }, 'csv'),
    ).toBe('/listeler/export/cariler?format=csv');
  });

  it('yalnız bilinen export kökleri (mutlak/başka yol programlama hatası)', () => {
    const bad = { yol: '/listeler/export/../api' } as unknown as Parameters<typeof exportUrl>[0];
    expect(() => exportUrl(bad, 'excel')).toThrow(TypeError);
  });
});
