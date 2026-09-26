import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideCeviri } from '@core/i18n/ceviri';

import { PlateChipComponent, type PlateSize } from './plaka';
import { PlateSearchComponent } from './plaka-arama';
import type { NormalizedPlate } from './plaka-normalize';

@Component({
  selector: 'rc-plaka-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PlateChipComponent, PlateSearchComponent],
  template: `
    <rc-plaka [plaka]="plaka()" [boyut]="boyut()" />
    <rc-plaka-arama (ara)="aramalar.push($event)" />
  `,
})
class Deneme {
  readonly plaka = signal<string | null>('07bfg579');
  readonly boyut = signal<PlateSize>('md');
  readonly aramalar: NormalizedPlate[] = [];
}

describe('rc-plaka / rc-plaka-arama', () => {
  beforeEach(async () => {
    TestBed.configureTestingModule({ providers: [...provideCeviri()] });
    await firstValueFrom(TestBed.inject(TranslocoService).load('tr'));
  });

  async function kur() {
    const fixture = TestBed.createComponent(Deneme);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const cip = () => kok.querySelector<HTMLElement>('rc-plaka')!;
    const girdi = () => kok.querySelector<HTMLInputElement>('rc-plaka-arama input')!;
    return { kok, d: fixture.componentInstance, yenile: () => fixture.whenStable(), cip, girdi };
  }

  it('geçerli plaka: TR şeridi + boşluklu gösterim; metin yalnız plaka ("TR" CSS içeriği)', async () => {
    const { cip } = await kur();
    expect(cip().querySelector('.metin')?.textContent?.trim()).toBe('07 BFG 579');
    expect(cip().textContent?.trim()).toBe('07 BFG 579');
    const serit = cip().querySelector('.serit');
    expect(serit).not.toBeNull();
    expect(serit?.getAttribute('aria-hidden')).toBe('true');
    expect(cip().classList).not.toContain('rc-plaka--yabanci');
    expect(cip().dataset['plaka']).toBe('07BFG579');
  });

  it('geçersiz plaka hata fırlatmaz: şeritsiz yabancı varyant, ham büyük harf', async () => {
    const { cip, d, yenile } = await kur();
    d.plaka.set('b 1234 xy');
    await yenile();
    expect(cip().classList).toContain('rc-plaka--yabanci');
    expect(cip().querySelector('.serit')).toBeNull();
    expect(cip().querySelector('.metin')?.textContent?.trim()).toBe('B 1234 XY');

    d.plaka.set(null);
    await yenile();
    expect(cip().querySelector('.metin')?.textContent?.trim()).toBe('');
  });

  it('boyut sınıfı: sm / md (varsayılan, sınıfsız) / lg', async () => {
    const { cip, d, yenile } = await kur();
    expect(cip().classList).not.toContain('rc-plaka--sm');
    expect(cip().classList).not.toContain('rc-plaka--lg');
    d.boyut.set('sm');
    await yenile();
    expect(cip().classList).toContain('rc-plaka--sm');
    d.boyut.set('lg');
    await yenile();
    expect(cip().classList).toContain('rc-plaka--lg');
  });

  it('arama: etiketli girdi, yazarken kök büyük harf (i → I), Enter normalize sonucu yayar', async () => {
    const { kok, d, girdi } = await kur();
    const label = kok.querySelector('rc-plaka-arama label');
    expect(label?.getAttribute('for')).toBe(girdi().id);
    expect(label?.textContent?.trim()).toBe('Plaka ile ara');

    girdi().value = '34 ibc 123';
    girdi().dispatchEvent(new Event('input'));
    expect(girdi().value).toBe('34 IBC 123');

    girdi().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(d.aramalar).toHaveLength(1);
    expect(d.aramalar[0]).toEqual({
      ham: '34 IBC 123',
      kanonik: '34IBC123',
      gosterim: '34 IBC 123',
      gecerli: true,
    });
  });

  it('arama: boş Enter de yayılır (gecerli=false) — bilgi mesajı çağıranın işi', async () => {
    const { d, girdi } = await kur();
    girdi().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(d.aramalar).toHaveLength(1);
    expect(d.aramalar[0].gecerli).toBe(false);
  });
});
