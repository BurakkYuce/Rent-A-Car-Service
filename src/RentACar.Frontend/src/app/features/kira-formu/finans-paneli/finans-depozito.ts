import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import { KF_ORTAK } from '../sekmeler/ortak';
import type { HesapTuru } from './finans-tipleri';
import { KiraFinansDurumu } from './kira-finans-durumu';

/**
 * Depozito al (`POST finans/depozito/al`) + irat (`POST finans/depozito/irat`, GERİ ALINAMAZ → onay).
 * İkisi de işlem başına kendi `Idempotency-Key`'i. İade/mahsup depozito ekranında (cari bazında; F8.3'ten beri SPA
 * `/depozito`, router bağlantısı).
 */
@Component({
  selector: 'rc-kf-finans-depozito',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, MoneySubmitBar, RouterLink],
  template: `
    <section class="kf-finans__islem" aria-labelledby="kf-finans-depozito">
      <h3 class="kf-finans__baslik" id="kf-finans-depozito">
        {{ 'kiraFinans.depozito.baslik' | transloco }}
      </h3>
      @if (!f.finans()) {
        <p class="kf-not">{{ 'kiraFinans.yetkiYok' | transloco }}</p>
      } @else {
        <div class="rc-form-izgara" [formGroup]="f.depozitoAlFormu">
          <rc-alan [etiket]="'kiraFinans.depozito.tutar' | transloco">
            <rc-para-girdisi formControlName="tutar" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.hesapTuru' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="hesapTurleri" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.hesap' | transloco">
            <rc-secim
              formControlName="hesapId"
              [secenekler]="hesaplar()"
              [bosEtiket]="'kiraFinans.alan.hesapYok' | transloco"
            />
          </rc-alan>
        </div>
        <rc-money-submit
          [submission]="f.depozitoAlGonderimi"
          testId="depozito-al"
          [label]="'kiraFinans.depozito.alDugme' | transloco"
          secondary
          (send)="f.depozitoAl()"
        />

        <div class="rc-form-izgara" [formGroup]="f.iratFormu">
          <rc-alan
            [etiket]="'kiraFinans.depozito.iratTutar' | transloco"
            [ipucu]="'kiraFinans.depozito.iratIpucu' | transloco"
          >
            <rc-para-girdisi formControlName="tutar" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.aciklama' | transloco">
            <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        <rc-money-submit
          [submission]="f.iratGonderimi"
          testId="depozito-irat"
          [label]="'kiraFinans.depozito.iratDugme' | transloco"
          secondary
          (send)="f.depozitoIrat()"
        />
        <p class="kf-not">
          {{ 'kiraFinans.depozito.not' | transloco }}
          <a routerLink="/depozito">{{ 'kiraFinans.depozito.ekran' | transloco }}</a>
        </p>
      }
    </section>
  `,
})
export class FinansDepozito {
  protected readonly f = inject(KiraFinansDurumu);
  protected readonly hesapTurleri: readonly SecenekOgesi<HesapTuru>[] = [
    { deger: 'Kasa', etiket: 'Kasa' },
    { deger: 'Banka', etiket: 'Banka' },
  ];
  protected readonly hesaplar = computed(() => this.f.hesapSecenekleri(this.f.depozitoHesapTuru()));
}
