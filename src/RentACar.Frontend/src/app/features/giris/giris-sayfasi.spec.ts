import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { provideCeviri } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Ben, GirisBilgileri } from '@core/oturum/oturum-tipleri';

import { GirisSayfasi } from './giris-sayfasi';

const sunucuHatasi = (status: number, kod: string) =>
  apiHatasinaCevir(new HttpErrorResponse({ status, error: { status, kod, detail: 'ayrıntı' } }));

function yaz(kok: HTMLElement, secici: string, deger: string): void {
  const girdi = kok.querySelector<HTMLInputElement>(secici);
  if (!girdi) throw new Error(secici);
  girdi.value = deger;
  girdi.dispatchEvent(new Event('input'));
}

describe('GirisSayfasi (/app/giris)', () => {
  const girisYap = vi.fn<(b: GirisBilgileri) => Promise<Ben>>();

  beforeEach(async () => {
    girisYap.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([
          { path: 'giris', component: GirisSayfasi },
          { path: '**', children: [] },
        ]),
        { provide: OturumServisi, useValue: { girisYap } },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function ac(url: string): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    return harness.routeNativeElement as HTMLElement;
  }

  async function gonder(kok: HTMLElement): Promise<void> {
    yaz(kok, '#rc-giris-firma', 'pilot');
    yaz(kok, '#rc-giris-kullanici', 'ayse');
    yaz(kok, '#rc-giris-sifre', 'rastgele-parola');
    kok.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
  }

  it.each([
    [400, 'dogrulama', 'Firma, kullanıcı veya parola hatalı.'],
    [429, 'cok_istek', 'Çok fazla deneme yapıldı. Lütfen biraz bekleyip yeniden deneyin.'],
    [401, 'kiraci_kapali', 'Firma hesabı kapalı. Yöneticinizle iletişime geçin.'],
  ])(
    '%i %s → Türkçe mesaj; firma/kullanıcı korunur, parola silinir',
    async (status, kod, mesaj) => {
      girisYap.mockRejectedValue(sunucuHatasi(status, kod));
      const kok = await ac('/giris');
      await gonder(kok);
      await vi.waitFor(() =>
        expect(kok.querySelector('[role="alert"]')?.textContent?.trim()).toBe(mesaj),
      );
      expect(kok.querySelector<HTMLInputElement>('#rc-giris-firma')?.value).toBe('pilot');
      expect(kok.querySelector<HTMLInputElement>('#rc-giris-kullanici')?.value).toBe('ayse');
      expect(kok.querySelector<HTMLInputElement>('#rc-giris-sifre')?.value).toBe('');
    },
  );

  it('boş alanlar sunucuya gitmez, alan hatası gösterilir', async () => {
    const kok = await ac('/giris');
    kok.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    await vi.waitFor(() => expect(kok.querySelectorAll('.rc-alan__hata')).toHaveLength(3));
    expect(girisYap).not.toHaveBeenCalled();
    expect(kok.querySelector('#rc-giris-firma')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('başarılı girişte returnUrl’e (yalnız iç yol) gider', async () => {
    girisYap.mockResolvedValue({} as Ben);
    const kok = await ac('/giris?returnUrl=%2Fkiralar%2F5%3Fsekme%3Dodeme');
    await gonder(kok);
    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/kiralar/5?sekme=odeme'));
  });

  it('dış returnUrl yok sayılır → ana sayfa; ?neden=kiraci_kapali mesajı gösterilir', async () => {
    girisYap.mockResolvedValue({} as Ben);
    const kok = await ac('/giris?neden=kiraci_kapali&returnUrl=%2F%2Fkotu.example');
    expect(kok.querySelector('[role="status"]')?.textContent?.trim()).toBe(
      'Firma hesabı kapalı. Yöneticinizle iletişime geçin.',
    );
    await gonder(kok);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/'));
  });
});
