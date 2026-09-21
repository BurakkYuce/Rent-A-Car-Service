import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Ben } from '@core/oturum/oturum-tipleri';

import { appConfig } from '../../app.config';
import { YerTutucu } from './yer-tutucu';

const BEN: Ben = {
  kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
  kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Admin',
  izinler: [],
  subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
  moduller: { webSitesi: false },
  renkler: {},
  pilot: true,
};

describe('YerTutucu', () => {
  const cikisYap = vi.fn(() => Promise.resolve());

  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    cikisYap.mockClear();
    await TestBed.configureTestingModule({
      imports: [YerTutucu],
      providers: [
        ...appConfig.providers,
        provideRouter([]),
        { provide: OturumServisi, useValue: { ben: signal(BEN).asReadonly(), cikisYap } },
      ],
    }).compileComponents();
  });

  it('Türkçe yapım aşaması başlığını, kullanıcıyı ve firmayı gösterir', async () => {
    const fixture = TestBed.createComponent(YerTutucu);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;

    const basliklar = kok.querySelectorAll('h1');
    expect(basliklar.length).toBe(1);
    expect(basliklar[0]?.textContent?.trim()).toBe('Yeni arayüz yapım aşamasında');
    expect(kok.textContent).toContain('Ayşe Yılmaz');
    expect(kok.textContent).toContain('Pilot Firma');
  });

  it('çıkış düğmesi oturum servisinin tam temizlikli çıkışını çağırır', async () => {
    const fixture = TestBed.createComponent(YerTutucu);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    [...kok.querySelectorAll('button')].find((b) => b.textContent?.includes('Çıkış yap'))?.click();
    expect(cikisYap).toHaveBeenCalledTimes(1);
  });

  it('tema düğmeleri data-theme yazar ve seçili olanı işaretler', async () => {
    const fixture = TestBed.createComponent(YerTutucu);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const dugme = (ad: string) =>
      [...kok.querySelectorAll('button')].find((b) => b.textContent?.trim() === ad);

    dugme('Koyu')?.click();
    await fixture.whenStable();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(dugme('Koyu')?.getAttribute('aria-pressed')).toBe('true');
    expect(dugme('Sistem')?.getAttribute('aria-pressed')).toBe('false');

    dugme('Sistem')?.click();
    await fixture.whenStable();
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });
});
