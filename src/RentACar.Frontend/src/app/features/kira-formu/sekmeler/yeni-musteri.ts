import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { bugun } from '@core/form/tarih-girdisi';
import { Alan } from '@shared/form/alan/alan';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';
import { KiraFormuDurumu } from '../kira-formu-durumu';

/**
 * Hızlı müşteri (yalnız yeni kira): cari ANINDA açılır ve müşteri alanına seçili gelir — sayfa
 * yenilenmez, girilen kira alanları kaybolmaz. TC/ehliyet sunucuda şifrelenir; yanıt yalnız kimlik +
 * etiket. Kaydedilmeden kira kaydedilirse önce bu cari açılır (Blazor tek adım davranışı).
 */
@Component({
  selector: 'rc-kf-yeni-musteri',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormHatalari, MetinGirdisi, TarihSecici],
  template: `
    <details
      class="kf-acilir"
      id="kf-yeni-musteri"
      [open]="d.yeniMusteriAcik()"
      (toggle)="acildi($event)"
    >
      <summary>{{ 'kiraFormu.yeniMusteri.baslik' | transloco }}</summary>
      <div class="rc-form-izgara" [formGroup]="d.yeniMusteriFormu">
        <rc-alan [etiket]="'kiraFormu.alan.ad' | transloco">
          <rc-metin-girdisi formControlName="ad" [azamiUzunluk]="64" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.alan.soyad' | transloco">
          <rc-metin-girdisi formControlName="soyad" [azamiUzunluk]="64" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.unvan' | transloco">
          <rc-metin-girdisi formControlName="unvan" [azamiUzunluk]="128" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.tc' | transloco">
          <rc-metin-girdisi formControlName="tcKimlik" [azamiUzunluk]="11" girdiModu="numeric" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.alan.telefon' | transloco">
          <rc-metin-girdisi formControlName="cepTel" tur="tel" [azamiUzunluk]="32" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.eposta' | transloco">
          <rc-metin-girdisi formControlName="email" tur="email" [azamiUzunluk]="128" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.dogum' | transloco">
          <rc-tarih-secici formControlName="dogumTarihi" [enCok]="bugun" [hazirlar]="false" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.ehliyetNo' | transloco">
          <rc-metin-girdisi formControlName="ehliyetNo" [azamiUzunluk]="32" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.alan.ehliyetSinifi' | transloco">
          <rc-metin-girdisi formControlName="ehliyetSinifi" [azamiUzunluk]="8" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.ehliyetTarihi' | transloco">
          <rc-tarih-secici formControlName="ehliyetTarihi" [enCok]="bugun" [hazirlar]="false" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.ehliyetYeri' | transloco">
          <rc-metin-girdisi formControlName="ehliyetYeri" [azamiUzunluk]="64" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.il' | transloco">
          <rc-metin-girdisi formControlName="il" [azamiUzunluk]="64" />
        </rc-alan>
        <rc-alan [etiket]="'kiraFormu.yeniMusteri.ilce' | transloco">
          <rc-metin-girdisi formControlName="ilce" [azamiUzunluk]="64" />
        </rc-alan>
      </div>
      <rc-form-hatalari [hatalar]="d.yeniMusteriGonderimi.genelHatalar()" />
      <div class="kf-eylemler">
        <button
          type="button"
          class="rc-dugme"
          [disabled]="d.yeniMusteriGonderimi.gonderiliyor() || !d.operasyon()"
          (click)="d.yeniMusteriKaydet()"
        >
          {{ 'kiraFormu.yeniMusteri.kaydet' | transloco }}
        </button>
      </div>
      <p class="kf-not">{{ 'kiraFormu.yeniMusteri.not' | transloco }}</p>
    </details>
  `,
})
export class YeniMusteri {
  protected readonly d = inject(KiraFormuDurumu);
  protected readonly bugun = bugun();

  protected acildi(olay: Event): void {
    this.d.yeniMusteriAcik.set((olay.target as HTMLDetailsElement).open);
  }
}
