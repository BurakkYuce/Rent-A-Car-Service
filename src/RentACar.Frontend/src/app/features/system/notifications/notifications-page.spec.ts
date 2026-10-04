import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { NEVER, firstValueFrom, of, throwError } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { provideTranslation } from '@core/i18n/ceviri';
import { MenuRefresh } from '@core/sayac/menu-refresh';

import { NotificationsPage } from './notifications-page';

/** Kabul testi (d-sistem-bildirim-02): okundu işaretleyince menü/zil rozeti 5 dk beklemeden tazelenir. */
describe('Bildirim merkezi — rozet tazeleme', () => {
  async function open(post: () => unknown) {
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
        { provide: ApiIstemcisi, useValue: { get: () => NEVER, post } },
        { provide: ToastService, useValue: { basari: vi.fn(), hata: vi.fn() } },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    const fixture = TestBed.createComponent(NotificationsPage);
    await fixture.whenStable();
    return {
      page: fixture.componentInstance as unknown as { markAll(): void },
      refresh: TestBed.inject(MenuRefresh),
    };
  }

  it('"hepsini oku" başarılıysa menü tazelemesi istenir', async () => {
    const { page, refresh } = await open(() => of(null));
    page.markAll();
    expect(refresh.requests()).toBe(1);
  });

  it('işlem başarısızsa menü tazelenmez (rozet değişmedi)', async () => {
    const { page, refresh } = await open(() => throwError(() => new Error('ağ')));
    page.markAll();
    expect(refresh.requests()).toBe(0);
  });
});
