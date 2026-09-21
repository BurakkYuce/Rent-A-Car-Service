import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { appConfig } from './app.config';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: appConfig.providers,
    }).compileComponents();
  });

  it('Türkçe yapım aşaması başlığını tr.json’dan gösterir', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;

    const basliklar = kok.querySelectorAll('h1');
    expect(basliklar.length).toBe(1);
    expect(basliklar[0]?.textContent?.trim()).toBe('Yeni arayüz yapım aşamasında');
  });

  it('tema düğmeleri data-theme yazar ve seçili olanı işaretler', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const dugme = (ad: string) =>
      [...kok.querySelectorAll('button')].find((b) => b.textContent?.trim() === ad);

    dugme('Koyu')?.click();
    await fixture.whenStable();
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(dugme('Koyu')?.getAttribute('aria-pressed')).toBe('true');
    expect(dugme('Sistem')?.getAttribute('aria-pressed')).toBe('false');

    dugme('Sistem')?.click();
    await fixture.whenStable();
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });
});
