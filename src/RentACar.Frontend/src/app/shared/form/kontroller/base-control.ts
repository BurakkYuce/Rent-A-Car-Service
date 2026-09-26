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
import { FIELD_CONTEXT, uniqueId } from '../alan/alan-baglami';

/**
 * CVA kontrollerinin ortak tabanı: değer ve pasiflik signal'de (zoneless + OnPush), erişilebilirlik
 * öznitelikleri `rc-alan` bağlamından. Alt sınıf `bildir(deger)` ile formu, `dokun()` ile "touched"ı
 * günceller. `writeValue` asla `onChange` çağırmaz (form kirlenmez).
 */
@Directive()
export abstract class BaseControl<T> implements ControlValueAccessor {
  protected readonly alan = inject(FIELD_CONTEXT, { optional: true });

  /** Sarmalayıcısız kullanımda öğe kimliği; `rc-alan` içindeyse onun kimliği kullanılır. */
  readonly kimlik = input<string | undefined>(undefined);
  /** Sarmalayıcısız kullanımda erişilebilir ad. */
  readonly ariaEtiketi = input<string | undefined>(undefined);

  private readonly fallbackId = uniqueId('rc-k');
  protected readonly itemId = computed(() => this.kimlik() ?? this.alan?.kimlik ?? this.fallbackId);
  protected readonly ariaInvalid = computed(() => (this.alan?.gecersiz() ? 'true' : null));
  protected readonly ariaDescribedBy = computed(() => this.alan?.aciklayanlar() ?? null);
  protected readonly ariaRequired = computed(() => (this.alan?.zorunlu() ? 'true' : null));

  protected readonly deger = signal<T | null>(null);
  protected readonly pasif = signal(false);

  private changed: (value: T | null) => void = () => undefined;
  private touched: () => void = () => undefined;

  writeValue(value: T | null | undefined): void {
    this.deger.set(value ?? null);
    this.writtenExternally(value ?? null);
  }

  registerOnChange(fn: (value: T | null) => void): void {
    this.changed = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.touched = fn;
  }

  setDisabledState(inactive: boolean): void {
    this.pasif.set(inactive);
  }

  /** Alt sınıf görüntü metnini (ör. biçimli tutar) güncellemek için. */
  protected writtenExternally(value: T | null): void {
    void value;
  }

  protected notify(value: T | null): void {
    this.deger.set(value);
    this.changed(value);
  }

  protected touch(): void {
    this.touched();
  }
}

/**
 * Yazılan metnin ayrıştırılamadığı durumu (ör. `12,3a`, `31.02.2026`) forma hata olarak bildiren
 * kontroller için taban. Değer `null`'a düşer ama metin ekranda KALIR; hata anahtarı `rc-alan`
 * mesajına dönüşür ("zorunlu" yerine "geçerli bir tutar girin").
 */
@Directive()
export abstract class ParsingControl<T> extends BaseControl<T> implements Validator {
  protected readonly parseError = signal<ValidationErrors | null>(null);
  private validationChanged: () => void = () => undefined;

  validate(check: AbstractControl): ValidationErrors | null {
    void check;
    return this.parseError();
  }

  registerOnValidatorChange(fn: () => void): void {
    this.validationChanged = fn;
  }

  protected setError(error: ValidationErrors | null): void {
    const previous = this.parseError();
    this.parseError.set(error);
    if (JSON.stringify(previous) !== JSON.stringify(error)) this.validationChanged();
  }
}

/** `providers: kontrolSaglayicilari(() => X)` — CVA (ve istenirse doğrulayıcı) kaydı. */
export function controlProviders(
  tip: () => Type<unknown>,
  { dogrulayici: validator = false }: { dogrulayici?: boolean } = {},
): Provider[] {
  const providers: Provider[] = [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(tip), multi: true },
  ];
  if (validator) {
    providers.push({ provide: NG_VALIDATORS, useExisting: forwardRef(tip), multi: true });
  }
  return providers;
}
