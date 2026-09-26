import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';
import { provideTurkishLocale } from '@core/yerel/tr-yerel';

import { SavedViewChipsComponent, type SavedView } from './gorunum-cipleri';

@Component({
  selector: 'rc-cip-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SavedViewChipsComponent],
  template: `<rc-gorunum-cipleri [gorunumler]="views()" (secildi)="selected.push($event)" />`,
})
class TestHost {
  readonly views = signal<readonly SavedView[]>([]);
  readonly selected: SavedView[] = [];
}

describe('rc-gorunum-cipleri', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideTurkishLocale(), ...provideTranslation()],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function exchangeRate(views: readonly SavedView[]) {
    const fixture = TestBed.createComponent(TestHost);
    fixture.componentInstance.views.set(views);
    await fixture.whenStable();
    return { kok: fixture.nativeElement as HTMLElement, d: fixture.componentInstance };
  }

  it('bağlantı kipi: adlandırılmış nav, aktif olan aria-current=page, sayaçlar tr biçimli, hata sayacı işaretli', async () => {
    const { kok } = await exchangeRate([
      { ad: 'Tüm sözleşmeler', aktif: false, link: '/kiralar' },
      { ad: 'Kirada', sayac: 1234, aktif: true, link: '/kiralar', sorgu: { gorunum: 'kirada' } },
      { ad: 'Dönüşü gecikenler', sayac: 3, tur: 'hata', aktif: false, link: '/kiralar' },
    ]);
    const nav = kok.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Kayıtlı görünümler');
    const links = [...kok.querySelectorAll('a')];
    expect(links).toHaveLength(3);
    expect(links.map((a) => a.getAttribute('aria-current'))).toEqual([null, 'page', null]);
    expect(links[1].getAttribute('href')).toBe('/kiralar?gorunum=kirada');
    expect(links[1].querySelector('.sayac')?.textContent?.trim()).toBe('1.234');
    expect(links[0].querySelector('.sayac')).toBeNull();
    expect(links[2].querySelector('.sayac')?.classList).toContain('sayac--hata');
  });

  it('düğme kipi: aria-pressed ve tıklamada secildi yayılır', async () => {
    const { kok: root, d } = await exchangeRate([
      { ad: 'Tümü', aktif: true, id: 'tumu' },
      { ad: 'Bugün', sayac: 0, aktif: false, id: 'bugun' },
    ]);
    const buttons = [...root.querySelectorAll('button')];
    expect(buttons.map((b) => b.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
    expect(buttons[1].querySelector('.sayac')?.textContent?.trim()).toBe('0');
    buttons[1].click();
    expect(d.selected.map((g) => g.id)).toEqual(['bugun']);
  });
});
