import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';

import { SayfaBandi } from './sayfa-bandi';

@Component({
  selector: 'rc-deneme-bant',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SayfaBandi],
  template: `
    <rc-sayfa-bandi baslik="Kira listesi" ikon="key" [pill]="pill()" altMetin="Merkez">
      @if (ikinciller()) {
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
class Deneme {
  readonly pill = signal<string | null>('Kirada · 23 kayıt');
  readonly ikinciller = signal(true);
}

describe('SayfaBandi', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...provideCeviri()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function kur() {
    const f = TestBed.createComponent(Deneme);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return { f, kok: f.nativeElement as HTMLElement };
  }

  it('başlık sayfanın h1’i; ikon, pill ve ikincil metin; eylem yuvaları yerinde', async () => {
    const { f, kok } = await kur();
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
    const birincil = kok.querySelector('.rc-dugme--birincil');
    expect(birincil?.closest('.bant__eylemler')).toBeNull();

    f.componentInstance.pill.set(null);
    f.detectChanges();
    expect(kok.querySelector('.bant__pill')).toBeNull();
  });

  it('"…" düğmesi yalnız ikincil eylem varken; aç/kapat, dışarı tık ve Esc kapatır (odak düğmeye)', async () => {
    const { f, kok } = await kur();
    const dugme = () => kok.querySelector<HTMLButtonElement>('.bant__menu-dugmesi');
    const panel = () => kok.querySelector<HTMLElement>('.bant__eylemler');
    expect(dugme()?.getAttribute('aria-label')).toBe('Diğer işlemler');
    expect(dugme()?.getAttribute('aria-controls')).toBe(panel()?.id);
    expect(dugme()?.getAttribute('aria-expanded')).toBe('false');

    dugme()?.click();
    f.detectChanges();
    expect(dugme()?.getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.classList).toContain('bant__eylemler--acik');

    kok.querySelector<HTMLElement>('.disarisi')?.click();
    f.detectChanges();
    expect(dugme()?.getAttribute('aria-expanded')).toBe('false');

    dugme()?.click();
    f.detectChanges();
    const ilk = panel()?.querySelector<HTMLButtonElement>('button');
    ilk?.focus();
    ilk?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    f.detectChanges();
    expect(dugme()?.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(dugme());

    f.componentInstance.ikinciller.set(false);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(dugme()).toBeNull();
  });
});
