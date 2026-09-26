import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FormControl, type FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { Alan } from '@shared/form/alan/alan';
import { TextInput } from '@shared/form/kontroller/text-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import { DatePicker } from '@shared/form/tarih/date-picker';

import { INVOICE_TYPE_SUGGESTIONS, PRICE_TYPE_SUGGESTIONS } from './filo-modeli';

/** Künye kontrolleri (yeni sözleşme ve künye düzenleme formlarının ortak alanları). */
export interface KunyeKontrolleri {
  sozlesmeNo: FormControl<string | null>;
  makbuzNo: FormControl<string | null>;
  dosyaNo: FormControl<string | null>;
  sozlesmeTarihi: FormControl<string | null>;
  imzaTarih: FormControl<string | null>;
  satisTemsilcisi: FormControl<string | null>;
  faturaTuru: FormControl<string | null>;
  fiyatTuru: FormControl<string | null>;
  kaynak: FormControl<string | null>;
  vadeGun: FormControl<number | null>;
  toplamKmLimiti: FormControl<number | null>;
  cikisKm: FormControl<number | null>;
  toplamKm: FormControl<number | null>;
  aciklama: FormControl<string | null>;
}

/**
 * Filo sözleşmesi KÜNYE alanları (Blazor yeni sözleşme + "Künye" formlarının ortak alanları; para/süre
 * yok). Alan adları API ile birebir (sunucu alan hataları doğrudan eşlenir). Kontroller ana formun
 * `FormGroup`'undadır; bu bileşen yalnız çizer.
 */
@Component({
  selector: 'rc-filo-kunye-alanlari',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, TextInput, NumberInput, DatePicker],
  styles: `
    :host {
      display: contents;
    }
  `,
  template: `
    <ng-container [formGroup]="grup()">
      <rc-alan
        [etiket]="'filoKiralama.alan.sozlesmeNo' | transloco"
        [ipucu]="'filoKiralama.alan.sozlesmeNoIpucu' | transloco"
      >
        <rc-metin-girdisi formControlName="sozlesmeNo" [azamiUzunluk]="32" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.makbuzNo' | transloco">
        <rc-metin-girdisi formControlName="makbuzNo" [azamiUzunluk]="32" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.dosyaNo' | transloco">
        <rc-metin-girdisi formControlName="dosyaNo" [azamiUzunluk]="32" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.sozlesmeTarihi' | transloco">
        <rc-tarih-secici formControlName="sozlesmeTarihi" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.imzaTarih' | transloco">
        <rc-tarih-secici formControlName="imzaTarih" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.satisTemsilcisi' | transloco">
        <rc-metin-girdisi formControlName="satisTemsilcisi" [azamiUzunluk]="128" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.faturaTuru' | transloco">
        <rc-metin-girdisi
          formControlName="faturaTuru"
          liste="dl-filo-fatura-turu"
          [azamiUzunluk]="32"
        />
      </rc-alan>
      <rc-alan
        [etiket]="'filoKiralama.alan.fiyatTuru' | transloco"
        [ipucu]="'filoKiralama.alan.fiyatTuruIpucu' | transloco"
      >
        <rc-metin-girdisi
          formControlName="fiyatTuru"
          liste="dl-filo-fiyat-turu"
          [azamiUzunluk]="32"
        />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.kaynak' | transloco">
        <rc-metin-girdisi formControlName="kaynak" [azamiUzunluk]="64" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.vadeGun' | transloco">
        <rc-sayi-girdisi formControlName="vadeGun" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.toplamKmLimiti' | transloco">
        <rc-sayi-girdisi formControlName="toplamKmLimiti" />
      </rc-alan>
      <rc-alan [etiket]="'filoKiralama.alan.cikisKm' | transloco">
        <rc-sayi-girdisi formControlName="cikisKm" />
      </rc-alan>
      <rc-alan
        [etiket]="'filoKiralama.alan.toplamKm' | transloco"
        [ipucu]="'filoKiralama.alan.toplamKmIpucu' | transloco"
      >
        <rc-sayi-girdisi formControlName="toplamKm" />
      </rc-alan>
      <rc-alan class="rc-form-izgara__genis" [etiket]="'filoKiralama.alan.aciklama' | transloco">
        <rc-metin-girdisi formControlName="aciklama" [azamiUzunluk]="512" />
      </rc-alan>
    </ng-container>
    <datalist id="dl-filo-fatura-turu">
      @for (o of invoiceTypes; track o) {
        <option [value]="o"></option>
      }
    </datalist>
    <datalist id="dl-filo-fiyat-turu">
      @for (o of priceTypes; track o) {
        <option [value]="o"></option>
      }
    </datalist>
  `,
})
export class FleetIdentityFields {
  readonly grup = input.required<FormGroup>();
  protected readonly invoiceTypes = INVOICE_TYPE_SUGGESTIONS;
  protected readonly priceTypes = PRICE_TYPE_SUGGESTIONS;
}

/** Künye kontrollerini kurar (doğrulayıcılar sunucu sınırlarıyla aynı: negatif yok, varchar uzunlukları). */
export function profileControls(): KunyeKontrolleri {
  const text = (n: number) => new FormControl<string | null>(null, Validators.maxLength(n));
  const date = () => new FormControl<string | null>(null);
  const count = () => new FormControl<number | null>(null, Validators.min(0));
  return {
    sozlesmeNo: text(32),
    makbuzNo: text(32),
    dosyaNo: text(32),
    sozlesmeTarihi: date(),
    imzaTarih: date(),
    satisTemsilcisi: text(128),
    faturaTuru: text(32),
    fiyatTuru: text(32),
    kaynak: text(64),
    vadeGun: count(),
    toplamKmLimiti: count(),
    cikisKm: count(),
    toplamKm: count(),
    aciklama: text(512),
  };
}
