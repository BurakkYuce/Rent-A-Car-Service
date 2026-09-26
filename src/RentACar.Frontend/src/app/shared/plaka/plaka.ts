import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { normalizePlate } from './plaka-normalize';

export type PlateSize = 'sm' | 'md' | 'lg';

/**
 * Plaka çipi (Yol v2 imza öğesi): beyaz levha, koyu çerçeve, solda lacivert "TR" şeridi, Condensed 600 yazı.
 * Metin (textContent) yalnız plakadır; "TR" CSS içeriğidir. Fiziksel nesne → iki temada AYNI renk (`--rc-plaka-*`, tema bağımsız). Boyut sm/md/lg = 22/28/40 px.
 * Geçersiz plaka (yabancı, eski, hatalı giriş) hata fırlatmaz: şeritsiz "yabancı" varyantta ham büyük harf görünür.
 */
@Component({
  selector: 'rc-plaka',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'rc-plaka',
    '[class.rc-plaka--sm]': "boyut() === 'sm'",
    '[class.rc-plaka--lg]': "boyut() === 'lg'",
    '[class.rc-plaka--yabanci]': '!normal().gecerli',
    '[attr.data-plaka]': 'normal().kanonik',
  },
  template: `
    @if (normal().gecerli) {
      <span class="serit" aria-hidden="true"></span>
    }
    <span class="metin">{{ normal().gosterim }}</span>
  `,
  styles: `
    :host {
      --_h: 1.75rem; // md 28

      display: inline-flex;
      align-items: stretch;
      flex-shrink: 0;
      height: var(--_h);
      overflow: hidden;
      border: 1px solid var(--rc-plaka-kenar);
      border-radius: var(--rc-yaricap-sm);
      background-color: var(--rc-plaka-zemin);
      color: var(--rc-plaka-metin);
      font-family: var(--rc-font-plaka);
      font-weight: var(--rc-agirlik-kalin);
      line-height: 1;
      vertical-align: middle;
      white-space: nowrap;
    }
    :host(.rc-plaka--sm) {
      --_h: 1.375rem; // 22
    }
    :host(.rc-plaka--lg) {
      --_h: 2.5rem; // 40
    }
    .serit {
      display: flex;
      align-items: flex-end;
      justify-content: center;
      width: calc(var(--_h) * 0.4);
      padding-bottom: calc(var(--_h) * 0.08);
      background-color: var(--rc-plaka-serit);
      color: var(--rc-plaka-serit-metin);
      font-size: calc(var(--_h) * 0.28);
      letter-spacing: 0;
    }
    // "TR" CSS içeriği: plaka metni (textContent, kopyala-yapıştır, e2e) yalnız "34 ABC 123" kalır; ekran
    // okuyucuya boş alternatif metin (destekleyen tarayıcıda), desteklemeyende ilk bildirim geçerli.
    .serit::before {
      content: 'TR';
      content: 'TR' / '';
    }
    .metin {
      display: flex;
      align-items: center;
      padding-inline: calc(var(--_h) * 0.26);
      font-size: calc(var(--_h) * 0.54);
      font-variant-numeric: tabular-nums;
      letter-spacing: 0.04em;
    }
  `,
})
export class PlateChipComponent {
  readonly plaka = input.required<string | null | undefined>();
  readonly boyut = input<PlateSize>('md');

  protected readonly normal = computed(() => normalizePlate(this.plaka()));
}
