import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { provideTranslation } from '@core/i18n/ceviri';

import { SessionService } from './session-service';
import type { Ben, LoginCredentials } from './oturum-tipleri';
import { ReloginService } from './relogin-service';

const BEN: Ben = {
  kullanici: { id: 'u-1', kullaniciAdi: 'ayse', adSoyad: 'Ayşe Yılmaz' },
  kiraci: { id: 't-1', kod: 'pilot', ad: 'Pilot Firma' },
  rol: 'Admin',
  izinler: [],
  subeKapsami: { tumSubeler: true, subeId: null, subeAd: null },
  moduller: { webSitesi: false },
  renkler: {},
  pilot: true,
};

function findDialog(): Promise<HTMLElement> {
  return vi.waitFor(() => {
    const d = document.querySelector<HTMLElement>('[role="dialog"]');
    if (!d?.querySelector('input[type="password"]')) throw new Error('diyalog yok');
    return d;
  });
}

function write(input: HTMLInputElement | null, value: string): void {
  if (!input) throw new Error('girdi yok');
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('YenidenGirisServisi (yerinde yeniden giriş diyaloğu)', () => {
  const login = vi.fn<(b: LoginCredentials) => Promise<Ben>>();

  beforeEach(async () => {
    login.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideTranslation(),
        provideRouter([]),
        {
          provide: SessionService,
          useValue: {
            ben: signal(BEN).asReadonly(),
            login: login,
            registerCleanup: () => () => true,
          },
        },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((k) => k.remove()));

  it('firma/kullanıcı kilitli gelir, ilk odak parolada; giriş başarılıysa true; eşzamanlı istekler aynı diyaloğu paylaşır', async () => {
    const service = TestBed.inject(ReloginService);
    const a = service.request();
    const b = service.request();
    expect(b).toBe(a);

    const dialog = await findDialog();
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(document.getElementById(dialog.getAttribute('aria-labelledby') ?? '')?.textContent).toBe(
      'Oturumunuz sona erdi',
    );
    const company = dialog.querySelector<HTMLInputElement>('#rc-yeniden-giris-firma');
    const user = dialog.querySelector<HTMLInputElement>('#rc-yeniden-giris-kullanici');
    const password = dialog.querySelector<HTMLInputElement>('#rc-yeniden-giris-sifre');
    await vi.waitFor(() => expect(company?.value).toBe('pilot'));
    expect(company?.readOnly).toBe(true);
    expect(user?.value).toBe('ayse');
    expect(user?.readOnly).toBe(true);
    await vi.waitFor(() => expect(document.activeElement).toBe(password));
    expect(document.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(2);

    login.mockResolvedValue(BEN);
    write(password, 'rastgele-test-parolasi');
    dialog.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();

    await expect(a).resolves.toBe(true);
    expect(login).toHaveBeenCalledWith({
      firma: 'pilot',
      kullanici: 'ayse',
      sifre: 'rastgele-test-parolasi',
    });
  });

  it('hatalı parola: genel mesaj (role=alert), diyalog açık kalır; Esc → false', async () => {
    const result = TestBed.inject(ReloginService).request();
    const dialog = await findDialog();
    login.mockRejectedValue(
      toApiError(
        new HttpErrorResponse({
          status: 400,
          error: { status: 400, kod: 'dogrulama', detail: 'x' },
        }),
      ),
    );
    write(dialog.querySelector('#rc-yeniden-giris-sifre'), 'yanlis');
    dialog.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();

    await vi.waitFor(() =>
      expect(dialog.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
        'Firma, kullanıcı veya parola hatalı.',
      ),
    );
    expect(dialog.querySelector<HTMLInputElement>('#rc-yeniden-giris-sifre')?.value).toBe('');

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await expect(result).resolves.toBe(false);
  });

  it('başka kimlikle girilirse istek tekrarlanmaz (false)', async () => {
    const result = TestBed.inject(ReloginService).request();
    const dialog = await findDialog();
    login.mockResolvedValue({ ...BEN, kullanici: { ...BEN.kullanici, id: 'baska' } });
    write(dialog.querySelector('#rc-yeniden-giris-sifre'), 'parola');
    dialog.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    await expect(result).resolves.toBe(false);
  });
});
