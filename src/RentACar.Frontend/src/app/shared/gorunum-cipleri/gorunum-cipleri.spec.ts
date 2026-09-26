import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';
import { provideTurkceYerel } from '@core/yerel/tr-yerel';

import { SavedViewChipsComponent, type SavedView } from './gorunum-cipleri';

@Component({
  selector: 'rc-cip-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SavedViewChipsComponent],
  template: `<rc-gorunum-cipleri [gorunumler]="gorunumler()" (secildi)="secilen.push($event)" />`,
})
class Deneme {
  readonly gorunumler = signal<readonly SavedView[]>([]);
  readonly secilen: SavedView[] = [];
}

describe('rc-gorunum-cipleri', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideTurkceYerel(), ...provideCeviri()],
    });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function kur(gorunumler: readonly SavedView[]) {
    const fixture = TestBed.createComponent(Deneme);
    fixture.componentInstance.gorunumler.set(gorunumler);
    await fixture.whenStable();
    return { kok: fixture.nativeElement as HTMLElement, d: fixture.componentInstance };
  }

  it('bağlantı kipi: adlandırılmış nav, aktif olan aria-current=page, sayaçlar tr biçimli, hata sayacı işaretli', async () => {
    const { kok } = await kur([
      { ad: 'Tüm sözleşmeler', aktif: false, link: '/kiralar' },
      { ad: 'Kirada', sayac: 1234, aktif: true, link: '/kiralar', sorgu: { gorunum: 'kirada' } },
      { ad: 'Dönüşü gecikenler', sayac: 3, tur: 'hata', aktif: false, link: '/kiralar' },
    ]);
    const nav = kok.querySelector('nav');
    expect(nav?.getAttribute('aria-label')).toBe('Kayıtlı görünümler');
    const linkler = [...kok.querySelectorAll('a')];
    expect(linkler).toHaveLength(3);
    expect(linkler.map((a) => a.getAttribute('aria-current'))).toEqual([null, 'page', null]);
    expect(linkler[1].getAttribute('href')).toBe('/kiralar?gorunum=kirada');
    expect(linkler[1].querySelector('.sayac')?.textContent?.trim()).toBe('1.234');
    expect(linkler[0].querySelector('.sayac')).toBeNull();
    expect(linkler[2].querySelector('.sayac')?.classList).toContain('sayac--hata');
  });

  it('düğme kipi: aria-pressed ve tıklamada secildi yayılır', async () => {
    const { kok, d } = await kur([
      { ad: 'Tümü', aktif: true, id: 'tumu' },
      { ad: 'Bugün', sayac: 0, aktif: false, id: 'bugun' },
    ]);
    const dugmeler = [...kok.querySelectorAll('button')];
    expect(dugmeler.map((b) => b.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
    expect(dugmeler[1].querySelector('.sayac')?.textContent?.trim()).toBe('0');
    dugmeler[1].click();
    expect(d.secilen.map((g) => g.id)).toEqual(['bugun']);
  });
});
