import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';
import { ApiHatasi } from '../api/api-hatasi';
import {
  SERVER_ERROR,
  getFieldErrors,
  clearServerErrors,
  applyServerErrors,
} from './sunucu-hatalari';

function setupForm() {
  return new FormGroup({
    plaka: new FormControl<string | null>('34 ABC 123', Validators.required),
    islemAnahtari: new FormControl<string | null>('x'),
    adres: new FormGroup({ il: new FormControl<string | null>('İstanbul') }),
    kalemler: new FormArray([new FormGroup({ tutar: new FormControl<string | null>('10.00') })]),
  });
}

describe('sunucuHatalariniUygula', () => {
  it('alan adı büyük/küçük harf duyarsız (Türkçe I dahil), yol ve dizi; DEĞERLER KORUNUR', () => {
    const form = setupForm();
    const once = form.getRawValue();
    const unmatched = applyServerErrors(form, {
      Plaka: ['Bu plaka zaten kayıtlı.'],
      IslemAnahtari: ['Anahtar biçimsiz.'],
      'Adres.Il': ['İl zorunlu.'],
      'Kalemler[0].Tutar': ['Tutar sıfır olamaz.'],
      'Idempotency-Key': ['Başlık geçersiz.'],
    });
    expect(form.controls.plaka.errors).toEqual({ [SERVER_ERROR]: ['Bu plaka zaten kayıtlı.'] });
    expect(form.controls.islemAnahtari.hasError(SERVER_ERROR)).toBe(true);
    expect(form.controls.adres.controls.il.hasError(SERVER_ERROR)).toBe(true);
    expect(form.controls.kalemler.at(0).controls.tutar.hasError(SERVER_ERROR)).toBe(true);
    expect(form.controls.plaka.touched).toBe(true);
    expect(unmatched).toEqual(['Başlık geçersiz.']);
    expect(form.getRawValue()).toEqual(once);
  });

  it('eşleme tablosu önceliklidir', () => {
    const form = setupForm();
    applyServerErrors(form, { CariId: ['Cari yok.'] }, { CariId: 'plaka' });
    expect(form.controls.plaka.getError(SERVER_ERROR)).toEqual(['Cari yok.']);
  });

  it('kullanıcı alanı değiştirince sunucu hatası kalkar; temizle() değiştirmeden kaldırır', () => {
    const form = setupForm();
    applyServerErrors(form, { plaka: ['x'], 'adres.il': ['y'] });
    form.controls.plaka.setValue('06 XYZ 1');
    expect(form.controls.plaka.errors).toBeNull();
    expect(form.invalid).toBe(true);
    clearServerErrors(form);
    expect(form.valid).toBe(true);
  });

  it('istemci hatasıyla birlikte tutulur', () => {
    const form = setupForm();
    form.controls.plaka.setValue(null);
    applyServerErrors(form, { plaka: ['sunucu'] });
    expect(form.controls.plaka.hasError('required')).toBe(true);
    expect(form.controls.plaka.hasError(SERVER_ERROR)).toBe(true);
  });
});

describe('alanHatalariniAl', () => {
  it('ApiHatasi.alanlar; alansız hata → undefined', () => {
    const fields = { Plaka: ['x'] };
    expect(
      getFieldErrors(new ApiHatasi({ status: 400, kod: 'dogrulama', detay: 'd', alanlar: fields })),
    ).toEqual(fields);
    expect(getFieldErrors(new Error('ağ'))).toBeUndefined();
  });
});
