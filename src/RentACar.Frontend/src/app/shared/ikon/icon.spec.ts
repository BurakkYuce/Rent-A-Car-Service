import { TestBed } from '@angular/core/testing';
import { Icon } from './icon';
import { IKONLAR } from './ikon-kaydi';

describe('Ikon', () => {
  it('kayıttaki SVG çizilir; etiketsiz ikon süs sayılır (aria-hidden)', async () => {
    const fixture = TestBed.createComponent(Icon);
    fixture.componentRef.setInput('ad', 'sun');
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    const svg = root.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('stroke')).toBe('currentColor');
    expect(root.getAttribute('aria-hidden')).toBe('true');
    expect(root.getAttribute('role')).toBeNull();
    expect(root.style.width).toBe('16px');
  });

  it('etiket verilince img rolü ve erişilebilir ad; boyut uygulanır', async () => {
    const fixture = TestBed.createComponent(Icon);
    fixture.componentRef.setInput('ad', 'printer');
    fixture.componentRef.setInput('etiket', 'Yazdır');
    fixture.componentRef.setInput('boyut', 20);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.getAttribute('role')).toBe('img');
    expect(root.getAttribute('aria-label')).toBe('Yazdır');
    expect(root.hasAttribute('aria-hidden')).toBe(false);
    expect(root.style.height).toBe('20px');
  });

  it('kayıt harici adres ya da betik taşımaz (CSP ve güvenli innerHTML)', () => {
    for (const [name, svg] of Object.entries(IKONLAR)) {
      expect(svg.startsWith('<svg'), name).toBe(true);
      expect(svg, name).not.toMatch(/<script|on\w+=|href=|:\/\//i);
    }
  });
});
