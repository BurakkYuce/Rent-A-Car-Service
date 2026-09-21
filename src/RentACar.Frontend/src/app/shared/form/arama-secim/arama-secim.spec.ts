import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { OverlayContainer } from '@angular/cdk/overlay';
import { Observable, of, throwError } from 'rxjs';
import { provideCeviri } from '@core/i18n/ceviri';
import { AramaSecim } from './arama-secim';
import type { SecimKaynagi, SecimSecenegi } from './secim-kaynagi';

interface Musteri extends SecimSecenegi {
  readonly tip: string;
}

const MUSTERILER: Musteri[] = [
  { id: 'm1', etiket: 'Ahmet Yılmaz', tip: 'Bireysel' },
  { id: 'm2', etiket: 'Işık Lojistik', tip: 'Kurumsal' },
  { id: 'm3', etiket: 'İnci Kaya', tip: 'Bireysel' },
];

let cagrilar: [string, number][] = [];
let yanit: () => Observable<readonly Musteri[]> = () => of(MUSTERILER);

@Component({
  selector: 'rc-deneme-arama',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, AramaSecim],
  template: `<rc-arama-secim
    [formControl]="kontrol"
    [kaynak]="kaynak"
    [limit]="50"
    [gecikme]="20"
    ariaEtiketi="Müşteri"
  />`,
})
class DenemeArama {
  readonly kontrol = new FormControl<SecimSecenegi | null>(null);
  readonly kaynak: SecimKaynagi<Musteri> = (q, limit) => {
    cagrilar.push([q, limit]);
    return yanit();
  };
}

const bekle = (ms: number) => new Promise((r) => setTimeout(r, ms));

async function kur() {
  cagrilar = [];
  yanit = () => of(MUSTERILER);
  TestBed.configureTestingModule({ providers: [...provideCeviri()] });
  const fixture = TestBed.createComponent(DenemeArama);
  await fixture.whenStable();
  const kok = fixture.nativeElement as HTMLElement;
  const girdi = kok.querySelector<HTMLInputElement>('input[role="combobox"]');
  if (!girdi) throw new Error('combobox yok');
  const kap = TestBed.inject(OverlayContainer).getContainerElement();
  const stabil = async (ms = 0) => {
    if (ms) await bekle(ms);
    await fixture.whenStable();
  };
  const tus = async (key: string) => {
    girdi.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    await stabil();
  };
  const yaz = (metin: string) => {
    girdi.value = metin;
    girdi.dispatchEvent(new Event('input'));
  };
  const secenekler = () => [...kap.querySelectorAll<HTMLElement>('[role="option"]')];
  return {
    fixture,
    girdi,
    kap,
    stabil,
    tus,
    yaz,
    secenekler,
    kontrol: fixture.componentInstance.kontrol,
  };
}

describe('rc-arama-secim', () => {
  it('tıklayınca açılır, ilk sayfa HEMEN istenir; limit sunucu tavanına (20) kırpılır', async () => {
    const { girdi, stabil, secenekler } = await kur();
    girdi.click();
    await stabil(5);
    expect(cagrilar).toEqual([['', 20]]);
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
    const { girdi, stabil, yaz } = await kur();
    girdi.click();
    await stabil(5);
    cagrilar = [];
    yaz('ı');
    yaz('ış');
    yaz('ışı');
    await stabil(60);
    expect(cagrilar).toEqual([['ışı', 20]]);
  });

  it('klavye: ↓ gezinir (aria-activedescendant), Enter seçer ve kapatır', async () => {
    const { girdi, stabil, tus, kontrol, secenekler } = await kur();
    await tus('ArrowDown'); // kapalıysa açar
    await stabil(5);
    expect(girdi.getAttribute('aria-activedescendant')).toBe(secenekler()[0]?.id);
    await tus('ArrowDown');
    expect(girdi.getAttribute('aria-activedescendant')).toBe(secenekler()[1]?.id);
    await tus('Enter');
    expect(kontrol.value).toEqual(MUSTERILER[1]);
    expect(girdi.value).toBe('Işık Lojistik');
    expect(girdi.getAttribute('aria-expanded')).toBe('false');
    expect(kontrol.dirty).toBe(true);
  });

  it('Esc kapatır ve metni seçili öğeye döndürür; seçilmeden bırakılan metin değer değildir', async () => {
    const { girdi, stabil, tus, yaz, kontrol, fixture } = await kur();
    kontrol.setValue(MUSTERILER[0] ?? null);
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
    expect(kontrol.value).toEqual(MUSTERILER[0]);
    expect(kontrol.touched).toBe(true);
  });

  it('fare ile seçim; temizle düğmesi değeri null yapar', async () => {
    const { girdi, stabil, kontrol, secenekler, fixture } = await kur();
    girdi.click();
    await stabil(5);
    secenekler()[2]?.click();
    await stabil();
    expect(kontrol.value).toEqual(MUSTERILER[2]);
    const temizle = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      'button[aria-label="Seçimi temizle"]',
    );
    temizle?.click();
    await stabil();
    expect(kontrol.value).toBeNull();
    expect(girdi.value).toBe('');
  });

  it('hata "sonuç yok" gibi görünmez', async () => {
    const { girdi, stabil, kap } = await kur();
    yanit = () => throwError(() => new Error('500'));
    girdi.click();
    await stabil(5);
    expect(kap.textContent).toContain('Liste alınamadı');
    expect(kap.textContent).not.toContain('Sonuç bulunamadı');
  });
});
