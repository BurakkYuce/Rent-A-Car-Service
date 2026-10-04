import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation, translationFunction } from '@core/i18n/ceviri';

import { errorMessages } from './error-messages';

/**
 * Kabul testi (a-kkayit-16, a-kfinans-04/19, C sabit kur): sınır değerleri Türkçe sayı biçimiyle — "En az 0.01" değil
 * "En az 0,01"; binlik nokta. Negatif yazım (a-kkayit-03/05) "Geçerli bir sayı girin" değil "En az 0 olmalı.".
 * Beklenen metinler elle yazıldı (biçimleyiciden türetilmedi).
 */
describe('alan hata mesajları — sayı biçimi', () => {
  let t: ReturnType<typeof translationFunction>;

  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...provideTranslation()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    t = TestBed.runInInjectionContext(() => translationFunction());
  });

  it.each([
    [{ min: { min: 0.01, actual: 0 } }, 'En az 0,01 olmalı.'],
    [{ min: { min: 0.000001, actual: 0 } }, 'En az 0,000001 olmalı.'],
    [{ min: { min: 0, actual: -1 } }, 'En az 0 olmalı.'],
    [{ max: { max: 1000, actual: 1001 } }, 'En çok 1.000 olabilir.'],
    [{ max: { max: 0.5, actual: 1 } }, 'En çok 0,5 olabilir.'],
    [{ sayiNegatif: true }, 'En az 0 olmalı.'],
    // Tarih sınırı aşılan sınırı söyler (a-tyeni-02: geçerlilik < başlangıç).
    [{ tarihAralikDisi: { enAz: '2026-10-05' } }, 'En erken 05.10.2026 seçilebilir.'],
    [{ tarihAralikDisi: { enCok: '2027-01-31' } }, 'En geç 31.01.2027 seçilebilir.'],
    [{ tarihAralikDisi: true }, 'Tarih izin verilen aralığın dışında.'],
  ])('%j → %s', (errors, expected) => {
    expect(errorMessages(errors, t)).toEqual([expected]);
  });
});
