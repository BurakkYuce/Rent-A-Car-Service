import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { normalizePlate, type NormalizedPlate } from './plaka-normalize';

let nextId = 0;

/**
 * Plaka ile hızlı arama kutusu (Yol v2 §5.2): TR şeritli, Condensed 600. Yazarken kök büyük harfe çevrilir
 * ("i" → "I"; Türkçe "İ" değil — plakada yok). Enter → `ara` normalize edilmiş sonucu yayar (boş ya da geçersiz
 * dahil: bilgi toast'u ve yönlendirme çağıranın işi — bu bileşen rota/servis bilmez). `type="text"` (rol textbox):
 * `searchbox` olsaydı sayfadaki `getByRole('searchbox', { name: 'Ara' })` "Hızlı araç arama"yı da yakalardı.
 */
@Component({
  selector: 'rc-plaka-arama',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    <label class="rc-gorunmez" [for]="kimlik">{{
      etiket() || ('ortak.plaka.aramaEtiketi' | transloco)
    }}</label>
    <span class="serit" aria-hidden="true"></span>
    <input
      #girdi
      class="girdi"
      type="text"
      inputmode="text"
      autocomplete="off"
      autocapitalize="characters"
      spellcheck="false"
      enterkeyhint="search"
      maxlength="16"
      [id]="kimlik"
      [placeholder]="'ortak.plaka.aramaIpucu' | transloco"
      (input)="buyut(girdi)"
      (keydown.enter)="gonder($event, girdi)"
    />
  `,
  styles: `
    :host {
      display: inline-flex;
      align-items: stretch;
      width: 12rem;
      max-width: 100%;
      height: var(--rc-kontrol-yukseklik);
      overflow: hidden;
      border: 1px solid var(--rc-plaka-kenar);
      border-radius: var(--rc-yaricap-md);
      background-color: var(--rc-plaka-zemin);
      color: var(--rc-plaka-metin);
    }
    :host(:focus-within) {
      outline: var(--rc-odak-kalinlik) solid var(--rc-odak);
      outline-offset: var(--rc-odak-bosluk);
    }
    .serit {
      display: flex;
      flex-shrink: 0;
      align-items: flex-end;
      justify-content: center;
      width: 0.875rem;
      padding-bottom: var(--rc-bosluk-0-5);
      background-color: var(--rc-plaka-serit);
      color: var(--rc-plaka-serit-metin);
      font-family: var(--rc-font-plaka);
      font-size: calc(var(--rc-yazi-2xs) * 0.75);
      font-weight: var(--rc-agirlik-kalin);
    }
    .serit::before {
      content: 'TR';
      content: 'TR' / '';
    }
    .girdi {
      flex: 1 1 auto;
      min-width: 0;
      padding: 0 var(--rc-bosluk-2);
      border: 0;
      background: transparent;
      color: inherit;
      font-family: var(--rc-font-plaka);
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-kalin);
      letter-spacing: 0.04em;
    }
    .girdi::placeholder {
      color: var(--rc-ham-murekkep-600);
      font-weight: var(--rc-agirlik-orta);
      letter-spacing: 0;
    }
    .girdi:focus-visible {
      outline: none;
    }
  `,
})
export class PlateSearchComponent {
  /** Görünmez etiket (verilmezse "Plaka ile ara"). */
  readonly etiket = input('');
  /** Enter: normalize edilmiş plaka (geçersiz/boş da yayılır; `gecerli` ile ayırın). */
  readonly ara = output<NormalizedPlate>();

  protected readonly kimlik = `rc-plaka-arama-${nextId++}`;

  protected buyut(el: HTMLInputElement): void {
    const upper = el.value.toLocaleUpperCase('en-US').replaceAll('İ', 'I');
    if (upper === el.value) return;
    const start = el.selectionStart;
    const end = el.selectionEnd;
    el.value = upper;
    // Kök büyük harf bu alfabede uzunluğu değiştirmez: imleç yerinde kalır.
    if (start !== null && end !== null) el.setSelectionRange(start, end);
  }

  protected gonder(event: Event, el: HTMLInputElement): void {
    event.preventDefault();
    this.ara.emit(normalizePlate(el.value));
  }
}
