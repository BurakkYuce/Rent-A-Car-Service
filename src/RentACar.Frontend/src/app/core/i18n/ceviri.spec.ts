import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { appConfig } from '../../app.config';
import { EmptyState } from '../../shared/bos-durum/empty-state';
import { translationFunction } from './ceviri';

describe('i18n (Transloco, yalnız tr)', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('etkin dil tr; tipli çeviri fonksiyonu tr.json metnini döner', () => {
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('tr');
    const t = TestBed.runInInjectionContext(() => translationFunction());
    expect(t('yerTutucu.baslik')).toBe('Yeni arayüz yapım aşamasında');
    expect(t('tema.koyu')).toBe('Koyu');
  });

  it('eksik anahtar geliştirmede görünür biçimde işaretlenir', () => {
    expect(TestBed.inject(TranslocoService).translate('yok.boyle')).toBe('EKSİK: yok.boyle');
  });

  it('boş durum bileşeni ortak Türkçe metni gösterir', async () => {
    const fixture = TestBed.createComponent(EmptyState);
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Kayıt yok');
    expect(text).toContain('Gösterilecek kayıt bulunamadı.');
  });
});
