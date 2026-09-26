import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';

import { FilterPanelComponent } from './filtre-paneli';

@Component({
  selector: 'rc-filtre-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FilterPanelComponent],
  template: `
    <rc-filtre-paneli
      [depoAnahtari]="anahtar()"
      [etkinSayisi]="etkin()"
      (temizle)="olaylar.push('temizle')"
      (filtrele)="olaylar.push('filtrele')"
    >
      <label class="rc-alan">Plaka <input name="plaka" /></label>
      <label class="rc-alan">Cari <input name="cari" /></label>
    </rc-filtre-paneli>
  `,
})
class Deneme {
  readonly anahtar = signal<string | null>('rc.filtre.deneme');
  readonly etkin = signal(0);
  readonly olaylar: string[] = [];
}

describe('rc-filtre-paneli', () => {
  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [...provideCeviri()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  async function kur(ayar?: (d: Deneme) => void) {
    const fixture = TestBed.createComponent(Deneme);
    ayar?.(fixture.componentInstance);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    return {
      kok,
      d: fixture.componentInstance,
      yenile: () => fixture.whenStable(),
      acKapa: () => kok.querySelector<HTMLButtonElement>('button[aria-expanded]')!,
      govde: () => kok.querySelector<HTMLFormElement>('form')!,
      dugme: (metin: string) =>
        [...kok.querySelectorAll<HTMLButtonElement>('form button')].find(
          (b) => b.textContent?.trim() === metin,
        )!,
    };
  }

  it('varsayılan açık; alanlar 6 sütunlu ızgaraya yansıtılır; bölge düğmeyle etiketli', async () => {
    const { kok, acKapa, govde } = await kur();
    expect(acKapa().getAttribute('aria-expanded')).toBe('true');
    expect(acKapa().getAttribute('aria-controls')).toBe(govde().id);
    expect(govde().getAttribute('aria-labelledby')).toBe(acKapa().id);
    expect(govde().hidden).toBe(false);
    const izgara = kok.querySelector('.rc-filtre-izgara');
    expect(izgara?.querySelectorAll('input')).toHaveLength(2);
  });

  it('Temizle (çerçeveli) ve Filtrele (tek dolu) çıktıları; alanda Enter = Filtrele', async () => {
    const { d, dugme, govde } = await kur();
    const temizle = dugme('Temizle');
    const filtrele = dugme('Filtrele');
    expect(temizle.classList).not.toContain('rc-dugme--birincil');
    expect(filtrele.classList).toContain('rc-dugme--birincil');
    expect(filtrele.type).toBe('submit');
    expect(temizle.type).toBe('button');

    temizle.click();
    govde().dispatchEvent(new Event('submit', { cancelable: true }));
    expect(d.olaylar).toEqual(['temizle', 'filtrele']);
  });

  it('kapatma durumu localStorage anahtarına yazılır ve yeniden açılışta okunur', async () => {
    const ilk = await kur();
    ilk.acKapa().click();
    await ilk.yenile();
    expect(ilk.govde().hidden).toBe(true);
    expect(localStorage.getItem('rc.filtre.deneme')).toBe('0');

    const ikinci = await kur();
    expect(ikinci.acKapa().getAttribute('aria-expanded')).toBe('false');
    expect(ikinci.govde().hidden).toBe(true);
  });

  it('depolama erişilemezse hata fırlatmaz, varsayılan açık kalır ve aç/kapa çalışır', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    const { acKapa, govde, yenile } = await kur();
    expect(govde().hidden).toBe(false);
    acKapa().click();
    await yenile();
    expect(govde().hidden).toBe(true);
  });

  it('anahtar verilmezse hiçbir şey saklanmaz; etkin filtre sayısı düğmede görünür', async () => {
    const { acKapa, yenile, d } = await kur((d) => d.anahtar.set(null));
    d.etkin.set(2);
    await yenile();
    expect(acKapa().textContent).toContain('2 etkin');
    acKapa().click();
    await yenile();
    expect(localStorage.length).toBe(0);
  });
});
