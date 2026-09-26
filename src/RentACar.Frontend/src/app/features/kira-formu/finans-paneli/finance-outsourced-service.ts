import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import {
  type SelectionSource,
  serverSelectionSource,
} from '@shared/form/arama-secim/selection-source';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import { KF_SHARED } from '../sekmeler/ortak';
import { CURRENCIES, BASE_CURRENCY, displayMoney } from './finans-modeli';
import { RentalFinanceState } from './rental-finance-state';

/**
 * B2B dış hizmet alımları (tam defterli: bedel gider [araç] / tedarikçi cari + komisyon geliri) —
 * liste + kaydet (`POST finans/dis-hizmet`, işlem başına anahtar) + iptal (`POST finans/dis-hizmet/{id}/iptal`,
 * FinanceReverse; ters kayıt, geri alınamaz → onay). İzinsiz iptal düğmesi görünmez; sunucu yine 403 verirse
 * interceptor bandı gösterir.
 */
@Component({
  selector: 'rc-kf-finans-dis-hizmet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_SHARED, MoneySubmitBar],
  template: `
    @let s = f.outsourcedServices;
    <div
      class="rc-tablo-kap"
      role="region"
      tabindex="0"
      [attr.aria-label]="'kiraFinans.disHizmet.liste' | transloco"
      [attr.aria-busy]="s.isLoading()"
    >
      <table class="rc-duz-tablo" [attr.aria-label]="'kiraFinans.disHizmet.liste' | transloco">
        <thead>
          <tr>
            <th scope="col">{{ 'kiraFinans.disHizmet.no' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.disHizmet.hizmet' | transloco }}</th>
            <th scope="col" class="rc-num">{{ 'kiraFinans.disHizmet.bedel' | transloco }}</th>
            <th scope="col" class="rc-num">{{ 'kiraFinans.disHizmet.komisyon' | transloco }}</th>
            <th scope="col">{{ 'kiraFinans.alan.durum' | transloco }}</th>
            <th scope="col">
              <span class="rc-gorunmez">{{ 'kiraFinans.donem.islem' | transloco }}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          @if (s.tur() === 'hata') {
            <tr>
              <td colspan="6" class="rc-bos">{{ s.hata()?.detay }}</td>
            </tr>
          }
          @for (x of s.veri() ?? []; track x.id) {
            <tr>
              <td>{{ x.no }}</td>
              <td>{{ x.alinanHizmet }}</td>
              <td class="rc-num">{{ money(x.hizmetBedeli, x.currency) }}</td>
              <td class="rc-num">{{ x.tedarikciKomisyonOran }}</td>
              <td>{{ x.durum }}</td>
              <td>
                @if (x.durum === 'Kayitli' && f.finans() && f.reversePermission()) {
                  <button
                    type="button"
                    class="rc-dugme rc-dugme--kucuk rc-dugme--tehlike"
                    [attr.aria-label]="('kiraFinans.disHizmet.iptal' | transloco) + ' ' + x.no"
                    [disabled]="f.cancelLock.gonderiliyor()"
                    (click)="f.cancelOutsourcedService(x)"
                  >
                    {{ 'kiraFinans.disHizmet.iptal' | transloco }}
                  </button>
                }
              </td>
            </tr>
          } @empty {
            @if (s.tur() === 'hazir') {
              <tr>
                <td colspan="6" class="rc-bos">{{ 'kiraFinans.disHizmet.yok' | transloco }}</td>
              </tr>
            }
          }
        </tbody>
      </table>
    </div>

    @if (!f.finans()) {
      <p class="kf-not">{{ 'kiraFinans.yetkiYok' | transloco }}</p>
    } @else if (!f.iptal()) {
      <section
        class="kf-finans__islem"
        aria-labelledby="kf-finans-dis-hizmet"
        [formGroup]="f.outsourcedServiceForm"
      >
        <h3 class="kf-finans__baslik" id="kf-finans-dis-hizmet">
          {{ 'kiraFinans.disHizmet.yeni' | transloco }}
        </h3>
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraFinans.disHizmet.tedarikci' | transloco"
            class="rc-form-izgara__genis"
          >
            <rc-arama-secim formControlName="cari" [kaynak]="customerSource" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.hizmet' | transloco">
            <rc-metin-girdisi formControlName="alinanHizmet" [azamiUzunluk]="256" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.firma' | transloco">
            <rc-metin-girdisi formControlName="hizmetAlinanFirma" [azamiUzunluk]="256" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.bedel' | transloco">
            <rc-para-girdisi
              formControlName="hizmetBedeli"
              [paraBirimi]="f.outsourcedServiceCurrency()"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.komisyonOran' | transloco">
            <rc-sayi-girdisi formControlName="komisyonOran" [kesir]="2" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.doviz' | transloco">
            <rc-secim formControlName="doviz" [secenekler]="dovizler" />
          </rc-alan>
          @if (f.outsourcedServiceCurrency() !== temel) {
            <rc-alan
              [etiket]="'kiraFinans.alan.kur' | transloco"
              [ipucu]="'kiraFinans.ipucu.kur' | transloco"
            >
              <rc-para-girdisi formControlName="kur" [kesir]="6" yerTutucu="" />
            </rc-alan>
          }
          <rc-alan [etiket]="'kiraFinans.disHizmet.komisyonFaturaNo' | transloco">
            <rc-metin-girdisi formControlName="komisyonFaturaNo" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.aciklama' | transloco" class="rc-form-izgara__genis">
            <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
          </rc-alan>
        </div>
        <rc-money-submit
          [submission]="f.outsourcedServiceSubmission"
          testId="dis-hizmet-kaydet"
          [label]="'kiraFinans.disHizmet.kaydet' | transloco"
          (send)="f.saveOutsourcedService()"
        />
      </section>
    }
  `,
})
export class FinanceOutsourcedService {
  protected readonly f = inject(RentalFinanceState);
  protected readonly money = displayMoney;
  protected readonly temel = BASE_CURRENCY;
  protected readonly dovizler: readonly SecenekOgesi<string>[] = CURRENCIES.map((d) => ({
    deger: d,
    etiket: d,
  }));
  /** Müşteri seçimi OperationsWrite VEYA FinanceWrite ile açık (F4.4; Muhasebe tedarikçi arar), PII'sız. */
  protected readonly customerSource: SelectionSource = serverSelectionSource('musteri');
}
