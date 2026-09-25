import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { sayiBicimle } from '@core/bicim/bicim';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

/** Filo durum sözlüğü (Yol v2 §1.2): tabela rengi durumdan gelir, başka renk yok. */
export type FleetStatus = 'kirada' | 'bosta' | 'serviste' | 'rezerve' | 'gecikmis';

/**
 * Tabela kartı (Yol v2 imza öğesi): karayolu tabelası gibi DOLU renkli durum kartı — başlık + ikon, 34 px sayı,
 * oran çubuğu (0–1) ve alt metin. Fiziksel nesne → iki temada aynı renk (`--rc-tabela-*`). Sayı değer yoksa "—".
 */
@Component({
  selector: 'rc-tabela-karti',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon],
  host: {
    class: 'rc-tabela',
    '[attr.data-durum]': 'durum()',
  },
  template: `
    <p class="ust">
      @if (ikon(); as i) {
        <rc-ikon [ad]="i" [boyut]="16" />
      }
      <span class="etiket">{{ etiket() }}</span>
    </p>
    <p class="deger">{{ degerMetni() }}</p>
    @if (yuzde() !== null) {
      <span
        class="cubuk"
        role="progressbar"
        aria-valuemin="0"
        aria-valuemax="100"
        [attr.aria-valuenow]="yuzde()"
        [attr.aria-label]="etiket()"
      >
        <span class="cubuk__dolu" [style.inline-size.%]="yuzde()"></span>
      </span>
    }
    @if (altMetin()) {
      <p class="alt">{{ altMetin() }}</p>
    }
  `,
  styles: `
    :host {
      --_zemin: var(--rc-tabela-bosta-zemin);
      --_metin: var(--rc-tabela-bosta-metin);
      --_iz: var(--rc-tabela-bar-iz);

      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-1-5);
      min-width: 0;
      padding: var(--rc-bosluk-3) var(--rc-bosluk-4);
      border-radius: var(--rc-yaricap-lg);
      background-color: var(--_zemin);
      color: var(--_metin);
    }
    @each $durum in kirada, bosta, serviste, rezerve, gecikmis {
      :host([data-durum='#{$durum}']) {
        --_zemin: var(--rc-tabela-#{$durum}-zemin);
        --_metin: var(--rc-tabela-#{$durum}-metin);
      }
    }
    // Sarı kart koyu metinli: çubuk izi koyu tonda.
    :host([data-durum='serviste']) {
      --_iz: var(--rc-tabela-bar-iz-koyu-metin);
    }
    .ust {
      display: flex;
      gap: var(--rc-bosluk-1-5);
      align-items: center;
      min-width: 0;
      font-size: var(--rc-yazi-sm);
      font-weight: var(--rc-agirlik-kalin);
    }
    .etiket {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .deger {
      font-size: calc(var(--rc-yazi-lg) * 2.125);
      font-weight: var(--rc-agirlik-kalin);
      font-variant-numeric: tabular-nums;
      letter-spacing: -0.02em;
      line-height: 1;
    }
    .cubuk {
      display: block;
      block-size: var(--rc-bosluk-1);
      overflow: hidden;
      border-radius: var(--rc-yaricap-tam);
      background-color: var(--_iz);
    }
    .cubuk__dolu {
      display: block;
      block-size: 100%;
      border-radius: inherit;
      background-color: currentColor;
    }
    .alt {
      font-size: var(--rc-yazi-xs);
      font-weight: var(--rc-agirlik-orta);
    }
  `,
})
export class StatusSignCardComponent {
  readonly durum = input<FleetStatus>('bosta');
  readonly deger = input<number | null>(null);
  /** 0–1 arası oran; verilmezse çubuk çizilmez. Aralık dışı kırpılır. */
  readonly oran = input<number | null>(null);
  readonly etiket = input.required<string>();
  readonly altMetin = input('');
  readonly ikon = input<IkonAdi | null>(null);

  protected readonly degerMetni = computed(() => {
    const d = this.deger();
    return d === null || !Number.isFinite(d) ? '—' : sayiBicimle(d, '1.0-0');
  });

  protected readonly yuzde = computed(() => {
    const o = this.oran();
    if (o === null || !Number.isFinite(o)) return null;
    return Math.round(Math.min(1, Math.max(0, o)) * 100);
  });
}
