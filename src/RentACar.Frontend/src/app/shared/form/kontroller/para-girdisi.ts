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
/** Basıştan sonra gelen odağın işaretçi (fare/dokunuş) odağı sayıldığı süre. */
const ISARETCI_ODAK_PENCERESI_MS = 1000;

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
        (pointercancel)="isaretciIptal()"
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
    this.kullaniciYazdi = false; // programatik değer (ön-doldurma, sıfırlama): dokunulmamış sayılır
  }

  /**
   * Kullanıcı bu değere yazdı mı. Yazmadıysa değer ön-doldurma/programatiktir: fareyle odakta da TAMAMI seçilir
   * (3. tur M-B: sağa yaslı ön-dolu kutunun soluna tıklayıp "500" yazan kullanıcı "5002600,00" gönderiyordu).
   */
  private kullaniciYazdi = false;
  /**
   * Son işaretçi basışının zamanı (ms). Odak bu pencere içinde gelirse fare/dokunuş odağıdır (adversarial L1).
   * Zaman penceresi: dokunuşta odak `pointerup`'tan SONRA gelir; basış odak üretmezse (kaydırma — Q1) bayrak
   * kendiliğinden düşer, `pointercancel`/`blur` da sıfırlar.
   */
  private isaretciZamani: number | null = null;

  protected isaretciyleBasildi(olay: PointerEvent): void {
    const girdi = olay.target;
    if (girdi instanceof HTMLInputElement && girdi.ownerDocument.activeElement !== girdi) {
      this.isaretciZamani = performance.now();
    }
  }

  protected isaretciIptal(): void {
    this.isaretciZamani = null;
  }

  /**
   * Odakta gruplamasız düzenleme yazımına geçer ve metnin TAMAMINI seçer — TÜM para girdilerinde varsayılan
   * (F4.4 + F4.2 F3/F5 + P259-8b birleşimi; eski `odaktaSec` seçeneği kalktı). Klavyeyle (Tab) ya da
   * programatik odakla gelen kullanıcının yazdığı rakam mevcut/önerilen tutarın SONUNA eklenmez, yerine geçer.
   * Değer DOM'a EŞZAMANLI yazılır: yalnız sinyale yazılsaydı sonraki çizimde değer değişir, seçim çöker ve
   * yazılan sona eklenirdi ("2.600,00" + "500" → "2600,00500"; F4.4 e2e'de ölçüldü). Aynı değerin sonradan
   * yeniden yazılması seçimi korur. Sona eklenmiş fazla hane yine de sessizce yuvarlanmaz (`paraFazlaHane`).
   * **Fare/dokunuş odağı** (adversarial L1 + 3. tur M-B): değer kullanıcının YAZDIĞI bir tutarsa seçilmez — imleci
   * bilerek bir rakamın yanına koyan kullanıcı oraya yazabilmeli (tarayıcı imleci tıklanan noktaya koyar). Değer
   * DOKUNULMAMIŞ ön-doldurma/programatikse fare odağında da tamamı seçilir. Alan zaten odaklıyken ikinci tık
   * odak üretmez; imleç tıklanan yerde kalır.
   */
  protected odaklandi(olay: Event): void {
    const zaman = this.isaretciZamani;
    const isaretci = zaman !== null && performance.now() - zaman < ISARETCI_ODAK_PENCERESI_MS;
    this.isaretciZamani = null;
    if (this.ayristirmaHatasi() !== null) return;
    const kanonik = invariantOndalik(this.deger(), { kesir: this.kesir() });
    const duzenleme = ondalikDuzenlemeMetni(kanonik, this.kesir());
    this.metin.set(duzenleme);
    const girdi = olay.target;
    if (girdi instanceof HTMLInputElement && duzenleme !== '') {
      if (girdi.value !== duzenleme) girdi.value = duzenleme;
      if (!isaretci || !this.kullaniciYazdi) {
        girdi.select();
        // Fare odağında basış bırakılınca tarayıcı seçimi imlece çevirir; bir kezlik bırakma olayı engellenir.
        if (isaretci) {
          girdi.addEventListener('mouseup', (e) => e.preventDefault(), { once: true });
        }
      }
    }
  }

  protected yazildi(olay: Event): void {
    this.kullaniciYazdi = true;
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
    this.isaretciZamani = null;
    if (this.ayristirmaHatasi() === null) {
      const kanonik = invariantOndalik(this.deger(), { kesir: this.kesir() });
      this.metin.set(ondalikBicimle(kanonik, this.kesir()));
    }
    this.dokun();
  }
}
