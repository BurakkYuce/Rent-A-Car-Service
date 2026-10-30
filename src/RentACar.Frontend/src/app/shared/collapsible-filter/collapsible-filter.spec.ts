import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CollapsibleFilter } from './collapsible-filter';

@Component({
  selector: 'rc-deneme-sarmal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CollapsibleFilter],
  template: `
    <rc-katlanir-filtre [etkinSayisi]="active()" [(acik)]="acik">
      <label>Plaka <input name="plaka" /></label>
    </rc-katlanir-filtre>
  `,
})
class WrapperTestHost {
  readonly active = signal(0);
  readonly acik = signal(false);
}

describe('KatlanirFiltre', () => {
  async function exchangeRate(): Promise<{
    kok: HTMLElement;
    bilesen: WrapperTestHost;
    yenile: () => Promise<void>;
  }> {
    const fixture = TestBed.createComponent(WrapperTestHost);
    await fixture.whenStable();
    return {
      kok: fixture.nativeElement as HTMLElement,
      bilesen: fixture.componentInstance,
      yenile: () => fixture.whenStable(),
    };
  }

  function dugme(root: HTMLElement): HTMLButtonElement {
    const d = root.querySelector('button');
    if (d === null) throw new Error('düğme yok');
    return d;
  }

  function panel(root: HTMLElement): HTMLElement {
    const p = root.querySelector<HTMLElement>('[role="region"]');
    if (p === null) throw new Error('panel yok');
    return p;
  }

  it('kapalı başlar: aria-expanded=false, panel gizli; düğme paneli işaret eder', async () => {
    const { kok } = await exchangeRate();
    const d = dugme(kok);
    const p = panel(kok);

    expect(d.type).toBe('button');
    expect(d.getAttribute('aria-expanded')).toBe('false');
    expect(d.getAttribute('aria-controls')).toBe(p.id);
    expect(p.getAttribute('aria-labelledby')).toBe(d.id);
    expect(p.hidden).toBe(true);
    expect(d.textContent?.trim()).toBe('Filtreler');
  });

  it('tıklayınca açılır/kapanır ve iki yönlü bağ güncellenir', async () => {
    const { kok, bilesen, yenile } = await exchangeRate();

    dugme(kok).click();
    await yenile();
    expect(dugme(kok).getAttribute('aria-expanded')).toBe('true');
    expect(panel(kok).hidden).toBe(false);
    expect(bilesen.acik()).toBe(true);
    expect(panel(kok).querySelector('input[name="plaka"]')).not.toBeNull();

    dugme(kok).click();
    await yenile();
    expect(panel(kok).hidden).toBe(true);
    expect(bilesen.acik()).toBe(false);
  });

  it('kapalıyken de etkin filtre sayısı düğme metninde görünür', async () => {
    const { kok: root, bilesen: component, yenile: refresh } = await exchangeRate();
    component.active.set(2);
    await refresh();
    expect(dugme(root).textContent?.replace(/\s+/g, ' ').trim()).toBe('Filtreler (2 etkin)');
  });

  it('her örneğin kimlikleri benzersiz', async () => {
    const a = await exchangeRate();
    const b = await exchangeRate();
    expect(dugme(a.kok).id).not.toBe(dugme(b.kok).id);
    expect(panel(a.kok).id).not.toBe(panel(b.kok).id);
  });
});
