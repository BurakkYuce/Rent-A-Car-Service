import { LocationStrategy } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Icon } from '@shared/ikon/icon';

import { MAX_TABS, TabService } from './tab-service';

/**
 * Açık sekmeler çubuğu. Her sekme gerçek bağlantı (orta tık tarayıcıda yeni sekme) + kapat düğmesi.
 * Görünür sekme `aria-current="page"`. Tek sekme ana sayfaysa kapatılamaz (gidecek yer yok).
 */
@Component({
  selector: 'rc-sekme-cubugu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, TranslocoPipe],
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
                (click)="click($event, s.anahtar)"
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
        <span class="sayac" aria-hidden="true">{{ gorunum().length }}/{{ maximum }}</span>
      </nav>
    }
  `,
  styleUrl: './tab-bar.scss',
})
export class TabBar {
  protected readonly servis = inject(TabService);
  private readonly location = inject(LocationStrategy);
  protected readonly maximum = MAX_TABS;

  protected readonly gorunum = computed(() => {
    const active = this.servis.activeKey();
    const tabs = this.servis.tabs();
    const singleHomePage = tabs.length === 1 && tabs[0]?.desen === '/';
    return tabs.map((s) => ({
      anahtar: s.anahtar,
      etiket: this.servis.etiket(s),
      href: this.location.prepareExternalUrl(s.url),
      etkin: s.anahtar === active,
      kapatilabilir: !singleHomePage,
    }));
  });

  protected click(evt: MouseEvent, key: string): void {
    if (evt.button !== 0 || evt.ctrlKey || evt.metaKey || evt.shiftKey || evt.altKey) return;
    evt.preventDefault();
    void this.servis.gec(key);
  }
}
