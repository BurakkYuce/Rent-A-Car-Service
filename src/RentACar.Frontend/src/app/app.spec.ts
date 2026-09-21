import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';

import { App } from './app';
import { appConfig } from './app.config';

describe('App (kök)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: appConfig.providers,
    }).compileComponents();
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('uyarı bandını ve toast yığınını canlı bölgelerde gösterir', async () => {
    const fixture = TestBed.createComponent(App);
    const kok = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    expect(kok.querySelector('router-outlet')).not.toBeNull();
    // Canlı bölgeler içerik gelmeden de DOM'da (sonradan eklenen metin okunsun).
    expect(kok.querySelectorAll('[role="alert"]').length).toBe(2);
    expect(kok.querySelectorAll('[role="status"]').length).toBe(1);

    TestBed.inject(UyariBandiServisi).goster({
      tur: 'uyari',
      mesaj: 'Yetkiniz yok.',
      kod: 'yetki_yok',
    });
    TestBed.inject(ToastServisi).hata('Sunucu hatası.');
    TestBed.inject(ToastServisi).basari('Kaydedildi.');
    await fixture.whenStable();

    expect(kok.querySelector('rc-uyari-bandi [role="alert"]')?.textContent).toContain(
      'Yetkiniz yok.',
    );
    const toastAlani = kok.querySelector('rc-toast-alani') as HTMLElement;
    // Durum adı ekran okuyucu için görünmez önek olarak okunur.
    expect(toastAlani.querySelector('[role="alert"]')?.textContent).toMatch(
      /Hata:\s*Sunucu hatası\./,
    );
    expect(toastAlani.querySelector('[role="status"]')?.textContent).toMatch(
      /Başarılı:\s*Kaydedildi\./,
    );
    TestBed.inject(ToastServisi).temizle();
  });
});
