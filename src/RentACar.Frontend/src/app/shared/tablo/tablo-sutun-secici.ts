import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  inject,
  Injector,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Ikon } from '@shared/ikon/ikon';

export interface SutunSeciciOgesi {
  readonly kod: string;
  readonly baslik: string;
  readonly gorunur: boolean;
  readonly gizlenebilir: boolean;
  readonly oncekiyeTasinabilir: boolean;
  readonly sonrakiyeTasinabilir: boolean;
}

let sayac = 0;

/**
 * Sütun seçici: göster/gizle (onay kutusu), sıra (sola/sağa taşı — klavyeyle erişilebilir
 * sürükle-bırak karşılığı) ve varsayılana dönüş. Esc ve dışarı tıklama kapatır; açılınca odak
 * listeye, kapanınca düğmeye döner.
 */
@Component({
  selector: 'rc-tablo-sutun-secici',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Ikon],
  host: {
    '(document:click)': 'disariTiklama($event)',
    '(keydown.escape)': 'kapat(true)',
  },
  template: `
    <button
      #tetik
      type="button"
      class="rc-dugme rc-dugme--kucuk"
      [attr.aria-expanded]="acik()"
      [attr.aria-controls]="panelId"
      (click)="acik() ? kapat(false) : ac()"
    >
      <rc-ikon ad="columns-3" [boyut]="14" />
      {{ 'tablo.sutunlar' | transloco }}
    </button>
    @if (acik()) {
      <div
        class="panel"
        role="group"
        [id]="panelId"
        [attr.aria-label]="'tablo.sutunlariDuzenle' | transloco"
      >
        <ul class="liste">
          @for (o of ogeler(); track o.kod) {
            <li class="oge">
              <label class="etiket">
                <input
                  type="checkbox"
                  [checked]="o.gorunur"
                  [disabled]="!o.gizlenebilir"
                  (change)="gorunurluk.emit({ kod: o.kod, gorunur: !o.gorunur })"
                />
                <span class="metin">{{ o.baslik }}</span>
              </label>
              <button
                type="button"
                class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk rc-dugme--ikon"
                [disabled]="!o.oncekiyeTasinabilir"
                [attr.aria-label]="'tablo.solaTasi' | transloco: { baslik: o.baslik }"
                (click)="kaydir.emit({ kod: o.kod, yon: -1 })"
              >
                <rc-ikon ad="chevron-up" [boyut]="14" />
              </button>
              <button
                type="button"
                class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk rc-dugme--ikon"
                [disabled]="!o.sonrakiyeTasinabilir"
                [attr.aria-label]="'tablo.sagaTasi' | transloco: { baslik: o.baslik }"
                (click)="kaydir.emit({ kod: o.kod, yon: 1 })"
              >
                <rc-ikon ad="chevron-down" [boyut]="14" />
              </button>
            </li>
          }
        </ul>
        <p class="ipucu">{{ 'tablo.kisayollar' | transloco }}</p>
        <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="sifirla.emit()">
          {{ 'tablo.varsayilanaDon' | transloco }}
        </button>
      </div>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: inline-block;
    }
    .panel {
      position: absolute;
      inset-inline-end: 0;
      z-index: var(--rc-z-cekmece);
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-2);
      width: 17rem;
      margin-top: var(--rc-bosluk-1);
      padding: var(--rc-bosluk-2);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-lg);
      background: var(--rc-yuzey);
      box-shadow: var(--rc-golge-3);
    }
    .liste {
      max-height: 20rem;
      margin: 0;
      padding: 0;
      overflow-y: auto;
      list-style: none;
    }
    .oge {
      display: flex;
      align-items: center;
      gap: var(--rc-bosluk-0-5);
    }
    .etiket {
      display: flex;
      flex: 1;
      align-items: center;
      gap: var(--rc-bosluk-2);
      min-width: 0;
      min-height: var(--rc-kontrol-yukseklik-kucuk);
    }
    .metin {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .ipucu {
      color: var(--rc-metin-soluk);
      font-size: var(--rc-yazi-2xs);
    }
  `,
})
export class TabloSutunSecici {
  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly ogeler = input.required<readonly SutunSeciciOgesi[]>();
  readonly gorunurluk = output<{ kod: string; gorunur: boolean }>();
  readonly kaydir = output<{ kod: string; yon: -1 | 1 }>();
  readonly sifirla = output<void>();

  protected readonly panelId = `rc-tablo-sutunlar-${++sayac}`;
  protected readonly acik = signal(false);

  protected ac(): void {
    this.acik.set(true);
    afterNextRender(
      () => this.eleman.nativeElement.querySelector<HTMLInputElement>('.panel input')?.focus(),
      { injector: this.injector },
    );
  }

  protected kapat(odakGeriDon: boolean): void {
    if (!this.acik()) return;
    this.acik.set(false);
    if (odakGeriDon) this.eleman.nativeElement.querySelector<HTMLButtonElement>('button')?.focus();
  }

  protected disariTiklama(olay: MouseEvent): void {
    if (this.acik() && !this.eleman.nativeElement.contains(olay.target as Node)) {
      this.kapat(false);
    }
  }
}
