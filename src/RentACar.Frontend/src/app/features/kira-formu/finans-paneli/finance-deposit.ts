import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import { KF_SHARED } from '../sekmeler/ortak';
import type { AccountType } from './finans-tipleri';
import { RentalFinanceState } from './rental-finance-state';

/**
 * Depozito al (`POST finans/depozito/al`) + irat (`POST finans/depozito/irat`, GERİ ALINAMAZ → onay).
 * İkisi de işlem başına kendi `Idempotency-Key`'i. İade/mahsup depozito ekranında (cari bazında; F8.3'ten beri SPA
 * `/depozito`, router bağlantısı).
 */
@Component({
  selector: 'rc-kf-finans-depozito',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED, MoneySubmitBar, RouterLink],
  template: `
    <section class="kf-finans__islem" aria-labelledby="kf-finans-depozito">
      <h3 class="kf-finans__baslik" id="kf-finans-depozito">
        {{ 'kiraFinans.depozito.baslik' | transloco }}
      </h3>
      @if (!f.finans()) {
        <p class="kf-not">{{ 'kiraFinans.yetkiYok' | transloco }}</p>
      } @else {
        <div class="rc-form-izgara" [formGroup]="f.takeDepositForm">
          <rc-alan [etiket]="'kiraFinans.depozito.tutar' | transloco">
            <rc-para-girdisi formControlName="tutar" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.hesapTuru' | transloco">
            <rc-secim formControlName="hesap" [secenekler]="accountTypes" />
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
          [submission]="f.takeDepositSubmission"
          testId="depozito-al"
          [label]="'kiraFinans.depozito.alDugme' | transloco"
          secondary
          (send)="f.takeDeposit()"
        />

        <div class="rc-form-izgara" [formGroup]="f.forfeitForm">
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
          [submission]="f.forfeitSubmission"
          testId="depozito-irat"
          [label]="'kiraFinans.depozito.iratDugme' | transloco"
          secondary
          (send)="f.depositForfeit()"
        />
        <p class="kf-not">
          {{ 'kiraFinans.depozito.not' | transloco }}
          <a routerLink="/depozito">{{ 'kiraFinans.depozito.ekran' | transloco }}</a>
        </p>
      }
    </section>
  `,
})
export class FinanceDeposit {
  protected readonly f = inject(RentalFinanceState);
  protected readonly accountTypes: readonly SecenekOgesi<AccountType>[] = [
    { deger: 'Kasa', etiket: 'Kasa' },
    { deger: 'Banka', etiket: 'Banka' },
  ];
  protected readonly hesaplar = computed(() => this.f.accountOptions(this.f.depositAccountType()));
}
