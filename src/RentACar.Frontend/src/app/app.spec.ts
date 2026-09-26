import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';

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
    const root = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
    expect(root.querySelector('router-outlet')).not.toBeNull();
    // Canlı bölgeler içerik gelmeden de DOM'da (sonradan eklenen metin okunsun).
    expect(root.querySelectorAll('[role="alert"]').length).toBe(2);
    expect(root.querySelectorAll('[role="status"]').length).toBe(1);

    TestBed.inject(WarningBannerService).show({
      tur: 'uyari',
      mesaj: 'Yetkiniz yok.',
      kod: 'yetki_yok',
    });
    TestBed.inject(ToastService).hata('Sunucu hatası.');
    TestBed.inject(ToastService).basari('Kaydedildi.');
    await fixture.whenStable();

    expect(root.querySelector('rc-uyari-bandi [role="alert"]')?.textContent).toContain(
      'Yetkiniz yok.',
    );
    const toastAlani = root.querySelector('rc-toast-alani') as HTMLElement;
    // Durum adı ekran okuyucu için görünmez önek olarak okunur.
    expect(toastAlani.querySelector('[role="alert"]')?.textContent).toMatch(
      /Hata:\s*Sunucu hatası\./,
    );
    expect(toastAlani.querySelector('[role="status"]')?.textContent).toMatch(
      /Başarılı:\s*Kaydedildi\./,
    );
    TestBed.inject(ToastService).clear();
  });
});
