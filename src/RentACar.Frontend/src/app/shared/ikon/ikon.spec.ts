import { TestBed } from '@angular/core/testing';
import { Ikon } from './ikon';
import { IKONLAR } from './ikon-kaydi';

describe('Ikon', () => {
  it('kayıttaki SVG çizilir; etiketsiz ikon süs sayılır (aria-hidden)', async () => {
    const fixture = TestBed.createComponent(Ikon);
    fixture.componentRef.setInput('ad', 'sun');
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;

    const svg = kok.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('stroke')).toBe('currentColor');
    expect(kok.getAttribute('aria-hidden')).toBe('true');
    expect(kok.getAttribute('role')).toBeNull();
    expect(kok.style.width).toBe('16px');
  });

  it('etiket verilince img rolü ve erişilebilir ad; boyut uygulanır', async () => {
    const fixture = TestBed.createComponent(Ikon);
    fixture.componentRef.setInput('ad', 'printer');
    fixture.componentRef.setInput('etiket', 'Yazdır');
    fixture.componentRef.setInput('boyut', 20);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;

    expect(kok.getAttribute('role')).toBe('img');
    expect(kok.getAttribute('aria-label')).toBe('Yazdır');
    expect(kok.hasAttribute('aria-hidden')).toBe(false);
    expect(kok.style.height).toBe('20px');
  });

  it('kayıt harici adres ya da betik taşımaz (CSP ve güvenli innerHTML)', () => {
    for (const [ad, svg] of Object.entries(IKONLAR)) {
      expect(svg.startsWith('<svg'), ad).toBe(true);
      expect(svg, ad).not.toMatch(/<script|on\w+=|href=|:\/\//i);
    }
  });
});
