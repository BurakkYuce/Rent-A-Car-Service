import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';

import { OnayServisi } from './onay-servisi';

function diyalogBul(): Promise<HTMLElement> {
  return vi.waitFor(() => {
    const d = document.querySelector<HTMLElement>('[role="alertdialog"]');
    if (!d?.querySelector('.rc-onay-onayla')) throw new Error('diyalog yok');
    return d;
  });
}

describe('OnayServisi (CDK onay diyaloğu)', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...provideCeviri()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((k) => k.remove()));

  it('erişilebilir modal: alertdialog, aria-modal, başlık/açıklama bağlı, odak kilidi; Onayla → true', async () => {
    const sonuc = TestBed.inject(OnayServisi).sor({
      baslik: 'Kayıt silinsin mi?',
      mesaj: 'Geri alınamaz.',
    });
    const diyalog = await diyalogBul();

    expect(diyalog.getAttribute('aria-modal')).toBe('true');
    expect(
      document.getElementById(diyalog.getAttribute('aria-labelledby') ?? '')?.textContent,
    ).toBe('Kayıt silinsin mi?');
    expect(
      document.getElementById(diyalog.getAttribute('aria-describedby') ?? '')?.textContent?.trim(),
    ).toBe('Geri alınamaz.');
    // CDK odak kilidi: içeriğin iki yanında odak çapaları.
    expect(document.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(2);
    expect(diyalog.querySelector('.rc-onay-iptal')?.textContent?.trim()).toBe('Vazgeç');

    diyalog.querySelector<HTMLButtonElement>('.rc-onay-onayla')?.click();
    await expect(sonuc).resolves.toBe(true);
  });

  it('Esc → false (vazgeç); tehlikeli işlemde ilk odak "Vazgeç"', async () => {
    const sonuc = TestBed.inject(OnayServisi).sor({
      baslik: 'Kira iptal edilsin mi?',
      mesaj: 'Geri alınamaz.',
      tehlikeli: true,
      onayEtiketi: 'İptal et',
    });
    const diyalog = await diyalogBul();
    expect(diyalog.querySelector('.rc-onay-onayla')?.textContent?.trim()).toBe('İptal et');
    expect(diyalog.querySelector('.rc-onay-onayla')?.classList).toContain('rc-dugme--tehlike');
    await vi.waitFor(() =>
      expect(document.activeElement?.classList.contains('rc-onay-iptal')).toBe(true),
    );

    // CDK Esc'i keyCode ile tanır (gerçek tarayıcı ikisini de doldurur).
    diyalog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }),
    );
    await expect(sonuc).resolves.toBe(false);
  });
});
