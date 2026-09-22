import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { TAM_SAYFA_GEZINMESI } from '@core/form/kaydedilmemis-degisiklik';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { provideCeviri } from '@core/i18n/ceviri';

import { izinGuard, misafirGuard, oturumGuard } from './oturum-guard';
import { OturumServisi } from './oturum-servisi';
import type { Izin } from './oturum-tipleri';

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Bos {}

describe('oturum/izin guard (canMatch)', () => {
  const girisli = signal(false);
  const pilot = signal(true);
  const izinler = signal<readonly Izin[]>([]);
  const gezin = vi.fn<(adres: string) => void>();
  const sahteOturum = {
    ilkYukleme: vi.fn(() => Promise.resolve(null)),
    girisYapildi: girisli.asReadonly(),
    ben: () => (girisli() ? { pilot: pilot() } : null),
    izinVar: (izin: Izin) => izinler().includes(izin),
  };

  beforeEach(async () => {
    girisli.set(false);
    pilot.set(true);
    izinler.set([]);
    gezin.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([
          { path: 'giris', canMatch: [misafirGuard], component: Bos },
          { path: 'kiralar', canMatch: [oturumGuard], component: Bos },
          { path: 'finans', canMatch: [izinGuard('FinanceWrite')], component: Bos },
          { path: 'panel', component: Bos },
          { path: '', component: Bos },
        ]),
        { provide: OturumServisi, useValue: sahteOturum },
        { provide: TAM_SAYFA_GEZINMESI, useValue: gezin },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('oturum yoksa girişe, gidilmek istenen adres returnUrl olarak (SİTE yolu: /app önekli)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/kiralar?durum=acik#sekme=odeme');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/giris?returnUrl=%2Fapp%2Fkiralar%3Fdurum%3Dacik%23sekme%3Dodeme');
    expect(sahteOturum.ilkYukleme).toHaveBeenCalled();
  });

  it('oturum varsa geçer; giriş sayfası girişli pilot kullanıcıyı /app returnUrl’e ya da Panel’e gönderir', async () => {
    girisli.set(true);
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
    expect(gezin).not.toHaveBeenCalled();
  });

  it('giriş sayfası: pilot olmayan girişli kullanıcı Blazor’a (tam sayfa); Blazor dönüşü sunucu kapısına', async () => {
    girisli.set(true);
    pilot.set(false);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/giris');
    expect(gezin).toHaveBeenLastCalledWith('/');
    expect(TestBed.inject(Router).url).toBe('/giris'); // geçiş bitene dek giriş sayfası; kabuk yüklenmez

    pilot.set(true);
    await harness.navigateByUrl('/giris?returnUrl=%2Fvehicles');
    expect(gezin).toHaveBeenLastCalledWith('/login?ReturnUrl=%2Fvehicles');
  });

  it('izin eksikse uyarı bandı + ana sayfa; izin varsa geçer', async () => {
    girisli.set(true);
    const harness = await RouterTestingHarness.create();
    const router = TestBed.inject(Router);

    await harness.navigateByUrl('/finans');
    expect(router.url).toBe('/');
    expect(TestBed.inject(UyariBandiServisi).bant()).toMatchObject({
      kod: 'yetki_yok',
      mesaj: 'Bu sayfayı görüntüleme yetkiniz yok.',
    });

    izinler.set(['FinanceWrite']);
    await harness.navigateByUrl('/finans');
    expect(router.url).toBe('/finans');
  });

  it('izin guard’ı oturum yoksa önce girişe yönlendirir', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/finans');
    expect(TestBed.inject(Router).url).toBe('/giris?returnUrl=%2Fapp%2Ffinans');
  });
});
