import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { SessionService } from '@core/oturum/session-service';
import type { Ben } from '@core/oturum/oturum-tipleri';

import { appConfig } from '../../app.config';
import { Placeholder } from './placeholder';

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
      imports: [Placeholder],
      providers: [
        ...appConfig.providers,
        provideRouter([]),
        { provide: SessionService, useValue: { ben: signal(BEN).asReadonly() } },
      ],
    }).compileComponents();
  });

  it('Türkçe yapım aşaması başlığını, kullanıcıyı ve firmayı gösterir', async () => {
    const fixture = TestBed.createComponent(Placeholder);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    const headers = root.querySelectorAll('h1');
    expect(headers.length).toBe(1);
    expect(headers[0]?.textContent?.trim()).toBe('Yeni arayüz yapım aşamasında');
    expect(root.textContent).toContain('Ayşe Yılmaz');
    expect(root.textContent).toContain('Pilot Firma');
  });

  it('vitrin bağlantılarını gösterir (tema ve çıkış F3.2 kabuğunda)', async () => {
    const fixture = TestBed.createComponent(Placeholder);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    expect([...root.querySelectorAll('nav a')].map((a) => a.getAttribute('href'))).toEqual([
      '/vitrin',
      '/vitrin/geri-bildirim',
      '/vitrin/form',
      '/vitrin/tanim',
      '/vitrin/tablo',
    ]);
    expect(root.querySelector('button')).toBeNull();
  });
});
