import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';

/**
 * Kabul testi (b-fiyat-hesapla-16): fiyat motoru (`/fiyat-hesapla`) genç sürücü ücretini TOPLAMA EKLEMEZ (ücret kirada
 * sistem ek hizmet satırıdır, `FeeLineService`). Not "uygulandı" demiyordu — toplamla çelişiyordu. Motor değişmedi;
 * metin doğru şeyi söyler.
 */
describe('Fiyat Hesapla — genç sürücü notu', () => {
  it('ücretin bu toplama dahil OLMADIĞINI söyler, "uygulandı" demez', async () => {
    TestBed.configureTestingModule({ providers: [...provideTranslation()] });
    const transloco = TestBed.inject(TranslocoService);
    await firstValueFrom(transloco.load('tr'));
    const text = transloco.translate('fiyatTarife.fiyatHesapla.gencSurucu');
    expect(text).not.toContain('uygulandı');
    expect(text).toContain('DAHİL DEĞİLDİR');
  });
});
