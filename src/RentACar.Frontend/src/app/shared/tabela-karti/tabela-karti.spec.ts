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
      [altMetin]="sub()"
      ikon="car"
    />
  `,
})
class TestHost {
  readonly durum = signal<FleetStatus>('kirada');
  readonly deger = signal<number | null>(1234);
  readonly oran = signal<number | null>(0.88);
  readonly sub = signal('Toplamdan %88');
}

describe('rc-tabela-karti', () => {
  async function exchangeRate() {
    const fixture = TestBed.createComponent(TestHost);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const card = () => root.querySelector<HTMLElement>('rc-tabela-karti')!;
    return { d: fixture.componentInstance, yenile: () => fixture.whenStable(), kart: card };
  }

  it('durum özniteliği, etiket, tr biçimli sayı, alt metin', async () => {
    const { kart } = await exchangeRate();
    expect(kart().dataset['durum']).toBe('kirada');
    expect(kart().querySelector('.etiket')?.textContent?.trim()).toBe('Kiradaki araçlar');
    expect(kart().querySelector('.deger')?.textContent?.trim()).toBe('1.234');
    expect(kart().querySelector('.alt')?.textContent?.trim()).toBe('Toplamdan %88');
    expect(kart().querySelector('rc-ikon')).not.toBeNull();
  });

  it('oran → erişilebilir ilerleme çubuğu (yüzde, 0–1 dışı kırpılır); oran yoksa çubuk yok', async () => {
    const { kart, d, yenile } = await exchangeRate();
    const bar = () => kart().querySelector<HTMLElement>('[role="progressbar"]');
    expect(bar()?.getAttribute('aria-valuenow')).toBe('88');
    expect(bar()?.getAttribute('aria-label')).toBe('Kiradaki araçlar');
    expect(bar()?.querySelector<HTMLElement>('.cubuk__dolu')?.style.inlineSize).toBe('88%');

    d.oran.set(1.7);
    await yenile();
    expect(bar()?.getAttribute('aria-valuenow')).toBe('100');
    d.oran.set(-0.2);
    await yenile();
    expect(bar()?.getAttribute('aria-valuenow')).toBe('0');

    d.oran.set(null);
    await yenile();
    expect(bar()).toBeNull();
  });

  it('değer yoksa tire; beş durumun her biri özniteliğe yansır; alt metin boşsa yok', async () => {
    const { kart, d, yenile: refresh } = await exchangeRate();
    d.deger.set(null);
    d.sub.set('');
    await refresh();
    expect(kart().querySelector('.deger')?.textContent?.trim()).toBe('—');
    expect(kart().querySelector('.alt')).toBeNull();

    for (const status of ['kirada', 'bosta', 'serviste', 'rezerve', 'gecikmis'] as const) {
      d.durum.set(status);
      await refresh();
      expect(kart().dataset['durum']).toBe(status);
    }
  });
});
