import { FormControl, FormGroup } from '@angular/forms';

import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';

import { type FleetRental, profileValues, profileBody, createBody, newValues } from './filo-modeli';

/** Elle kurulmuş eski sözleşme: 1995 tarihli (belge tarihi sınırının DIŞINDA). */
const OLD = '1995-03-10T00:00:00Z';
const RECORD = {
  id: 'k1',
  no: 'FK-000001',
  durum: 'Aktif',
  surum: 'surum-7',
  sozlesmeNo: 'S-1',
  makbuzNo: null,
  dosyaNo: null,
  sozlesmeTarihi: OLD,
  imzaTarih: OLD,
  satisTemsilcisi: 'Ali',
  faturaTuru: 'Dönem',
  fiyatTuru: null,
  kaynak: null,
  vadeGun: 30,
  toplamKmLimiti: null,
  cikisKm: 1000,
  toplamKm: null,
  aciklama: null,
} as unknown as FleetRental;

describe('filo kiralama modeli', () => {
  it('künye PUT: dokunulmayan 1995 tarihi sunucunun ANI ile aynen gider (#271 Low-1: sınır yalnız değişende)', () => {
    const v = profileValues(RECORD);
    expect(v.sozlesmeTarihi).toBe('1995-03-10');
    const body = profileBody({ ...v, aciklama: 'yalnız açıklama' }, RECORD);
    expect(body).toEqual({
      surum: 'surum-7',
      sozlesmeNo: 'S-1',
      makbuzNo: null,
      dosyaNo: null,
      sozlesmeTarihi: OLD,
      imzaTarih: OLD,
      satisTemsilcisi: 'Ali',
      faturaTuru: 'Dönem',
      fiyatTuru: null,
      kaynak: null,
      vadeGun: 30,
      toplamKmLimiti: null,
      cikisKm: 1000,
      toplamKm: null,
      aciklama: 'yalnız açıklama',
    });
    // Değişen gün İstanbul gece yarısı olarak gider (1996-03-10 00:00 +03 = 1996-03-09T21:00Z).
    expect(profileBody({ ...v, sozlesmeTarihi: '1996-03-10' }, RECORD).sozlesmeTarihi).toBe(
      '1996-03-09T21:00:00.000Z',
    );
  });

  it('yeni sözleşme gövdesi: seçim kimlikleri, tutar invariant metin, KDV kesir, boş metin null', () => {
    const body = createBody({
      ...newValues(),
      musteri: { id: 'm1', etiket: 'Ayşe' },
      arac: { id: 'a1', etiket: '34 AB 1' },
      basTar: '2026-10-01',
      sureAy: 12,
      aylikUcret: '15000.50',
      kaynak: '   ',
    });
    expect(body).toMatchObject({
      musteriId: 'm1',
      vehicleId: 'a1',
      basTar: '2026-09-30T21:00:00.000Z',
      sureAy: 12,
      aylikUcret: '15000.50',
      kdvOrani: 0.2,
      damgaVergisi: null,
      kaynak: null,
    });
  });
});

describe('sunucu değerlerini kirli forma birleştirme (409 cakisma)', () => {
  it('dokunulmayan alan güncellenir, dokunulan korunur, ikisi de değiştiyse işaretlenir; form silinmez', () => {
    const form = new FormGroup({
      a: new FormControl<string | null>('taban-a'),
      b: new FormControl<string | null>('taban-b'),
      c: new FormControl<string | null>('taban-c'),
    });
    form.controls.b.setValue('benim-b');
    form.controls.b.markAsDirty();
    form.controls.c.setValue('benim-c');
    form.controls.c.markAsDirty();
    const conflicting = mergeServerValues(
      form,
      { a: 'sunucu-a', b: 'taban-b', c: 'sunucu-c' },
      { a: 'taban-a', b: 'taban-b', c: 'taban-c' },
      'çakışma',
    );
    expect(form.getRawValue()).toEqual({ a: 'sunucu-a', b: 'benim-b', c: 'benim-c' });
    expect(conflicting).toEqual(['c']);
    expect(form.controls.c.errors?.[SERVER_ERROR]).toEqual(['çakışma']);
    expect(form.controls.b.errors).toBeNull();
    expect(form.dirty).toBe(true);
  });

  it('seçim öğeleri kimlikle karşılaştırılır (etiket farkı çakışma değildir)', () => {
    const form = new FormGroup({ m: new FormControl<{ id: string; etiket: string } | null>(null) });
    form.controls.m.setValue({ id: 'x', etiket: 'Yeni ad' });
    form.controls.m.markAsDirty();
    const conflicting = mergeServerValues(
      form,
      { m: { id: 'x', etiket: 'X' } },
      { m: { id: 'y', etiket: 'Y' } },
      'çakışma',
    );
    expect(conflicting).toEqual([]);
  });
});
