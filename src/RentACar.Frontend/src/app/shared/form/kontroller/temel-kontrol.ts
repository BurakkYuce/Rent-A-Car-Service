import {
  Directive,
  Provider,
  Type,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import {
  AbstractControl,
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator,
} from '@angular/forms';
import { ALAN_BAGLAMI, tekilKimlik } from '../alan/alan-baglami';

/**
 * CVA kontrollerinin ortak tabanı: değer ve pasiflik signal'de (zoneless + OnPush), erişilebilirlik
 * öznitelikleri `rc-alan` bağlamından. Alt sınıf `bildir(deger)` ile formu, `dokun()` ile "touched"ı
 * günceller. `writeValue` asla `onChange` çağırmaz (form kirlenmez).
 */
@Directive()
export abstract class TemelKontrol<T> implements ControlValueAccessor {
  protected readonly alan = inject(ALAN_BAGLAMI, { optional: true });

  /** Sarmalayıcısız kullanımda öğe kimliği; `rc-alan` içindeyse onun kimliği kullanılır. */
  readonly kimlik = input<string | undefined>(undefined);
  /** Sarmalayıcısız kullanımda erişilebilir ad. */
  readonly ariaEtiketi = input<string | undefined>(undefined);

  private readonly yedekKimlik = tekilKimlik('rc-k');
  protected readonly ogeKimligi = computed(
    () => this.kimlik() ?? this.alan?.kimlik ?? this.yedekKimlik,
  );
  protected readonly ariaGecersiz = computed(() => (this.alan?.gecersiz() ? 'true' : null));
  protected readonly ariaAciklayan = computed(() => this.alan?.aciklayanlar() ?? null);
  protected readonly ariaZorunlu = computed(() => (this.alan?.zorunlu() ? 'true' : null));

  protected readonly deger = signal<T | null>(null);
  protected readonly pasif = signal(false);

  private degisti: (deger: T | null) => void = () => undefined;
  private dokunuldu: () => void = () => undefined;

  writeValue(deger: T | null | undefined): void {
    this.deger.set(deger ?? null);
    this.disaridanYazildi(deger ?? null);
  }

  registerOnChange(fn: (deger: T | null) => void): void {
    this.degisti = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.dokunuldu = fn;
  }

  setDisabledState(pasif: boolean): void {
    this.pasif.set(pasif);
  }

  /** Alt sınıf görüntü metnini (ör. biçimli tutar) güncellemek için. */
  protected disaridanYazildi(deger: T | null): void {
    void deger;
  }

  protected bildir(deger: T | null): void {
    this.deger.set(deger);
    this.degisti(deger);
  }

  protected dokun(): void {
    this.dokunuldu();
  }
}

/**
 * Yazılan metnin ayrıştırılamadığı durumu (ör. `12,3a`, `31.02.2026`) forma hata olarak bildiren
 * kontroller için taban. Değer `null`'a düşer ama metin ekranda KALIR; hata anahtarı `rc-alan`
 * mesajına dönüşür ("zorunlu" yerine "geçerli bir tutar girin").
 */
@Directive()
export abstract class AyristiranKontrol<T> extends TemelKontrol<T> implements Validator {
  protected readonly ayristirmaHatasi = signal<ValidationErrors | null>(null);
  private dogrulamaDegisti: () => void = () => undefined;

  validate(kontrol: AbstractControl): ValidationErrors | null {
    void kontrol;
    return this.ayristirmaHatasi();
  }

  registerOnValidatorChange(fn: () => void): void {
    this.dogrulamaDegisti = fn;
  }

  protected hataAyarla(hata: ValidationErrors | null): void {
    const onceki = this.ayristirmaHatasi();
    this.ayristirmaHatasi.set(hata);
    if (JSON.stringify(onceki) !== JSON.stringify(hata)) this.dogrulamaDegisti();
  }
}

/** `providers: kontrolSaglayicilari(() => X)` — CVA (ve istenirse doğrulayıcı) kaydı. */
export function kontrolSaglayicilari(
  tip: () => Type<unknown>,
  { dogrulayici = false }: { dogrulayici?: boolean } = {},
): Provider[] {
  const saglayicilar: Provider[] = [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(tip), multi: true },
  ];
  if (dogrulayici) {
    saglayicilar.push({ provide: NG_VALIDATORS, useExisting: forwardRef(tip), multi: true });
  }
  return saglayicilar;
}
