import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { provideCeviri } from '@core/i18n/ceviri';

import { izinGuard, misafirGuard, oturumGuard } from './oturum-guard';
import { OturumServisi } from './oturum-servisi';
import type { Izin } from './oturum-tipleri';

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Bos {}

describe('oturum/izin guard (canMatch)', () => {
  const girisli = signal(false);
  const izinler = signal<readonly Izin[]>([]);
  const sahteOturum = {
    ilkYukleme: vi.fn(() => Promise.resolve(null)),
    girisYapildi: girisli.asReadonly(),
    izinVar: (izin: Izin) => izinler().includes(izin),
  };

  beforeEach(async () => {
    girisli.set(false);
    izinler.set([]);
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([
          { path: 'giris', canMatch: [misafirGuard], component: Bos },
          { path: 'kiralar', canMatch: [oturumGuard], component: Bos },
          { path: 'finans', canMatch: [izinGuard('FinanceWrite')], component: Bos },
          { path: '', component: Bos },
        ]),
        { provide: OturumServisi, useValue: sahteOturum },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  it('oturum yoksa girişe, gidilmek istenen adres returnUrl olarak', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/kiralar?durum=acik#sekme=odeme');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/giris?returnUrl=%2Fkiralar%3Fdurum%3Dacik%23sekme%3Dodeme');
    expect(sahteOturum.ilkYukleme).toHaveBeenCalled();
  });

  it('oturum varsa geçer; giriş sayfası girişli kullanıcıyı returnUrl’e (yalnız iç yol) gönderir', async () => {
    girisli.set(true);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/kiralar');
    const router = TestBed.inject(Router);
    expect(router.url).toBe('/kiralar');

    await harness.navigateByUrl('/giris?returnUrl=%2Fkiralar%3Fx%3D1');
    expect(router.url).toBe('/kiralar?x=1');

    await harness.navigateByUrl('/giris?returnUrl=%2F%2Fkotu.example');
    expect(router.url).toBe('/');
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
    expect(TestBed.inject(Router).url).toBe('/giris?returnUrl=%2Ffinans');
  });
});
