import { DatePipe, DecimalPipe } from '@angular/common';
import { DEFAULT_CURRENCY_CODE, LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../../app.config';

describe('Türkçe yerel ayar', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
  });

  it("uygulama LOCALE_ID = 'tr' ve varsayılan para birimi TRY", () => {
    expect(TestBed.inject(LOCALE_ID)).toBe('tr');
    expect(TestBed.inject(DEFAULT_CURRENCY_CODE)).toBe('TRY');
  });

  it("Angular'ın kendi pipe'ları da Türkçe biçimler (yerel veri kayıtlı)", () => {
    const yerel = TestBed.inject(LOCALE_ID);
    expect(new DecimalPipe(yerel).transform(1234.5)).toBe('1.234,5');
    expect(new DatePipe(yerel).transform('2026-08-26', 'd MMMM y EEEE')).toBe(
      '26 Ağustos 2026 Çarşamba',
    );
  });
});
