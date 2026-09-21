import { FormArray, FormControl, FormGroup, Validators } from '@angular/forms';
import { ApiHatasi } from '../api/api-hatasi';
import {
  SUNUCU_HATASI,
  alanHatalariniAl,
  sunucuHatalariniTemizle,
  sunucuHatalariniUygula,
} from './sunucu-hatalari';

function formKur() {
  return new FormGroup({
    plaka: new FormControl<string | null>('34 ABC 123', Validators.required),
    islemAnahtari: new FormControl<string | null>('x'),
    adres: new FormGroup({ il: new FormControl<string | null>('İstanbul') }),
    kalemler: new FormArray([new FormGroup({ tutar: new FormControl<string | null>('10.00') })]),
  });
}

describe('sunucuHatalariniUygula', () => {
  it('alan adı büyük/küçük harf duyarsız (Türkçe I dahil), yol ve dizi; DEĞERLER KORUNUR', () => {
    const form = formKur();
    const once = form.getRawValue();
    const eslesmeyen = sunucuHatalariniUygula(form, {
      Plaka: ['Bu plaka zaten kayıtlı.'],
      IslemAnahtari: ['Anahtar biçimsiz.'],
      'Adres.Il': ['İl zorunlu.'],
      'Kalemler[0].Tutar': ['Tutar sıfır olamaz.'],
      'Idempotency-Key': ['Başlık geçersiz.'],
    });
    expect(form.controls.plaka.errors).toEqual({ [SUNUCU_HATASI]: ['Bu plaka zaten kayıtlı.'] });
    expect(form.controls.islemAnahtari.hasError(SUNUCU_HATASI)).toBe(true);
    expect(form.controls.adres.controls.il.hasError(SUNUCU_HATASI)).toBe(true);
    expect(form.controls.kalemler.at(0).controls.tutar.hasError(SUNUCU_HATASI)).toBe(true);
    expect(form.controls.plaka.touched).toBe(true);
    expect(eslesmeyen).toEqual(['Başlık geçersiz.']);
    expect(form.getRawValue()).toEqual(once);
  });

  it('eşleme tablosu önceliklidir', () => {
    const form = formKur();
    sunucuHatalariniUygula(form, { CariId: ['Cari yok.'] }, { CariId: 'plaka' });
    expect(form.controls.plaka.getError(SUNUCU_HATASI)).toEqual(['Cari yok.']);
  });

  it('kullanıcı alanı değiştirince sunucu hatası kalkar; temizle() değiştirmeden kaldırır', () => {
    const form = formKur();
    sunucuHatalariniUygula(form, { plaka: ['x'], 'adres.il': ['y'] });
    form.controls.plaka.setValue('06 XYZ 1');
    expect(form.controls.plaka.errors).toBeNull();
    expect(form.invalid).toBe(true);
    sunucuHatalariniTemizle(form);
    expect(form.valid).toBe(true);
  });

  it('istemci hatasıyla birlikte tutulur', () => {
    const form = formKur();
    form.controls.plaka.setValue(null);
    sunucuHatalariniUygula(form, { plaka: ['sunucu'] });
    expect(form.controls.plaka.hasError('required')).toBe(true);
    expect(form.controls.plaka.hasError(SUNUCU_HATASI)).toBe(true);
  });
});

describe('alanHatalariniAl', () => {
  it('ApiHatasi.alanlar; alansız hata → undefined', () => {
    const alanlar = { Plaka: ['x'] };
    expect(
      alanHatalariniAl(new ApiHatasi({ status: 400, kod: 'dogrulama', detay: 'd', alanlar })),
    ).toEqual(alanlar);
    expect(alanHatalariniAl(new Error('ağ'))).toBeUndefined();
  });
});
