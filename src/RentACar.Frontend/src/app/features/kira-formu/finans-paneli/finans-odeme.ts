import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import { KF_ORTAK } from '../sekmeler/ortak';
import { KiraFinansDurumu } from './kira-finans-durumu';

/**
 * Giden havale — `POST finans/odeme` (Banka, TRY; Blazor paritesi: kiraya bağlanmaz). İşlem başına
 * `Idempotency-Key` (başlık ZORUNLU); yeniden denemede aynı, 2xx sonrası yeni.
 */
@Component({
  selector: 'rc-kf-finans-odeme',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, MoneySubmitBar],
  template: `
    <section class="kf-finans__islem" aria-labelledby="kf-finans-odeme">
      <h3 class="kf-finans__baslik" id="kf-finans-odeme">
        {{ 'kiraFinans.odeme.baslik' | transloco }}
      </h3>
      @if (!f.finans()) {
        <p class="kf-not">{{ 'kiraFinans.yetkiYok' | transloco }}</p>
      } @else {
        <div class="rc-form-izgara" [formGroup]="f.odemeFormu">
          <rc-alan [etiket]="'kiraFinans.alan.tutar' | transloco">
            <rc-para-girdisi formControlName="tutar" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.hesap' | transloco">
            <rc-secim
              formControlName="hesapId"
              [secenekler]="hesaplar()"
              [bosEtiket]="'kiraFinans.alan.hesapYok' | transloco"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.kanal' | transloco">
            <rc-secim formControlName="kanal" [secenekler]="f.kanalSecenekleri" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.aciklama' | transloco" class="rc-form-izgara__genis">
            <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        <p class="kf-not">{{ 'kiraFinans.odeme.not' | transloco }}</p>
        <rc-money-submit
          [submission]="f.odemeGonderimi"
          testId="odeme"
          [label]="'kiraFinans.odeme.dugme' | transloco"
          secondary
          (send)="f.odemeYap()"
        />
      }
    </section>
  `,
})
export class FinansOdeme {
  protected readonly f = inject(KiraFinansDurumu);
  protected readonly hesaplar = computed(() => this.f.hesapSecenekleri('Banka'));
}
