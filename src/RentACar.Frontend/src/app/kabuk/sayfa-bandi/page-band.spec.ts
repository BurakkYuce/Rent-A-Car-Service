import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';

import { PageBand } from './page-band';

@Component({
  selector: 'rc-deneme-bant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand],
  template: `
    <rc-sayfa-bandi baslik="Kira listesi" ikon="key" [pill]="pill()" altMetin="Merkez">
      @if (secondaries()) {
        <ng-container eylemler>
          <button type="button" class="rc-dugme">Excel</button>
          <button type="button" class="rc-dugme">Yazdır</button>
        </ng-container>
      }
      <button birincil type="button" class="rc-dugme rc-dugme--birincil">Yeni kira</button>
    </rc-sayfa-bandi>
    <p class="disarisi">Dışarısı</p>
  `,
})
class TestHost {
  readonly pill = signal<string | null>('Kirada · 23 kayıt');
  readonly secondaries = signal(true);
}

describe('SayfaBandi', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...provideTranslation()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function exchangeRate() {
    const f = TestBed.createComponent(TestHost);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return { f, kok: f.nativeElement as HTMLElement };
  }

  it('başlık sayfanın h1’i; ikon, pill ve ikincil metin; eylem yuvaları yerinde', async () => {
    const { f, kok } = await exchangeRate();
    const h1 = kok.querySelectorAll('h1');
    expect(h1).toHaveLength(1);
    expect(h1[0]?.textContent?.trim()).toBe('Kira listesi');
    expect(kok.querySelector('rc-sayfa-bandi rc-ikon')).not.toBeNull();
    expect(kok.querySelector('.bant__pill')?.textContent?.trim()).toBe('Kirada · 23 kayıt');
    expect(kok.querySelector('.bant__alt')?.textContent?.trim()).toBe('Merkez');
    expect(
      [...kok.querySelectorAll('.bant__eylemler button')].map((b) => b.textContent?.trim()),
    ).toEqual(['Excel', 'Yazdır']);
    // Birincil ikincil menüsünün DIŞINDA: mobilde "…" altına inmez.
    const primary = kok.querySelector('.rc-dugme--birincil');
    expect(primary?.closest('.bant__eylemler')).toBeNull();

    f.componentInstance.pill.set(null);
    f.detectChanges();
    expect(kok.querySelector('.bant__pill')).toBeNull();
  });

  it('"…" düğmesi yalnız ikincil eylem varken; aç/kapat, dışarı tık ve Esc kapatır (odak düğmeye)', async () => {
    const { f, kok: root } = await exchangeRate();
    const button = () => root.querySelector<HTMLButtonElement>('.bant__menu-dugmesi');
    const panel = () => root.querySelector<HTMLElement>('.bant__eylemler');
    expect(button()?.getAttribute('aria-label')).toBe('Diğer işlemler');
    expect(button()?.getAttribute('aria-controls')).toBe(panel()?.id);
    expect(button()?.getAttribute('aria-expanded')).toBe('false');

    button()?.click();
    f.detectChanges();
    expect(button()?.getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.classList).toContain('bant__eylemler--acik');

    root.querySelector<HTMLElement>('.disarisi')?.click();
    f.detectChanges();
    expect(button()?.getAttribute('aria-expanded')).toBe('false');

    button()?.click();
    f.detectChanges();
    const first = panel()?.querySelector<HTMLButtonElement>('button');
    first?.focus();
    first?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    f.detectChanges();
    expect(button()?.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(button());

    f.componentInstance.secondaries.set(false);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(button()).toBeNull();
  });
});
