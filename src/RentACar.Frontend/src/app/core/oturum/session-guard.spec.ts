import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { FULL_PAGE_NAVIGATION } from '@core/form/kaydedilmemis-degisiklik';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { provideTranslation } from '@core/i18n/ceviri';

import { permissionGuard, guestGuard, sessionGuard } from './session-guard';
import { SessionService } from './session-service';
import type { Permission } from './oturum-tipleri';

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Empty {}

describe('oturum/izin guard (canMatch)', () => {
  const loggedIn = signal(false);
  const pilot = signal(true);
  const permissions = signal<readonly Permission[]>([]);
  const navigate = vi.fn<(address: string) => void>();
  const fakeSession = {
    initialLoad: vi.fn(() => Promise.resolve(null)),
    loggedIn: loggedIn.asReadonly(),
    ben: () => (loggedIn() ? { pilot: pilot() } : null),
    izinVar: (permission: Permission) => permissions().includes(permission),
  };

  beforeEach(async () => {
    loggedIn.set(false);
    pilot.set(true);
    permissions.set([]);
    navigate.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([
          { path: 'giris', canMatch: [guestGuard], component: Empty },
          { path: 'kiralar', canMatch: [sessionGuard], component: Empty },
          { path: 'finans', canMatch: [permissionGuard('FinanceWrite')], component: Empty },
          { path: 'panel', component: Empty },
          { path: '', component: Empty },
        ]),
        { provide: SessionService, useValue: fakeSession },
        { provide: FULL_PAGE_NAVIGATION, useValue: navigate },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('oturum yoksa girişe, gidilmek istenen adres returnUrl olarak (SİTE yolu: /app önekli)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/kiralar?durum=acik#sekme=odeme');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/giris?returnUrl=%2Fapp%2Fkiralar%3Fdurum%3Dacik%23sekme%3Dodeme');
    expect(fakeSession.initialLoad).toHaveBeenCalled();
  });

  it('oturum varsa geçer; giriş sayfası girişli pilot kullanıcıyı /app returnUrl’e ya da Panel’e gönderir', async () => {
    loggedIn.set(true);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/kiralar');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/kiralar');

    await harness.navigateByUrl('/giris?returnUrl=%2Fapp%2Fkiralar%3Fx%3D1');
    expect(router.url).toBe('/kiralar?x=1');

    await harness.navigateByUrl('/giris?returnUrl=%2F%2Fkotu.example');
    expect(router.url).toBe('/panel');

    await harness.navigateByUrl('/giris?returnUrl=%2Fapp%2Fgiris');
    expect(router.url).toBe('/panel'); // döngü yok
    expect(navigate).not.toHaveBeenCalled();
  });

  it('giriş sayfası: pilot olmayan girişli kullanıcı Blazor’a (tam sayfa); Blazor dönüşü sunucu kapısına', async () => {
    loggedIn.set(true);
    pilot.set(false);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/giris');
    expect(navigate).toHaveBeenLastCalledWith('/');
    expect(TestBed.inject(Router).url).toBe('/giris'); // geçiş bitene dek giriş sayfası; kabuk yüklenmez

    pilot.set(true);
    await harness.navigateByUrl('/giris?returnUrl=%2Fvehicles');
    expect(navigate).toHaveBeenLastCalledWith('/login?ReturnUrl=%2Fvehicles');
  });

  it('izin eksikse uyarı bandı + ana sayfa; izin varsa geçer', async () => {
    loggedIn.set(true);
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/finans');
    expect(router.url).toBe('/');
    expect(TestBed.inject(WarningBannerService).bant()).toMatchObject({
      kod: 'yetki_yok',
      mesaj: 'Bu sayfayı görüntüleme yetkiniz yok.',
    });

    permissions.set(['FinanceWrite']);
    await harness.navigateByUrl('/finans');
    expect(router.url).toBe('/finans');
  });

  it('izin guard’ı oturum yoksa önce girişe yönlendirir', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/finans');
    expect(TestBed.inject(Router).url).toBe('/giris?returnUrl=%2Fapp%2Ffinans');
  });
});
