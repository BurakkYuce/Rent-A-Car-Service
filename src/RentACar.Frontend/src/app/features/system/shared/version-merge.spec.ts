import { FormControl, FormGroup } from '@angular/forms';

import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';

import { mergeServerValues, pickValues } from './version-merge';

/** Bağımsız oracle: senaryolar elle kuruldu (taban, kullanıcı düzenlemesi, başka oturumun değişikliği). */
function form() {
  return new FormGroup({
    ad: new FormControl<unknown>('Merkez'),
    telefon: new FormControl<unknown>('111'),
    adres: new FormControl<unknown>('Eski adres'),
    sifre: new FormControl<unknown>(null),
  });
}

describe('mergeServerValues (409 cakisma birleştirmesi)', () => {
  it('dokunulmayan alan sunucuya çekilir, dokunulan korunur, iki taraf değiştiyse işaretlenir', () => {
    const f = form();
    const base = { ad: 'Merkez', telefon: '111', adres: 'Eski adres' };
    f.controls.telefon.setValue('222');
    f.controls.telefon.markAsDirty();
    f.controls.adres.setValue('Benim adresim');
    f.controls.adres.markAsDirty();
    const fresh = { ad: 'Merkez Ofis', telefon: '111', adres: 'Başka oturum adresi' };

    const flagged = mergeServerValues(f, base, fresh, 'değişti');

    expect(f.controls.ad.value).toBe('Merkez Ofis');
    expect(f.controls.telefon.value).toBe('222');
    expect(f.controls.telefon.errors).toBeNull();
    expect(f.controls.adres.value).toBe('Benim adresim');
    expect(f.controls.adres.errors?.[SUNUCU_HATASI]).toEqual(['değişti']);
    expect(flagged).toEqual(['adres']);
  });

  it('yanıtta olmayan alana (yazılabilir sır) dokunmaz', () => {
    const f = form();
    f.controls.sifre.setValue('yeni-parola');
    f.controls.sifre.markAsDirty();
    mergeServerValues(f, { ad: 'Merkez' }, { ad: 'Merkez' }, 'x');
    expect(f.controls.sifre.value).toBe('yeni-parola');
  });

  it('pickValues formun her alanını doldurur, kayıtta olmayan null', () => {
    expect(pickValues(form(), { ad: 'A', fazla: 1 })).toEqual({
      ad: 'A',
      telefon: null,
      adres: null,
      sifre: null,
    });
  });
});
