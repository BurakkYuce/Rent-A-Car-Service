import { LocationStrategy } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Ikon } from '@shared/ikon/ikon';

import { EN_FAZLA_SEKME, SekmeServisi } from './sekme-servisi';

/**
 * Açık sekmeler çubuğu. Her sekme gerçek bağlantı (orta tık tarayıcıda yeni sekme) + kapat düğmesi.
 * Görünür sekme `aria-current="page"`. Tek sekme ana sayfaysa kapatılamaz (gidecek yer yok).
 */
@Component({
  selector: 'rc-sekme-cubugu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, TranslocoPipe],
  template: `
    @if (gorunum().length) {
      <nav class="cubuk" [attr.aria-label]="'kabuk.sekme.liste' | transloco">
        <ul class="liste">
          @for (s of gorunum(); track s.anahtar) {
            <li class="sekme" [class.sekme--etkin]="s.etkin">
              <a
                class="sekme__baglanti"
                [href]="s.href"
                [title]="s.etiket"
                [attr.aria-current]="s.etkin ? 'page' : null"
                (click)="tikla($event, s.anahtar)"
                >{{ s.etiket }}</a
              >
              @if (s.kapatilabilir) {
                <button
                  type="button"
                  class="sekme__kapat"
                  [attr.aria-label]="'kabuk.sekme.kapat' | transloco: { etiket: s.etiket }"
                  (click)="servis.kapat(s.anahtar)"
                >
                  <rc-ikon ad="x" [boyut]="12" />
                </button>
              }
            </li>
          }
        </ul>
        <span class="sayac" aria-hidden="true">{{ gorunum().length }}/{{ enFazla }}</span>
      </nav>
    }
  `,
  styleUrl: './sekme-cubugu.scss',
})
export class SekmeCubugu {
  protected readonly servis = inject(SekmeServisi);
  private readonly konum = inject(LocationStrategy);
  protected readonly enFazla = EN_FAZLA_SEKME;

  protected readonly gorunum = computed(() => {
    const etkin = this.servis.etkinAnahtar();
    const sekmeler = this.servis.sekmeler();
    const tekAnaSayfa = sekmeler.length === 1 && sekmeler[0]?.desen === '/';
    return sekmeler.map((s) => ({
      anahtar: s.anahtar,
      etiket: this.servis.etiket(s),
      href: this.konum.prepareExternalUrl(s.url),
      etkin: s.anahtar === etkin,
      kapatilabilir: !tekAnaSayfa,
    }));
  });

  protected tikla(olay: MouseEvent, anahtar: string): void {
    if (olay.button !== 0 || olay.ctrlKey || olay.metaKey || olay.shiftKey || olay.altKey) return;
    olay.preventDefault();
    void this.servis.gec(anahtar);
  }
}
