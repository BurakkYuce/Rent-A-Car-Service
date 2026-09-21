import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [App] }).compileComponents();
  });

  it('Türkçe yapım aşaması başlığını gösterir', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;

    const basliklar = kok.querySelectorAll('h1');
    expect(basliklar.length).toBe(1);
    expect(basliklar[0]?.textContent?.trim()).toBe('Yeni arayüz yapım aşamasında');
  });
});
