import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

/** Sigorta / MTV / Muayene / Vade arasında gezinme (Blazor `/regulasyon` tek sayfasının bölümleri). */
@Component({
  selector: 'rc-regulation-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, TranslocoPipe],
  styleUrl: '../service-insurance.scss',
  template: `
    <nav [attr.aria-label]="'servisSigorta.regulasyon.bolumler' | transloco">
      <ul class="sekmeler">
        @for (l of links; track l.path) {
          <li>
            <a
              [routerLink]="l.path"
              routerLinkActive="etkin"
              ariaCurrentWhenActive="page"
              [routerLinkActiveOptions]="{ exact: true }"
              >{{ l.label | transloco }}</a
            >
          </li>
        }
      </ul>
    </nav>
  `,
})
export class RegulationTabs {
  protected readonly links = [
    { path: '/regulasyon', label: 'servisSigorta.sigorta.baslik' },
    { path: '/regulasyon/zeyiller', label: 'servisSigorta.zeyil.tumBaslik' },
    { path: '/regulasyon/mtv', label: 'servisSigorta.mtv.baslik' },
    { path: '/regulasyon/muayene', label: 'servisSigorta.muayene.baslik' },
    { path: '/vade', label: 'servisSigorta.vade.baslik' },
  ] as const;
}
