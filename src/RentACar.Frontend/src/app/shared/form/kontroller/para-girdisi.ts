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
 * kullanıcı değiştirince kanonik metin bildirilir. Anlaşılmayan yazım `paraGecersiz` hatası verir, metin
 * ekranda kalır. Odakta metnin tamamı seçilir (bkz. `odaklandi`).
 *
 * **Fazla ondalık (F4.2 adversarial F3/F5):** KULLANICININ YAZDIĞI değerde `kesir`'den fazla anlamlı hane
 * (`1,555`, önceden dolu `1250,50`'nin sonuna yazılan `90` → `1250,5090`) YUVARLANMAZ, `paraFazlaHane` alan
 * hatası olur (sessiz yuvarlama niyet dışı tutar gönderirdi). Sondaki sıfırlar (`1,500`) sorun değildir.
 * PROGRAMATİK değer (sunucudan `1250.5000`, `writeValue`) eskisi gibi yarım kuruş sıfırdan uzağa yuvarlanır.
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
        (pointerdown)="isaretciyleBasildi($event)"
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

  /** Odak fare/dokunuşla mı geliyor (adversarial L1): o zaman imleç kullanıcının tıkladığı yerde kalır. */
  private isaretciOdagi = false;

  protected isaretciyleBasildi(olay: PointerEvent): void {
    const girdi = olay.target;
    if (girdi instanceof HTMLInputElement && girdi.ownerDocument.activeElement !== girdi) {
      this.isaretciOdagi = true;
    }
  }

  /**
   * Odakta gruplamasız düzenleme yazımına geçer ve metnin TAMAMINI seçer — TÜM para girdilerinde varsayılan
   * (F4.4 + F4.2 F3/F5 + P259-8b birleşimi; eski `odaktaSec` seçeneği kalktı). Klavyeyle (Tab) ya da
   * programatik odakla gelen kullanıcının yazdığı rakam mevcut/önerilen tutarın SONUNA eklenmez, yerine geçer.
   * Değer DOM'a EŞZAMANLI yazılır: yalnız sinyale yazılsaydı sonraki çizimde değer değişir, seçim çöker ve
   * yazılan sona eklenirdi ("2.600,00" + "500" → "2600,00500"; F4.4 e2e'de ölçüldü). Aynı değerin sonradan
   * yeniden yazılması seçimi korur. Sona eklenmiş fazla hane yine de sessizce yuvarlanmaz (`paraFazlaHane`).
   * **Fare/dokunuşla odakta seçilmez** (adversarial L1): imleci bilerek bir rakamın yanına koyan kullanıcı oraya
   * yazabilmeli; düzenleme yazımı yine eşzamanlı yazılır, tarayıcı imleci tıklanan noktaya yerleştirir.
   */
  protected odaklandi(olay: Event): void {
    const isaretci = this.isaretciOdagi;
    this.isaretciOdagi = false;
    if (this.ayristirmaHatasi() !== null) return;
    const kanonik = invariantOndalik(this.deger(), { kesir: this.kesir() });
    const duzenleme = ondalikDuzenlemeMetni(kanonik, this.kesir());
    this.metin.set(duzenleme);
    const girdi = olay.target;
    if (girdi instanceof HTMLInputElement && duzenleme !== '') {
      if (girdi.value !== duzenleme) girdi.value = duzenleme;
      if (!isaretci) girdi.select();
    }
  }

  protected yazildi(olay: Event): void {
    const yazilan = (olay.target as HTMLInputElement).value;
    this.metin.set(yazilan);
    const secenek = { kesir: this.kesir(), negatif: this.negatif() };
    const cozum = ondalikCoz(yazilan, { ...secenek, fazlaHane: 'reddet' });
    if (cozum.gecerli) {
      this.hataAyarla(null);
      this.bildir(cozum.deger);
      return;
    }
    // Yalnız fazla hane yüzünden mi geçersiz? (Yuvarlayarak anlaşılıyorsa evet — ama YUVARLANMAZ.)
    const fazlaHane = ondalikCoz(yazilan, { ...secenek, fazlaHane: 'yuvarla' }).gecerli;
    this.hataAyarla(fazlaHane ? { paraFazlaHane: { hane: this.kesir() } } : { paraGecersiz: true });
    this.bildir(null);
  }

  protected birakildi(): void {
    this.isaretciOdagi = false;
    if (this.ayristirmaHatasi() === null) {
      const kanonik = invariantOndalik(this.deger(), { kesir: this.kesir() });
      this.metin.set(ondalikBicimle(kanonik, this.kesir()));
    }
    this.dokun();
  }
}
