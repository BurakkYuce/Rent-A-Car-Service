import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { QUERY_MESSAGE_LIMIT, QueryMessages } from './query-messages';
import { ToastService } from './toast-service';
import { WarningBannerService } from './warning-banner-service';

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Empty {}

describe('SorguMesajlari (?bilgi= / ?hata=)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Empty }])],
    });
    TestBed.inject(QueryMessages).start();
  });

  afterEach(() => TestBed.inject(ToastService).clear());

  it('bilgi → başarı toast’u, hata → hata bandı; bir kez gösterilir ve URL’den silinir (diğerleri + #sekme korunur)', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(
      '/kiralar/5?bilgi=Kira%20kaydedildi.&hata=Tahsilat%20yap%C4%B1lamad%C4%B1.&x=1#sekme=odeme',
    );
    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/kiralar/5?x=1#sekme=odeme'));
    await vi.waitFor(() =>
      expect(TestBed.inject(WarningBannerService).bant()).toEqual({
        tur: 'hata',
        mesaj: 'Tahsilat yapılamadı.',
      }),
    );
    expect(TestBed.inject(ToastService).toasts()).toEqual([
      expect.objectContaining({ durum: 'basari', mesaj: 'Kira kaydedildi.' }),
    ]);
  });

  it('uzun mesaj kısaltılır', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/?bilgi=${'a'.repeat(QUERY_MESSAGE_LIMIT + 50)}`);
    await vi.waitFor(() => expect(TestBed.inject(ToastService).toasts()).toHaveLength(1));
    expect(TestBed.inject(ToastService).toasts()[0]?.mesaj).toHaveLength(QUERY_MESSAGE_LIMIT + 1);
  });
});
