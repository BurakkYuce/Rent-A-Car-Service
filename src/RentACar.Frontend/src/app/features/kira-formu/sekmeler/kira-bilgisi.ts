import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { KF_ORTAK } from './ortak';

/**
 * KİRA BİLGİSİ / TESLİMAT — tarih/ofis/tür KANONİK alanları (Hızlı Giriş aynalar). Düzenlemede tarihler
 * donuk (değişiklik yalnız Uzat), km/aşım parametreleri kayıttan sonra; teslim mini formu teslim
 * edilmemiş açık kirada.
 */
@Component({
  selector: 'rc-kf-kira-bilgisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK],
  template: `
    <div [formGroup]="d.form">
      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.kiraBilgisi' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan
            [etiket]="'kiraFormu.alan.basTar' | transloco"
            [ipucu]="d.yeni ? '' : ('kiraFormu.ipucu.tarihUzat' | transloco)"
          >
            <rc-tarih-saat-secici formControlName="basTar" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.bitTar' | transloco">
            <rc-tarih-saat-secici formControlName="bitTar" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.cikisOfisi' | transloco">
            <rc-arama-secim formControlName="cikisOfisi" [kaynak]="d.lokasyonKaynagi" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.donusOfisi' | transloco">
            <rc-arama-secim formControlName="donusOfisi" [kaynak]="d.lokasyonKaynagi" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.kiralamaTuru' | transloco">
            <rc-secim
              formControlName="kiralamaTuru"
              [secenekler]="d.kiralamaTurleri()"
              bosEtiket="—"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.faturalamaTipi' | transloco">
            <rc-secim
              formControlName="faturalamaTipi"
              [secenekler]="d.faturalamaTipleri()"
              bosEtiket="—"
            />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.donemsel' | transloco" etiketGizli>
            <rc-onay-kutusu formControlName="donemselFaturalama">{{
              'kiraFormu.alan.donemsel' | transloco
            }}</rc-onay-kutusu>
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.kaynak' | transloco">
            <rc-metin-girdisi formControlName="kaynak" liste="kf-dl-kaynak" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.talepTuru' | transloco">
            <rc-metin-girdisi formControlName="talepTuru" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.geldigiBirim' | transloco">
            <rc-metin-girdisi formControlName="geldigiBirim" [azamiUzunluk]="64" />
          </rc-alan>
          <div class="kf-bilgi">
            <span>{{ 'kiraFormu.alan.islemSube' | transloco }}</span>
            <strong>{{
              d.yeni
                ? ('kiraFormu.alan.subeTuretilir' | transloco)
                : (d.gorunenDetay()?.islemSubeAdi ?? ('kiraFormu.alan.subeEslenmemis' | transloco))
            }}</strong>
          </div>
        </div>
      </section>

      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.km' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.kmLimit' | transloco">
            <rc-sayi-girdisi formControlName="kmLimit" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.fazlaKmUcret' | transloco">
            <rc-para-girdisi formControlName="fazlaKmUcret" [paraBirimi]="d.kiraDovizi()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.yakitBirimUcret' | transloco">
            <rc-para-girdisi formControlName="yakitBirimUcret" [paraBirimi]="d.kiraDovizi()" />
          </rc-alan>
        </div>
        <p class="kf-not">
          {{ (d.yeni ? 'kiraFormu.not.kmKayittanSonra' : 'kiraFormu.not.km') | transloco }}
        </p>
      </section>

      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.opsiyon' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.opsiyonNet' | transloco">
            <rc-para-girdisi formControlName="opsiyonNet" [paraBirimi]="d.kiraDovizi()" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.opsiyonGun' | transloco">
            <rc-sayi-girdisi formControlName="opsiyonGun" />
          </rc-alan>
          <div class="kf-bilgi">
            <span>{{ 'kiraFormu.alan.hediyeGun' | transloco }}</span>
            <strong>{{ (d.yeni ? d.hesap.veri()?.hediyeGun : d.kira()?.hediyeGun) ?? '—' }}</strong>
          </div>
          <rc-alan [etiket]="'kiraFormu.alan.otomatikUzat' | transloco" etiketGizli>
            <rc-onay-kutusu formControlName="otomatikUzat">{{
              'kiraFormu.alan.otomatikUzat' | transloco
            }}</rc-onay-kutusu>
          </rc-alan>
        </div>
      </section>

      <section class="kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.kabis' | transloco }}</h3>
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'kiraFormu.alan.kabisCikis' | transloco" etiketGizli>
            <rc-onay-kutusu formControlName="kabisCikis">{{
              'kiraFormu.alan.kabisCikis' | transloco
            }}</rc-onay-kutusu>
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.kabisDonus' | transloco" etiketGizli>
            <rc-onay-kutusu formControlName="kabisDonus">{{
              'kiraFormu.alan.kabisDonus' | transloco
            }}</rc-onay-kutusu>
          </rc-alan>
        </div>
        <p class="kf-not">{{ 'kiraFormu.not.kabis' | transloco }}</p>
      </section>
    </div>

    <section class="kf-kart">
      <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.teslimat' | transloco }}</h3>
      @if (d.kirada() && !d.teslimEdildi()) {
        <div class="rc-form-izgara" [formGroup]="d.teslimFormu" data-testid="teslim-formu">
          <rc-alan [etiket]="'kiraFormu.alan.cikisKm' | transloco">
            <rc-sayi-girdisi formControlName="cikisKm" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.cikisYakit' | transloco">
            <rc-sayi-girdisi formControlName="cikisYakit" />
          </rc-alan>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme rc-dugme--birincil"
              [disabled]="!d.operasyon() || d.teslimGonderimi.gonderiliyor()"
              (click)="d.teslimEt()"
            >
              {{ 'kiraFormu.eylem.teslimEt' | transloco }}
            </button>
          </div>
        </div>
        <rc-form-hatalari [hatalar]="d.teslimGonderimi.genelHatalar()" />
      } @else {
        <dl class="kf-bilgiler">
          <div>
            <dt>{{ 'kiraFormu.alan.cikisKm' | transloco }}</dt>
            <dd>{{ d.kira()?.cikisKm ?? ('kiraFormu.not.teslimdeGirilir' | transloco) }}</dd>
          </div>
          <div>
            <dt>{{ 'kiraFormu.alan.cikisYakit' | transloco }}</dt>
            <dd>{{ d.kira()?.cikisYakit ?? ('kiraFormu.not.teslimdeGirilir' | transloco) }}</dd>
          </div>
        </dl>
      }
      @if (!d.yeni) {
        <div class="rc-form-izgara" [formGroup]="d.form">
          <rc-alan [etiket]="'kiraFormu.alan.teslimEden' | transloco">
            <rc-arama-secim formControlName="teslimEdenPersonel" [kaynak]="d.personelKaynagi" />
          </rc-alan>
        </div>
      }
    </section>
  `,
})
export class KiraBilgisi {
  protected readonly d = inject(KiraFormuDurumu);
}
