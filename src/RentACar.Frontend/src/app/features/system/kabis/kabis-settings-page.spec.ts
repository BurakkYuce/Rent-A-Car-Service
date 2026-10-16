import { kabisSettingsValues } from './kabis-settings-page';

describe('KABİS ayar formu değerleri', () => {
  it('şifre form değerine hiç girmez; bilinmeyen kurum Emniyet olur', () => {
    const values = kabisSettingsValues({
      etkin: true,
      kurum: 'Bilinmiyor',
      kullanici: 'firma01',
      sifreTanimli: true,
      teslimiEngelle: false,
      surum: '42',
    });
    expect(values).toEqual({
      etkin: true,
      kurum: 'Emniyet',
      kullanici: 'firma01',
      teslimiEngelle: false,
    });
    expect('sifre' in values).toBe(false);
  });

  it('Jandarma korunur', () => {
    expect(
      kabisSettingsValues({
        etkin: false,
        kurum: 'Jandarma',
        kullanici: null,
        sifreTanimli: false,
        teslimiEngelle: false,
        surum: null,
      })['kurum'],
    ).toBe('Jandarma');
  });
});
