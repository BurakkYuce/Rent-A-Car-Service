import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { SORGU_MESAJI_SINIRI, SorguMesajlari } from './sorgu-mesajlari';
import { ToastServisi } from './toast-servisi';
import { UyariBandiServisi } from './uyari-bandi-servisi';

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Bos {}

describe('SorguMesajlari (?bilgi= / ?hata=)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Bos }])],
    });
    TestBed.inject(SorguMesajlari).baslat();
  });

  afterEach(() => TestBed.inject(ToastServisi).temizle());

  it('bilgi → başarı toast’u, hata → hata bandı; bir kez gösterilir ve URL’den silinir (diğerleri + #sekme korunur)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(
      '/kiralar/5?bilgi=Kira%20kaydedildi.&hata=Tahsilat%20yap%C4%B1lamad%C4%B1.&x=1#sekme=odeme',
    );
    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/kiralar/5?x=1#sekme=odeme'));
    await vi.waitFor(() =>
      expect(TestBed.inject(UyariBandiServisi).bant()).toEqual({
        tur: 'hata',
        mesaj: 'Tahsilat yapılamadı.',
      }),
    );
    expect(TestBed.inject(ToastServisi).toastlar()).toEqual([
      expect.objectContaining({ durum: 'basari', mesaj: 'Kira kaydedildi.' }),
    ]);
  });

  it('uzun mesaj kısaltılır', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/?bilgi=${'a'.repeat(SORGU_MESAJI_SINIRI + 50)}`);
    await vi.waitFor(() => expect(TestBed.inject(ToastServisi).toastlar()).toHaveLength(1));
    expect(TestBed.inject(ToastServisi).toastlar()[0]?.mesaj).toHaveLength(SORGU_MESAJI_SINIRI + 1);
  });
});
