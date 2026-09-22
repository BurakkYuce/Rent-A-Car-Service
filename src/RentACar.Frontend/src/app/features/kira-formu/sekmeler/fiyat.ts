import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { HesapOzeti } from './hesap-ozeti';
import { KF_ORTAK } from './ortak';

/**
 * FİYAT/TOPLAM — kanonik fiyat türü / günlük ücret / döviz (yalnız yeni kira; kayıtlı sözleşmede fiyat
 * sabit — değişiklik fark faturasıyla). Tutarlar sunucu motorundan (`hesapla`); ödeme/komisyon alanları
 * BİLGİdir (deftere/bakiyeye yansımaz — drop ücreti hariç: sistem ücret satırı).
 */
@Component({
  selector: 'rc-kf-fiyat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, HesapOzeti],
  template: `
    <div class="kf-iki-sutun" [formGroup]="d.form">
      <div class="kf-sutun">
        <section class="kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.fiyatGirisi' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.fiyatTuru' | transloco">
              <rc-secim formControlName="fiyatTuru" [secenekler]="d.fiyatTurleri()" bosEtiket="—" />
            </rc-alan>
            <rc-alan
              [etiket]="'kiraFormu.alan.gunlukUcret' | transloco"
              [ipucu]="d.yeni ? ('kiraFormu.ipucu.toplamModu' | transloco) : ''"
            >
              <rc-para-girdisi
                formControlName="gunlukUcret"
                [paraBirimi]="d.kiraDovizi()"
                [yerTutucu]="'kiraFormu.alan.otomatikBos' | transloco"
              />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.kampanyaKodu' | transloco">
              <rc-metin-girdisi formControlName="kampanyaKodu" [azamiUzunluk]="64" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.doviz' | transloco">
              <rc-secim formControlName="doviz" [secenekler]="d.dovizler()" bosEtiket="—" />
            </rc-alan>
            <rc-alan
              [etiket]="'kiraFormu.alan.ozelKdv' | transloco"
              [ipucu]="'kiraFormu.ipucu.ozelKdv' | transloco"
            >
              <rc-sayi-girdisi formControlName="ozelKdvOran" [kesir]="4" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.damga' | transloco">
              <rc-para-girdisi formControlName="damgaVergisi" [paraBirimi]="d.kiraDovizi()" />
            </rc-alan>
          </div>
        </section>

        <section class="kf-kart">
          <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.odeme' | transloco }}</h3>
          <div class="rc-form-izgara">
            <rc-alan [etiket]="'kiraFormu.alan.odemeSekli' | transloco">
              <rc-metin-girdisi
                formControlName="odemeSekli"
                liste="kf-dl-odeme"
                [azamiUzunluk]="64"
              />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.provizyon' | transloco">
              <rc-para-girdisi formControlName="provizyon" [paraBirimi]="d.kiraDovizi()" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.depozito' | transloco">
              <rc-para-girdisi formControlName="depozito" [paraBirimi]="d.kiraDovizi()" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.komisyonOran' | transloco">
              <rc-sayi-girdisi formControlName="komisyonOran" [kesir]="2" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.komisyonTutar' | transloco">
              <rc-para-girdisi formControlName="komisyonTutar" [paraBirimi]="d.kiraDovizi()" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.dropUcreti' | transloco">
              <rc-para-girdisi formControlName="dropUcreti" [paraBirimi]="d.kiraDovizi()" />
            </rc-alan>
            <rc-alan [etiket]="'kiraFormu.alan.sonraOde' | transloco">
              <rc-sayi-girdisi formControlName="sonraOdeOran" [kesir]="2" />
            </rc-alan>
          </div>
          <p class="kf-not">{{ 'kiraFormu.not.odemeBilgi' | transloco }}</p>
        </section>
      </div>
      <div class="kf-sutun">
        <rc-kf-hesap-ozeti />
      </div>
    </div>
  `,
})
export class Fiyat {
  protected readonly d = inject(KiraFormuDurumu);
}
