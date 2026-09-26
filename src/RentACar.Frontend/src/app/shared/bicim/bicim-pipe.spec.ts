import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FORMAT_PIPES } from './bicim-pipe';

@Component({
  selector: 'rc-bicim-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...FORMAT_PIPES],
  template: `
    <span id="para">{{ 1234.5 | para }}</span>
    <span id="dolar">{{ -93040 | para: 'USD' }}</span>
    <span id="sayi">{{ 12000 | sayi }}</span>
    <span id="tarih">{{ '2026-08-26' | tarih }}</span>
    <span id="an">{{ '2026-08-26T21:30:00Z' | tarihSaat }}</span>
    <span id="bos">{{ null | para }}</span>
  `,
})
class FormatTest {}

describe("Biçim pipe'ları", () => {
  it('şablonda Türkçe biçim üretir', async () => {
    const fixture = TestBed.createComponent(FormatTest);
    await fixture.whenStable();
    const text = (id: string) =>
      (fixture.nativeElement as HTMLElement).querySelector(`#${id}`)?.textContent;

    expect(text('para')).toBe('1.234,50 ₺');
    expect(text('dolar')).toBe('-93.040,00 $');
    expect(text('sayi')).toBe('12.000');
    expect(text('tarih')).toBe('26.08.2026');
    expect(text('an')).toBe('27.08.2026 00:30');
    expect(text('bos')).toBe('');
  });
});
