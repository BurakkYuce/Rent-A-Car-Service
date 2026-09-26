import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { type SecimKaynagi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { MoneySubmitBar } from '@shared/form/money-submit/money-submit-bar';

import { KF_ORTAK } from '../sekmeler/ortak';
import { DOVIZLER, TEMEL_DOVIZ, paraGoster } from './finans-modeli';
import { KiraFinansDurumu } from './kira-finans-durumu';

/**
 * B2B dış hizmet alımları (tam defterli: bedel gider [araç] / tedarikçi cari + komisyon geliri) —
 * liste + kaydet (`POST finans/dis-hizmet`, işlem başına anahtar) + iptal (`POST finans/dis-hizmet/{id}/iptal`,
 * FinanceReverse; ters kayıt, geri alınamaz → onay). İzinsiz iptal düğmesi görünmez; sunucu yine 403 verirse
 * interceptor bandı gösterir.
 */
@Component({
  selector: 'rc-kf-finans-dis-hizmet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, MoneySubmitBar],
  template: `
    @let s = f.disHizmetler;
    <div
      class="rc-tablo-kap"
      role="region"
      tabindex="0"
      [attr.aria-label]="'kiraFinans.disHizmet.liste' | transloco"
      [attr.aria-busy]="s.yukleniyor()"
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
              <td class="rc-num">{{ para(x.hizmetBedeli, x.currency) }}</td>
              <td class="rc-num">{{ x.tedarikciKomisyonOran }}</td>
              <td>{{ x.durum }}</td>
              <td>
                @if (x.durum === 'Kayitli' && f.finans() && f.tersIzni()) {
                  <button
                    type="button"
                    class="rc-dugme rc-dugme--kucuk rc-dugme--tehlike"
                    [attr.aria-label]="('kiraFinans.disHizmet.iptal' | transloco) + ' ' + x.no"
                    [disabled]="f.iptalKilidi.gonderiliyor()"
                    (click)="f.disHizmetIptal(x)"
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
        [formGroup]="f.disHizmetFormu"
      >
        <h3 class="kf-finans__baslik" id="kf-finans-dis-hizmet">
          {{ 'kiraFinans.disHizmet.yeni' | transloco }}
        </h3>
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraFinans.disHizmet.tedarikci' | transloco"
            class="rc-form-izgara__genis"
          >
            <rc-arama-secim formControlName="cari" [kaynak]="cariKaynagi" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.hizmet' | transloco">
            <rc-metin-girdisi formControlName="alinanHizmet" [azamiUzunluk]="256" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.firma' | transloco">
            <rc-metin-girdisi formControlName="hizmetAlinanFirma" [azamiUzunluk]="256" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.bedel' | transloco">
            <rc-para-girdisi formControlName="hizmetBedeli" [paraBirimi]="f.disHizmetDovizi()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.disHizmet.komisyonOran' | transloco">
            <rc-sayi-girdisi formControlName="komisyonOran" [kesir]="2" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFinans.alan.doviz' | transloco">
            <rc-secim formControlName="doviz" [secenekler]="dovizler" />
          </rc-alan>
          @if (f.disHizmetDovizi() !== temel) {
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
          [submission]="f.disHizmetGonderimi"
          testId="dis-hizmet-kaydet"
          [label]="'kiraFinans.disHizmet.kaydet' | transloco"
          (send)="f.disHizmetKaydet()"
        />
      </section>
    }
  `,
})
export class FinansDisHizmet {
  protected readonly f = inject(KiraFinansDurumu);
  protected readonly para = paraGoster;
  protected readonly temel = TEMEL_DOVIZ;
  protected readonly dovizler: readonly SecenekOgesi<string>[] = DOVIZLER.map((d) => ({
    deger: d,
    etiket: d,
  }));
  /** Müşteri seçimi OperationsWrite VEYA FinanceWrite ile açık (F4.4; Muhasebe tedarikçi arar), PII'sız. */
  protected readonly cariKaynagi: SecimKaynagi = sunucuSecimKaynagi('musteri');
}
