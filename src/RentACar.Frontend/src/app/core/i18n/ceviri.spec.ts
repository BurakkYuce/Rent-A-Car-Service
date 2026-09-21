import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { appConfig } from '../../app.config';
import { BosDurum } from '../../shared/bos-durum/bos-durum';
import { ceviriFonksiyonu } from './ceviri';

describe('i18n (Transloco, yalnız tr)', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('etkin dil tr; tipli çeviri fonksiyonu tr.json metnini döner', () => {
    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('tr');
    const t = TestBed.runInInjectionContext(() => ceviriFonksiyonu());
    expect(t('yerTutucu.baslik')).toBe('Yeni arayüz yapım aşamasında');
    expect(t('tema.koyu')).toBe('Koyu');
  });

  it('eksik anahtar geliştirmede görünür biçimde işaretlenir', () => {
    expect(TestBed.inject(TranslocoService).translate('yok.boyle')).toBe('EKSİK: yok.boyle');
  });

  it('boş durum bileşeni ortak Türkçe metni gösterir', async () => {
    const fixture = TestBed.createComponent(BosDurum);
    await fixture.whenStable();
    const metin = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(metin).toContain('Kayıt yok');
    expect(metin).toContain('Gösterilecek kayıt bulunamadı.');
  });
});
