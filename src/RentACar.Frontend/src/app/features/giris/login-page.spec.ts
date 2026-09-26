import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { FULL_PAGE_NAVIGATION } from '@core/form/kaydedilmemis-degisiklik';
import { provideTranslation } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Ben, LoginCredentials } from '@core/oturum/oturum-tipleri';

import { LoginPage } from './login-page';

const serverError = (status: number, code: string) =>
  toApiError(new HttpErrorResponse({ status, error: { status, kod: code, detail: 'ayrıntı' } }));

function write(root: HTMLElement, picker: string, value: string): void {
  const input = root.querySelector<HTMLInputElement>(picker);
  if (!input) throw new Error(picker);
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('GirisSayfasi (/app/giris)', () => {
  const login = vi.fn<(b: LoginCredentials) => Promise<Ben>>();
  const navigate = vi.fn<(address: string) => void>();
  const pilot = { pilot: true } as Ben;
  const notPilot = { pilot: false } as Ben;

  beforeEach(async () => {
    login.mockReset();
    navigate.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([
          { path: 'giris', component: LoginPage },
          { path: '**', children: [] },
        ]),
        { provide: SessionService, useValue: { login: login } },
        { provide: FULL_PAGE_NAVIGATION, useValue: navigate },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function open(url: string): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    return harness.routeNativeElement as HTMLElement;
  }

  async function gonder(root: HTMLElement): Promise<void> {
    write(root, '#rc-giris-firma', 'pilot');
    write(root, '#rc-giris-kullanici', 'ayse');
    write(root, '#rc-giris-sifre', 'rastgele-parola');
    root.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
  }

  it.each([
    [400, 'dogrulama', 'Firma, kullanıcı veya parola hatalı.'],
    [429, 'cok_istek', 'Çok fazla deneme yapıldı. Lütfen biraz bekleyip yeniden deneyin.'],
    [401, 'kiraci_kapali', 'Firma hesabı kapalı. Yöneticinizle iletişime geçin.'],
  ])(
    '%i %s → Türkçe mesaj; firma/kullanıcı korunur, parola silinir',
    async (status, code, message) => {
      login.mockRejectedValue(serverError(status, code));
      const root = await open('/giris');
      await gonder(root);
      await vi.waitFor(() =>
        expect(root.querySelector('[role="alert"]')?.textContent?.trim()).toBe(message),
      );
      expect(root.querySelector<HTMLInputElement>('#rc-giris-firma')?.value).toBe('pilot');
      expect(root.querySelector<HTMLInputElement>('#rc-giris-kullanici')?.value).toBe('ayse');
      expect(root.querySelector<HTMLInputElement>('#rc-giris-sifre')?.value).toBe('');
    },
  );

  it('boş alanlar sunucuya gitmez, alan hatası gösterilir', async () => {
    const root = await open('/giris');
    root.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    await vi.waitFor(() => expect(root.querySelectorAll('.rc-alan__hata')).toHaveLength(3));
    expect(login).not.toHaveBeenCalled();
    expect(root.querySelector('#rc-giris-firma')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('pilot: /app returnUrl’e SPA içinde gider (tam sayfa geçiş yok)', async () => {
    login.mockResolvedValue(pilot);
    const root = await open('/giris?returnUrl=%2Fapp%2Fkiralar%2F5%3Fsekme%3Dodeme');
    await gonder(root);
    const router = TestBed.inject(Router);
    await vi.waitFor(() => expect(router.url).toBe('/kiralar/5?sekme=odeme'));
    expect(navigate).not.toHaveBeenCalled();
  });

  it('pilot: dönüş yoksa Panel', async () => {
    login.mockResolvedValue(pilot);
    const root = await open('/giris');
    await gonder(root);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/panel'));
  });

  it('pilot: dış returnUrl yok sayılır → Panel; ?neden=kiraci_kapali mesajı gösterilir', async () => {
    login.mockResolvedValue(pilot);
    const root = await open('/giris?neden=kiraci_kapali&returnUrl=%2F%2Fkotu.example');
    expect(root.querySelector('[role="status"]')?.textContent?.trim()).toBe(
      'Firma hesabı kapalı. Yöneticinizle iletişime geçin.',
    );
    await gonder(root);
    await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe('/panel'));
    expect(navigate).not.toHaveBeenCalled();
  });

  it.each([
    ['pilot', pilot],
    ['pilot değil', notPilot],
  ])(
    'Blazor returnUrl (%s) sunucunun /login kapısına verilir — istemci açmaz',
    async (_name, ben) => {
      login.mockResolvedValue(ben);
      const root = await open('/giris?returnUrl=%2Fkiralar%3Fvarac%3D5');
      await gonder(root);
      await vi.waitFor(() =>
        expect(navigate).toHaveBeenCalledWith('/login?ReturnUrl=%2Fkiralar%3Fvarac%3D5'),
      );
    },
  );

  it.each([
    ['/giris', '/panel'],
    ['/giris?returnUrl=%2Fapp%2Fkiralar', '/kiralar'],
  ])(
    'F13: pilot bayrağı hedefi etkilemez (%s) — SPA içinde, tam sayfa yok',
    async (url, expected) => {
      login.mockResolvedValue(notPilot);
      const root = await open(url);
      await gonder(root);
      await vi.waitFor(() => expect(TestBed.inject(Router).url).toBe(expected));
      expect(navigate).not.toHaveBeenCalled();
    },
  );
});
