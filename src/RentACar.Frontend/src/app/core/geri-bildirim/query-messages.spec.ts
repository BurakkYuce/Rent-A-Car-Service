import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';

import { QueryMessages } from './query-messages';
import { ToastService } from './toast-service';
import { WarningBannerService } from './warning-banner-service';

@Component({ selector: 'rc-bos', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class Empty {}

async function open(url: string): Promise<void> {
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
}

describe('SorguMesajlari (?bilgi= / ?hata=)', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Empty }]), ...provideTranslation()],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
    TestBed.inject(QueryMessages).start();
  });

  afterEach(() => TestBed.inject(ToastService).clear());

  it('bilinen kodlar çeviri metnine döner; bir kez gösterilir ve URL’den silinir (diğerleri + #sekme korunur)', async () => {
    await open('/kiralar/5?bilgi=kaydedildi&hata=yetki_yok&x=1#sekme=odeme');
    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/kiralar/5?x=1#sekme=odeme'));
    await vi.waitFor(() =>
      expect(TestBed.inject(WarningBannerService).bant()).toEqual({
        tur: 'hata',
        mesaj: 'Bu işlem için yetkiniz yok.',
      }),
    );
    expect(TestBed.inject(ToastService).toasts()).toEqual([
      expect.objectContaining({ durum: 'basari', mesaj: 'Kaydedildi.' }),
    ]);
  });

  it('serbest metin (içerik sahteciliği) ASLA gösterilmez: bilinmeyen değer genel metne düşer', async () => {
    const phishing = 'Hesabınız askıya alındı, 0850 000 00 00 numarasını arayın';
    await open(
      `/app/panel?hata=${encodeURIComponent(phishing)}&bilgi=${encodeURIComponent(phishing)}`,
    );
    await vi.waitFor(() => expect(TestBed.inject(WarningBannerService).bant()).not.toBeNull());
    const band = TestBed.inject(WarningBannerService).bant()?.mesaj ?? '';
    const toast = TestBed.inject(ToastService).toasts()[0]?.mesaj ?? '';
    expect(band).toBe('İşlem tamamlanamadı.');
    expect(toast).toBe('İşlem tamamlandı.');
    expect(band + toast).not.toContain('0850');
  });

  it('destek kodu yalnız "beklenmeyen" ile ve hex biçimindeyse eklenir', async () => {
    await open('/app/panel?hata=beklenmeyen&destek=0af7651916cd43dd8448eb211c80319c');
    await vi.waitFor(() =>
      expect(TestBed.inject(WarningBannerService).bant()?.mesaj).toContain(
        'Destek kodu: 0af7651916cd43dd8448eb211c80319c',
      ),
    );
  });

  it('hex olmayan destek değeri banda taşınmaz', async () => {
    await open('/app/panel?hata=beklenmeyen&destek=hemen%20aray%C4%B1n');
    await vi.waitFor(() => expect(TestBed.inject(WarningBannerService).bant()).not.toBeNull());
    expect(TestBed.inject(WarningBannerService).bant()?.mesaj).not.toContain('aray');
  });
});
