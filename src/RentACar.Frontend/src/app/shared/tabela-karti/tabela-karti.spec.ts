import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { StatusSignCardComponent, type FleetStatus } from './tabela-karti';

@Component({
  selector: 'rc-tabela-deneme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StatusSignCardComponent],
  template: `
    <rc-tabela-karti
      [durum]="durum()"
      [deger]="deger()"
      [oran]="oran()"
      etiket="Kiradaki araçlar"
      [altMetin]="alt()"
      ikon="car"
    />
  `,
})
class Deneme {
  readonly durum = signal<FleetStatus>('kirada');
  readonly deger = signal<number | null>(1234);
  readonly oran = signal<number | null>(0.88);
  readonly alt = signal('Toplamdan %88');
}

describe('rc-tabela-karti', () => {
  async function kur() {
    const fixture = TestBed.createComponent(Deneme);
    await fixture.whenStable();
    const kok = fixture.nativeElement as HTMLElement;
    const kart = () => kok.querySelector<HTMLElement>('rc-tabela-karti')!;
    return { d: fixture.componentInstance, yenile: () => fixture.whenStable(), kart };
  }

  it('durum özniteliği, etiket, tr biçimli sayı, alt metin', async () => {
    const { kart } = await kur();
    expect(kart().dataset['durum']).toBe('kirada');
    expect(kart().querySelector('.etiket')?.textContent?.trim()).toBe('Kiradaki araçlar');
    expect(kart().querySelector('.deger')?.textContent?.trim()).toBe('1.234');
    expect(kart().querySelector('.alt')?.textContent?.trim()).toBe('Toplamdan %88');
    expect(kart().querySelector('rc-ikon')).not.toBeNull();
  });

  it('oran → erişilebilir ilerleme çubuğu (yüzde, 0–1 dışı kırpılır); oran yoksa çubuk yok', async () => {
    const { kart, d, yenile } = await kur();
    const cubuk = () => kart().querySelector<HTMLElement>('[role="progressbar"]');
    expect(cubuk()?.getAttribute('aria-valuenow')).toBe('88');
    expect(cubuk()?.getAttribute('aria-label')).toBe('Kiradaki araçlar');
    expect(cubuk()?.querySelector<HTMLElement>('.cubuk__dolu')?.style.inlineSize).toBe('88%');

    d.oran.set(1.7);
    await yenile();
    expect(cubuk()?.getAttribute('aria-valuenow')).toBe('100');
    d.oran.set(-0.2);
    await yenile();
    expect(cubuk()?.getAttribute('aria-valuenow')).toBe('0');

    d.oran.set(null);
    await yenile();
    expect(cubuk()).toBeNull();
  });

  it('değer yoksa tire; beş durumun her biri özniteliğe yansır; alt metin boşsa yok', async () => {
    const { kart, d, yenile } = await kur();
    d.deger.set(null);
    d.alt.set('');
    await yenile();
    expect(kart().querySelector('.deger')?.textContent?.trim()).toBe('—');
    expect(kart().querySelector('.alt')).toBeNull();

    for (const durum of ['kirada', 'bosta', 'serviste', 'rezerve', 'gecikmis'] as const) {
      d.durum.set(durum);
      await yenile();
      expect(kart().dataset['durum']).toBe(durum);
    }
  });
});
