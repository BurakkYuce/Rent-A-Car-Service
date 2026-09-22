import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  numberAttribute,
  signal,
} from '@angular/core';
import { getCurrencySymbol } from '@angular/common';
import {
  invariantOndalik,
  ondalikBicimle,
  ondalikCoz,
  ondalikDuzenlemeMetni,
} from '@core/form/ondalik';
import { YEREL } from '@core/yerel/tr-yerel';
import { AyristiranKontrol, kontrolSaglayicilari } from './temel-kontrol';

/**
 * Para girdisi. Görüntü Türkçe (`1.234,56`), DEĞER invariant ondalık METİN (`"1234.56"`) — kayan
 * nokta yok, sunucuya birebir `decimal` gider. Sunucudan gelen JSON sayısı (`1234.5`) da yazılabilir;
 * kullanıcı değiştirince kanonik metin bildirilir. Yarım kuruş sıfırdan uzağa yuvarlanır
 * (`0,005` → `0.01`); anlaşılmayan yazım `paraGecersiz` hatası verir, metin ekranda kalır.
 */
@Component({
  selector: 'rc-para-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: kontrolSaglayicilari(() => ParaGirdisi, { dogrulayici: true }),
  template: `
    <div class="rc-girdi-kutusu">
      <input
        class="rc-girdi rc-girdi--sayi"
        inputmode="decimal"
        autocomplete="off"
        [id]="ogeKimligi()"
        [value]="metin()"
        [disabled]="pasif()"
        [attr.placeholder]="yerTutucu() || null"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaGecersiz()"
        [attr.aria-describedby]="ariaAciklayan()"
        [attr.aria-required]="ariaZorunlu()"
        (focus)="odaklandi($event)"
        (input)="yazildi($event)"
        (blur)="birakildi()"
      />
      <span class="rc-girdi-eki" aria-hidden="true">{{ simge() }}</span>
    </div>
  `,
})
export class ParaGirdisi extends AyristiranKontrol<string | number> {
  /** ISO para birimi kodu (`TRY`, `USD`, `EUR`); yalnız gösterim — değer tutardır. */
  readonly paraBirimi = input('TRY');
  readonly kesir = input(2, { transform: numberAttribute });
  readonly negatif = input(false, { transform: booleanAttribute });
  readonly yerTutucu = input('0,00');

  protected readonly simge = computed(() => getCurrencySymbol(this.paraBirimi(), 'narrow', YEREL));
  protected readonly metin = signal('');

  protected override disaridanYazildi(deger: string | number | null): void {
    const kanonik = invariantOndalik(deger, { kesir: this.kesir() });
    this.metin.set(ondalikBicimle(kanonik, this.kesir()));
    this.hataAyarla(null);
  }

  /**
   * Odakta gruplamasız düzenleme yazımına geçer. Metin DOM'a EŞZAMANLI yazılır ve odaktan önce metnin
   * TAMAMI seçiliyse (klavyeyle Tab'la gelme, otomatik doldurma, e2e `fill`: önce seç sonra odakla) seçim
   * yeniden kurulur. Aksi halde değer değişince imleç sona kayar ve yazılan mevcut tutarın SONUNA eklenirdi:
   * "2.600,00" dolu alana "500" → "2600,00500" → 2.600,01 sessizce geçerli (F4.4 e2e'de ölçüldü). Aynı
   * değerin değişiklik algılamasında yeniden yazılması seçimi bozmaz.
   */
  protected odaklandi(olay?: Event): void {
    if (this.ayristirmaHatasi() !== null) return;
    const kanonik = invariantOndalik(this.deger(), { kesir: this.kesir() });
    const duzenleme = ondalikDuzenlemeMetni(kanonik, this.kesir());
    const girdi = olay?.target;
    if (girdi instanceof HTMLInputElement && girdi.value !== duzenleme) {
      const tamamiSecili =
        girdi.value.length > 0 &&
        girdi.selectionStart === 0 &&
        girdi.selectionEnd === girdi.value.length;
      girdi.value = duzenleme;
      if (tamamiSecili) girdi.select();
    }
    this.metin.set(duzenleme);
  }

  protected yazildi(olay: Event): void {
    const yazilan = (olay.target as HTMLInputElement).value;
    this.metin.set(yazilan);
    const cozum = ondalikCoz(yazilan, { kesir: this.kesir(), negatif: this.negatif() });
    this.hataAyarla(cozum.gecerli ? null : { paraGecersiz: true });
    this.bildir(cozum.gecerli ? cozum.deger : null);
  }

  protected birakildi(): void {
    if (this.ayristirmaHatasi() === null) {
      const kanonik = invariantOndalik(this.deger(), { kesir: this.kesir() });
      this.metin.set(ondalikBicimle(kanonik, this.kesir()));
    }
    this.dokun();
  }
}
