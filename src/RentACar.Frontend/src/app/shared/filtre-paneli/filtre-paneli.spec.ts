import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTranslation } from '@core/i18n/ceviri';

import { FilterPanelComponent } from './filtre-paneli';

@Component({
  selector: 'rc-filtre-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FilterPanelComponent],
  template: `
    <rc-filtre-paneli
      [depoAnahtari]="anahtar()"
      [etkinSayisi]="active()"
      (temizle)="events.push('temizle')"
      (filtrele)="events.push('filtrele')"
    >
      <label class="rc-alan">Plaka <input name="plaka" /></label>
      <label class="rc-alan">Cari <input name="cari" /></label>
    </rc-filtre-paneli>
  `,
})
class TestHost {
  readonly anahtar = signal<string | null>('rc.filtre.deneme');
  readonly active = signal(0);
  readonly events: string[] = [];
}

describe('rc-filtre-paneli', () => {
  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideTranslation()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  async function exchangeRate(setting?: (d: TestHost) => void) {
    const fixture = TestBed.createComponent(TestHost);
    setting?.(fixture.componentInstance);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return {
      kok: root,
      d: fixture.componentInstance,
      yenile: () => fixture.whenStable(),
      acKapa: () => root.querySelector<HTMLButtonElement>('button[aria-expanded]')!,
      govde: () => root.querySelector<HTMLFormElement>('form')!,
      dugme: (text: string) =>
        [...root.querySelectorAll<HTMLButtonElement>('form button')].find(
          (b) => b.textContent?.trim() === text,
        )!,
    };
  }

  it('varsayılan açık; alanlar 6 sütunlu ızgaraya yansıtılır; bölge düğmeyle etiketli', async () => {
    const { kok, acKapa, govde } = await exchangeRate();
    expect(acKapa().getAttribute('aria-expanded')).toBe('true');
    expect(acKapa().getAttribute('aria-controls')).toBe(govde().id);
    expect(govde().getAttribute('aria-labelledby')).toBe(acKapa().id);
    expect(govde().hidden).toBe(false);
    const grid = kok.querySelector('.rc-filtre-izgara');
    expect(grid?.querySelectorAll('input')).toHaveLength(2);
  });

  it('Temizle (çerçeveli) ve Filtrele (tek dolu) çıktıları; alanda Enter = Filtrele', async () => {
    const { d, dugme: button, govde } = await exchangeRate();
    const clear = button('Temizle');
    const filter = button('Filtrele');
    expect(clear.classList).not.toContain('rc-dugme--birincil');
    expect(filter.classList).toContain('rc-dugme--birincil');
    expect(filter.type).toBe('submit');
    expect(clear.type).toBe('button');

    clear.click();
    govde().dispatchEvent(new Event('submit', { cancelable: true }));
    expect(d.events).toEqual(['temizle', 'filtrele']);
  });

  it('kapatma durumu localStorage anahtarına yazılır ve yeniden açılışta okunur', async () => {
    const first = await exchangeRate();
    first.acKapa().click();
    await first.yenile();
    expect(first.govde().hidden).toBe(true);
    expect(localStorage.getItem('rc.filtre.deneme')).toBe('0');

    const second = await exchangeRate();
    expect(second.acKapa().getAttribute('aria-expanded')).toBe('false');
    expect(second.govde().hidden).toBe(true);
  });

  it('depolama erişilemezse hata fırlatmaz, varsayılan açık kalır ve aç/kapa çalışır', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    const { acKapa, govde: body, yenile } = await exchangeRate();
    expect(body().hidden).toBe(false);
    acKapa().click();
    await yenile();
    expect(body().hidden).toBe(true);
  });

  it('anahtar verilmezse hiçbir şey saklanmaz; etkin filtre sayısı düğmede görünür', async () => {
    const { acKapa: toggle, yenile: refresh, d } = await exchangeRate((d) => d.anahtar.set(null));
    d.active.set(2);
    await refresh();
    expect(toggle().textContent).toContain('2 etkin');
    toggle().click();
    await refresh();
    expect(localStorage.length).toBe(0);
  });
});
