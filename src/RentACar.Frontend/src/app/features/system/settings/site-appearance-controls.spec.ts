import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { provideTranslation } from '@core/i18n/translation';
import { Alan } from '@shared/form/field/field';

import type { SitePaletteOption } from './settings-model';
import { SiteColorInput } from './site-color-input';
import { SitePalettePicker } from './site-palette-picker';

/** Elle kurulmuş katalog satırları (sunucunun döndüğü biçim). */
const PALETTES: SitePaletteOption[] = [
  {
    anahtar: 'petrol',
    ad: 'Petrol ve yol sarısı',
    zemin: '#f5f6f3',
    murekkep: '#1d2227',
    ana: '#0b5d6b',
    vurgu: '#f2c230',
    geceZemin: '#121417',
  },
  {
    anahtar: 'lacivert',
    ad: 'Lacivert ve amber',
    zemin: '#f4f6f9',
    murekkep: '#17202e',
    ana: '#1f4e9c',
    vurgu: null,
    geceZemin: '#10151d',
  },
];

@Component({
  selector: 'rc-gorunum-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, Alan, SitePalettePicker, SiteColorInput],
  template: `
    <rc-alan etiket="Renk seti" grup data-t="palet">
      <rc-site-palet-secici [formControl]="palette" [paletler]="palettes()" />
    </rc-alan>
    <rc-alan etiket="Kendi ana rengim" data-t="renk">
      <rc-site-renk-girdisi [formControl]="color" [varsayilan]="'#0b5d6b'" />
    </rc-alan>
  `,
})
class AppearanceHost {
  readonly palettes = signal<readonly SitePaletteOption[]>(PALETTES);
  readonly palette = new FormControl<string | null>(null);
  readonly color = new FormControl<string | null>(null);
}

async function setup() {
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(AppearanceHost);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, root, host: fixture.componentInstance };
}

describe('site görünümü kontrolleri', () => {
  it('palet: değer yokken varsayılan (ilk) set seçili; kart seçimi anahtarı yazar; çipler setin renkleri', async () => {
    const { fixture, root, host } = await setup();
    const group = root.querySelector<HTMLElement>('[role="radiogroup"]');
    expect(group?.getAttribute('aria-labelledby')).toBeTruthy();
    const radios = root.querySelectorAll<HTMLInputElement>('[data-t="palet"] input[type="radio"]');
    expect(radios.length).toBe(2);
    expect(radios[0]?.checked).toBe(true);
    expect(radios[1]?.checked).toBe(false);

    // Çipler: zemin, mürekkep, ana, (vurgu varsa) gece zemini — vurgusuz sette dört çip.
    const chips = (key: string) =>
      [...root.querySelectorAll<HTMLElement>(`[data-palet="${key}"] .cip`)].map(
        (c) => c.style.backgroundColor,
      );
    expect(chips('petrol').length).toBe(5);
    expect(chips('lacivert').length).toBe(4);
    expect(chips('lacivert')[2]).toBe('rgb(31, 78, 156)'); // #1f4e9c

    radios[1]?.click();
    await fixture.whenStable();
    expect(host.palette.value).toBe('lacivert');
    expect(host.palette.dirty).toBe(true);

    // Liste değişir (tema değişti): seçili set yeni listede yoksa hiçbir kart seçili görünmez.
    host.palettes.set([PALETTES[0] as SitePaletteOption]);
    await fixture.whenStable();
    const after = root.querySelectorAll<HTMLInputElement>('[data-t="palet"] input[type="radio"]');
    expect(after.length).toBe(1);
    expect(after[0]?.checked).toBe(false);
  });

  it('renk: seçici ve metin aynı değeri gösterir; seçici küçük harf yazar; temizle null yapar', async () => {
    const { fixture, root, host } = await setup();
    const picker = root.querySelector<HTMLInputElement>('[data-t="renk"] input[type="color"]');
    const text = root.querySelector<HTMLInputElement>('[data-t="renk"] input[type="text"]');
    const clear = root.querySelector<HTMLButtonElement>('[data-t="renk"] button');
    if (!picker || !text || !clear) throw new Error('kontroller yok');

    // Değer yokken seçici setin ana rengini gösterir, metin boş, temizle pasif.
    expect(picker.value).toBe('#0b5d6b');
    expect(text.value).toBe('');
    expect(clear.disabled).toBe(true);
    // Etiket metin alanına bağlı; seçicinin kendi adı var.
    expect(root.querySelector('[data-t="renk"] label.rc-alan__etiket')?.getAttribute('for')).toBe(
      text.id,
    );
    expect(picker.getAttribute('aria-label')).toBeTruthy();

    picker.value = '#c0392b';
    picker.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.color.value).toBe('#c0392b');
    expect(text.value).toBe('#c0392b');

    // Metin aynen bildirilir (biçim sunucuda); geçerliyse seçici de onu gösterir, değilse setin rengini.
    text.value = '#1F4E9C';
    text.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.color.value).toBe('#1F4E9C');
    expect(picker.value).toBe('#1f4e9c');
    text.value = 'kirmizi';
    text.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(host.color.value).toBe('kirmizi');
    expect(picker.value).toBe('#0b5d6b');

    clear.click();
    await fixture.whenStable();
    expect(host.color.value).toBeNull();
    expect(text.value).toBe('');
  });
});
