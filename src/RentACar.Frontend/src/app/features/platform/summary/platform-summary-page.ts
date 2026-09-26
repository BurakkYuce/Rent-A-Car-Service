import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { TemelStore } from '@core/veri/temel-store';
import { SayiPipe } from '@shared/bicim/bicim-pipe';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { PLATFORM_API, type PlatformSummary, toNumber } from '../platform-model';
import { PlatformSessionService } from '../platform-session';

/**
 * `/app/platform` — console landing: whole-platform summary strip (`GET platform/ozet`) with links to
 * the tenant console and the Document Center. Counts only; no tenant data.
 */
@Component({
  selector: 'rc-platform-summary-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, SayiPipe, SayfaBandi],
  styleUrls: ['../platform-page.scss'],
  template: `
    <rc-sayfa-bandi [baslik]="'platform.ozet.baslik' | transloco" ikon="chart-bar">
      <ng-container eylemler>
        <a class="rc-dugme" routerLink="/platform/kiracilar">{{
          'platform.ozet.firmalaraGit' | transloco
        }}</a>
        <a class="rc-dugme" routerLink="/platform/belgeler">{{
          'platform.ozet.belgelereGit' | transloco
        }}</a>
      </ng-container>
    </rc-sayfa-bandi>
    <div class="rc-sayfa">
      @switch (summary.durum().tur) {
        @case ('hata') {
          <div class="rc-bolum error-panel" role="alert">
            <span>{{ 'platform.yuklenemedi' | transloco }}</span>
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="summary.yenile()">
              {{ 'platform.yenidenDene' | transloco }}
            </button>
          </div>
        }
        @case ('hazir') {
          @if (summary.veri(); as s) {
            <div class="cards" data-testid="platform-ozet">
              @for (card of cards(s); track card.key) {
                <div class="rc-kart">
                  <span class="card__num">{{ card.value | sayi }}</span>
                  <span class="card__label">{{ card.key | transloco }}</span>
                </div>
              }
            </div>
          }
        }
        @default {
          <p class="muted" aria-busy="true">{{ 'platform.yukleniyor' | transloco }}</p>
        }
      }
    </div>
  `,
})
export class PlatformSummaryPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(PlatformSessionService);

  protected readonly summary = new TemelStore(() =>
    this.api.get<PlatformSummary>(`${PLATFORM_API}/ozet`),
  );

  constructor() {
    this.summary.yukle();
    effect(() => {
      const error = this.summary.hata();
      if (error) this.session.handleSessionLoss(error);
    });
  }

  protected cards(s: PlatformSummary) {
    return [
      { key: 'platform.ozet.toplamKiraci', value: toNumber(s.toplamKiraci) },
      { key: 'platform.ozet.aktif', value: toNumber(s.aktif) },
      { key: 'platform.ozet.pasif', value: toNumber(s.pasif) },
      { key: 'platform.ozet.kapali', value: toNumber(s.kapali) },
      { key: 'platform.ozet.toplamKullanici', value: toNumber(s.toplamKullanici) },
      { key: 'platform.ozet.toplamArac', value: toNumber(s.toplamArac) },
      { key: 'platform.ozet.aktifKira', value: toNumber(s.aktifKira) },
      { key: 'platform.ozet.son30Gun', value: toNumber(s.son30GunYeniKiraci) },
    ] as const;
  }
}
