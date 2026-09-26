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
import { invariantDecimal, formatDecimal, parseDecimal, decimalEditText } from '@core/form/ondalik';
import { LOCALE } from '@core/yerel/tr-yerel';
import { ParsingControl, controlProviders } from './base-control';

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
const POINTER_FOCUS_WINDOW_MS = 1000;

@Component({
  selector: 'rc-para-girdisi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: controlProviders(() => MoneyInput, { dogrulayici: true }),
  template: `
    <div class="rc-girdi-kutusu">
      <input
        class="rc-girdi rc-girdi--sayi"
        inputmode="decimal"
        autocomplete="off"
        [id]="itemId()"
        [value]="metin()"
        [disabled]="pasif()"
        [attr.placeholder]="yerTutucu() || null"
        [attr.aria-label]="ariaEtiketi() ?? null"
        [attr.aria-invalid]="ariaInvalid()"
        [attr.aria-describedby]="ariaDescribedBy()"
        [attr.aria-required]="ariaRequired()"
        (pointerdown)="pressedWithPointer($event)"
        (pointercancel)="pointerCancel()"
        (focus)="focused($event)"
        (input)="written($event)"
        (blur)="released()"
      />
      <span class="rc-girdi-eki" aria-hidden="true">{{ iconSymbol() }}</span>
    </div>
  `,
})
export class MoneyInput extends ParsingControl<string | number> {
  /** ISO para birimi kodu (`TRY`, `USD`, `EUR`); yalnız gösterim — değer tutardır. */
  readonly paraBirimi = input('TRY');
  readonly kesir = input(2, { transform: numberAttribute });
  readonly negatif = input(false, { transform: booleanAttribute });
  readonly yerTutucu = input('0,00');

  protected readonly iconSymbol = computed(() =>
    getCurrencySymbol(this.paraBirimi(), 'narrow', LOCALE),
  );
  protected readonly metin = signal('');

  protected override writtenExternally(value: string | number | null): void {
    const canonical = invariantDecimal(value, { kesir: this.kesir() });
    this.metin.set(formatDecimal(canonical, this.kesir()));
    this.setError(null);
    this.userTyped = false; // programatik değer (ön-doldurma, sıfırlama): dokunulmamış sayılır
  }

  /**
   * Kullanıcı bu değere yazdı mı. Yazmadıysa değer ön-doldurma/programatiktir: fareyle odakta da TAMAMI seçilir
   * (3. tur M-B: sağa yaslı ön-dolu kutunun soluna tıklayıp "500" yazan kullanıcı "5002600,00" gönderiyordu).
   */
  private userTyped = false;
  /**
   * Son işaretçi basışının zamanı (ms). Odak bu pencere içinde gelirse fare/dokunuş odağıdır (adversarial L1).
   * Zaman penceresi: dokunuşta odak `pointerup`'tan SONRA gelir; basış odak üretmezse (kaydırma — Q1) bayrak
   * kendiliğinden düşer, `pointercancel`/`blur` da sıfırlar.
   */
  private pointerTime: number | null = null;

  protected pressedWithPointer(evt: PointerEvent): void {
    const girdi = evt.target;
    if (girdi instanceof HTMLInputElement && girdi.ownerDocument.activeElement !== girdi) {
      this.pointerTime = performance.now();
    }
  }

  protected pointerCancel(): void {
    this.pointerTime = null;
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
  protected focused(evt: Event): void {
    const zaman = this.pointerTime;
    const pointer = zaman !== null && performance.now() - zaman < POINTER_FOCUS_WINDOW_MS;
    this.pointerTime = null;
    if (this.parseError() !== null) return;
    const canonical = invariantDecimal(this.deger(), { kesir: this.kesir() });
    const editing = decimalEditText(canonical, this.kesir());
    this.metin.set(editing);
    const girdi = evt.target;
    if (girdi instanceof HTMLInputElement && editing !== '') {
      if (girdi.value !== editing) girdi.value = editing;
      if (!pointer || !this.userTyped) {
        girdi.select();
        // Fare odağında basış bırakılınca tarayıcı seçimi imlece çevirir; bir kezlik bırakma olayı engellenir.
        if (pointer) {
          girdi.addEventListener('mouseup', (e) => e.preventDefault(), { once: true });
        }
      }
    }
  }

  protected written(evt: Event): void {
    this.userTyped = true;
    const written = (evt.target as HTMLInputElement).value;
    this.metin.set(written);
    const option = { kesir: this.kesir(), negatif: this.negatif() };
    const resolution = parseDecimal(written, { ...option, fazlaHane: 'reddet' });
    if (resolution.gecerli) {
      this.setError(null);
      this.notify(resolution.deger);
      return;
    }
    // Yalnız fazla hane yüzünden mi geçersiz? (Yuvarlayarak anlaşılıyorsa evet — ama YUVARLANMAZ.)
    const excessDigits = parseDecimal(written, { ...option, fazlaHane: 'yuvarla' }).gecerli;
    this.setError(
      excessDigits ? { paraFazlaHane: { hane: this.kesir() } } : { paraGecersiz: true },
    );
    this.notify(null);
  }

  protected released(): void {
    this.pointerTime = null;
    if (this.parseError() === null) {
      const canonical = invariantDecimal(this.deger(), { kesir: this.kesir() });
      this.metin.set(formatDecimal(canonical, this.kesir()));
    }
    this.touch();
  }
}
