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
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [YerTutucu],
      providers: [
        ...appConfig.providers,
        provideRouter([]),
        { provide: OturumServisi, useValue: { ben: signal(BEN).asReadonly() } },
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

  it('vitrin bağlantılarını gösterir (tema ve çıkış F3.2 kabuğunda)', async () => {
    const fixture = TestBed.createComponent(YerTutucu);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    expect([...kok.querySelectorAll('nav a')].map((a) => a.getAttribute('href'))).toEqual([
      '/vitrin',
      '/vitrin/geri-bildirim',
      '/vitrin/form',
      '/vitrin/tanim',
      '/vitrin/tablo',
    ]);
    expect(kok.querySelector('button')).toBeNull();
  });
});
