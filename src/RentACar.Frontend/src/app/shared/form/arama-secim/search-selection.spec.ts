import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { OverlayContainer } from '@angular/cdk/overlay';
import { Observable, of, throwError } from 'rxjs';
import { provideTranslation } from '@core/i18n/ceviri';
import { SearchSelection } from './search-selection';
import type { SelectionSource, SecimSecenegi } from './selection-source';

interface Musteri extends SecimSecenegi {
  readonly tip: string;
}

const CUSTOMERS: Musteri[] = [
  { id: 'm1', etiket: 'Ahmet Yılmaz', tip: 'Bireysel' },
  { id: 'm2', etiket: 'Işık Lojistik', tip: 'Kurumsal' },
  { id: 'm3', etiket: 'İnci Kaya', tip: 'Bireysel' },
];

let calls: [string, number][] = [];
let response: () => Observable<readonly Musteri[]> = () => of(CUSTOMERS);

@Component({
  selector: 'rc-deneme-arama',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, SearchSelection],
  template: `<rc-arama-secim
    [formControl]="kontrol"
    [kaynak]="kaynak"
    [limit]="50"
    [gecikme]="20"
    ariaEtiketi="Müşteri"
  />`,
})
class SearchTestHost {
  readonly kontrol = new FormControl<SecimSecenegi | null>(null);
  readonly kaynak: SelectionSource<Musteri> = (q, limit) => {
    calls.push([q, limit]);
    return response();
  };
}

const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));

async function exchangeRate() {
  calls = [];
  response = () => of(CUSTOMERS);
  TestBed.configureTestingModule({ providers: [...provideTranslation()] });
  const fixture = TestBed.createComponent(SearchTestHost);
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  const input = root.querySelector<HTMLInputElement>('input[role="combobox"]');
  if (!input) throw new Error('combobox yok');
  const kap = TestBed.inject(OverlayContainer).getContainerElement();
  const stable = async (ms = 0) => {
    if (ms) await wait(ms);
    await fixture.whenStable();
  };
  const tus = async (key: string) => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    await stable();
  };
  const write = (text: string) => {
    input.value = text;
    input.dispatchEvent(new Event('input'));
  };
  const options = () => [...kap.querySelectorAll<HTMLElement>('[role="option"]')];
  return {
    fixture,
    girdi: input,
    kap,
    stabil: stable,
    tus,
    yaz: write,
    secenekler: options,
    kontrol: fixture.componentInstance.kontrol,
  };
}

describe('rc-arama-secim', () => {
  it('tıklayınca açılır, ilk sayfa HEMEN istenir; limit sunucu tavanına (20) kırpılır', async () => {
    const { girdi, stabil, secenekler } = await exchangeRate();
    girdi.click();
    await stabil(5);
    expect(calls).toEqual([['', 20]]);
    expect(girdi.getAttribute('aria-expanded')).toBe('true');
    expect(secenekler().map((s) => s.textContent?.trim())).toEqual([
      'Ahmet Yılmaz',
      'Işık Lojistik',
      'İnci Kaya',
    ]);
    expect(girdi.getAttribute('aria-controls')).toBe(
      secenekler()[0]?.closest('[role="listbox"]')?.id,
    );
  });

  it('yazarken gecikmeli; ara metinler için istek gitmez (son metin kazanır)', async () => {
    const { girdi, stabil, yaz } = await exchangeRate();
    girdi.click();
    await stabil(5);
    calls = [];
    yaz('ı');
    yaz('ış');
    yaz('ışı');
    await stabil(60);
    expect(calls).toEqual([['ışı', 20]]);
  });

  it('klavye: ↓ gezinir (aria-activedescendant), Enter seçer ve kapatır', async () => {
    const { girdi, stabil, tus, kontrol, secenekler } = await exchangeRate();
    await tus('ArrowDown'); // kapalıysa açar
    await stabil(5);
    expect(girdi.getAttribute('aria-activedescendant')).toBe(secenekler()[0]?.id);
    await tus('ArrowDown');
    expect(girdi.getAttribute('aria-activedescendant')).toBe(secenekler()[1]?.id);
    await tus('Enter');
    expect(kontrol.value).toEqual(CUSTOMERS[1]);
    expect(girdi.value).toBe('Işık Lojistik');
    expect(girdi.getAttribute('aria-expanded')).toBe('false');
    expect(kontrol.dirty).toBe(true);
  });

  it('Esc kapatır ve metni seçili öğeye döndürür; seçilmeden bırakılan metin değer değildir', async () => {
    const { girdi, stabil, tus, yaz, kontrol, fixture } = await exchangeRate();
    kontrol.setValue(CUSTOMERS[0] ?? null);
    await stabil();
    expect(girdi.value).toBe('Ahmet Yılmaz');
    girdi.click();
    yaz('xyz');
    await stabil(40);
    await tus('Escape');
    expect(girdi.value).toBe('Ahmet Yılmaz');
    yaz('başka');
    girdi.dispatchEvent(new Event('blur'));
    await fixture.whenStable();
    expect(girdi.value).toBe('Ahmet Yılmaz');
    expect(kontrol.value).toEqual(CUSTOMERS[0]);
    expect(kontrol.touched).toBe(true);
  });

  it('fare ile seçim; temizle düğmesi değeri null yapar', async () => {
    const { girdi, stabil, kontrol: check, secenekler, fixture } = await exchangeRate();
    girdi.click();
    await stabil(5);
    secenekler()[2]?.click();
    await stabil();
    expect(check.value).toEqual(CUSTOMERS[2]);
    const clear = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'button[aria-label="Seçimi temizle"]',
    );
    clear?.click();
    await stabil();
    expect(check.value).toBeNull();
    expect(girdi.value).toBe('');
  });

  it('hata "sonuç yok" gibi görünmez', async () => {
    const { girdi, stabil, kap } = await exchangeRate();
    response = () => throwError(() => new Error('500'));
    girdi.click();
    await stabil(5);
    expect(kap.textContent).toContain('Liste alınamadı');
    expect(kap.textContent).not.toContain('Sonuç bulunamadı');
  });
});
