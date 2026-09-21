import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { provideCeviri } from '@core/i18n/ceviri';

import { OturumServisi } from './oturum-servisi';
import type { Ben, GirisBilgileri } from './oturum-tipleri';
import { YenidenGirisServisi } from './yeniden-giris-servisi';

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

function diyalogBul(): Promise<HTMLElement> {
  return vi.waitFor(() => {
    const d = document.querySelector<HTMLElement>('[role="dialog"]');
    if (!d?.querySelector('input[type="password"]')) throw new Error('diyalog yok');
    return d;
  });
}

function yaz(girdi: HTMLInputElement | null, deger: string): void {
  if (!girdi) throw new Error('girdi yok');
  girdi.value = deger;
  girdi.dispatchEvent(new Event('input'));
}

describe('YenidenGirisServisi (yerinde yeniden giriş diyaloğu)', () => {
  const girisYap = vi.fn<(b: GirisBilgileri) => Promise<Ben>>();

  beforeEach(async () => {
    girisYap.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...provideCeviri(),
        provideRouter([]),
        {
          provide: OturumServisi,
          useValue: { ben: signal(BEN).asReadonly(), girisYap, temizlikKaydet: () => () => true },
        },
      ],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((k) => k.remove()));

  it('firma/kullanıcı kilitli gelir, ilk odak parolada; giriş başarılıysa true; eşzamanlı istekler aynı diyaloğu paylaşır', async () => {
    const servis = TestBed.inject(YenidenGirisServisi);
    const a = servis.iste();
    const b = servis.iste();
    expect(b).toBe(a);

    const diyalog = await diyalogBul();
    expect(diyalog.getAttribute('aria-modal')).toBe('true');
    expect(
      document.getElementById(diyalog.getAttribute('aria-labelledby') ?? '')?.textContent,
    ).toBe('Oturumunuz sona erdi');
    const firma = diyalog.querySelector<HTMLInputElement>('#rc-yeniden-giris-firma');
    const kullanici = diyalog.querySelector<HTMLInputElement>('#rc-yeniden-giris-kullanici');
    const sifre = diyalog.querySelector<HTMLInputElement>('#rc-yeniden-giris-sifre');
    await vi.waitFor(() => expect(firma?.value).toBe('pilot'));
    expect(firma?.readOnly).toBe(true);
    expect(kullanici?.value).toBe('ayse');
    expect(kullanici?.readOnly).toBe(true);
    await vi.waitFor(() => expect(document.activeElement).toBe(sifre));
    expect(document.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(2);

    girisYap.mockResolvedValue(BEN);
    yaz(sifre, 'rastgele-test-parolasi');
    diyalog.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();

    await expect(a).resolves.toBe(true);
    expect(girisYap).toHaveBeenCalledWith({
      firma: 'pilot',
      kullanici: 'ayse',
      sifre: 'rastgele-test-parolasi',
    });
  });

  it('hatalı parola: genel mesaj (role=alert), diyalog açık kalır; Esc → false', async () => {
    const sonuc = TestBed.inject(YenidenGirisServisi).iste();
    const diyalog = await diyalogBul();
    girisYap.mockRejectedValue(
      apiHatasinaCevir(
        new HttpErrorResponse({
          status: 400,
          error: { status: 400, kod: 'dogrulama', detail: 'x' },
        }),
      ),
    );
    yaz(diyalog.querySelector('#rc-yeniden-giris-sifre'), 'yanlis');
    diyalog.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();

    await vi.waitFor(() =>
      expect(diyalog.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
        'Firma, kullanıcı veya parola hatalı.',
      ),
    );
    expect(diyalog.querySelector<HTMLInputElement>('#rc-yeniden-giris-sifre')?.value).toBe('');

    diyalog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await expect(sonuc).resolves.toBe(false);
  });

  it('başka kimlikle girilirse istek tekrarlanmaz (false)', async () => {
    const sonuc = TestBed.inject(YenidenGirisServisi).iste();
    const diyalog = await diyalogBul();
    girisYap.mockResolvedValue({ ...BEN, kullanici: { ...BEN.kullanici, id: 'baska' } });
    yaz(diyalog.querySelector('#rc-yeniden-giris-sifre'), 'parola');
    diyalog.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
    await expect(sonuc).resolves.toBe(false);
  });
});
