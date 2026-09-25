import { countParameters } from './service-insurance.store';

describe('servis sayaç parametreleri', () => {
  it('durum ve sayfalama düşer, diğer süzgeçler aynen gider', () => {
    expect(
      countParameters({
        durum: 'Acik',
        sayfa: 2,
        boyut: 50,
        sirala: '-girisTarihi',
        tip: 'Hasar',
        plaka: '34',
        bas: '2026-09-01',
      }),
    ).toEqual({ tip: 'Hasar', plaka: '34', bas: '2026-09-01' });
  });
});
