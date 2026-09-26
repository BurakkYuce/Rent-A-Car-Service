import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  type OnInit,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  Subscription,
  catchError,
  debounceTime,
  distinctUntilChanged,
  map,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { tarihBicimle } from '@core/bicim/bicim';
import { invariantDecimal, formatDecimal } from '@core/form/ondalik';
import { toApiError } from '@core/api/api-hatasi';
import { SubmitLock } from '@core/form/submit-lock';
import { SERVER_ERROR } from '@core/form/sunucu-hatalari';
import { translationFunction } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { Icon } from '../../ikon/icon';
import { Alan } from '../alan/alan';
import { uniqueId } from '../alan/alan-baglami';
import { formSubmission } from '../form-submission';
import { FormErrors } from '../form-errors';
import { TextArea } from '../kontroller/text-area';
import { TextInput } from '../kontroller/text-input';
import { Checkbox } from '../kontroller/checkbox';
import { MoneyInput } from '../kontroller/money-input';
import { NumberInput } from '../kontroller/number-input';
import { Selection } from '../kontroller/selection';
import { DatePicker } from '../tarih/date-picker';
import type {
  DefinitionOptions,
  TanimAlani,
  DefinitionValue,
  DefinitionSource,
  DefinitionRow,
} from './definition-source';

type Edit = { readonly tur: 'yeni' } | { readonly tur: 'satir'; readonly id: string };

/** Öneri araması gecikmesi (kira formu `oneriAramasi` ile aynı). */
export const SUGGESTION_DELAY_MS = 250;

/** `textarea` alanının listedeki kısaltma uzunluğu (karakter). */
export const TEXTAREA_PREVIEW = 80;

/** Sunucu birleştirmesinde değer eşitliği (JSON; `null`/`undefined` aynı). */
const sameValue = (a: unknown, b: unknown) =>
  JSON.stringify(a ?? null) === JSON.stringify(b ?? null);

/**
 * Genel tanım CRUD'u (F11'in tanım ekranları): liste + satır içi oluştur/düzenle + onaylı sil.
 * Alanlar `TanimAlani[]` ile tanımlanır; form her düzenlemede o alanlardan kurulur. Liste
 * `TemelStore` ile (hata boş liste gibi görünmez), kayıt `formGonderimi` ile (kilit + anahtar +
 * sunucu alan hatası), silme ayrı `GonderimKilidi` ile.
 *
 * İyimser eşzamanlılık (F11.2a): satırın `surum`'u PUT'a gider (satırda yoksa düzenleme açılırken kayıt
 * `read` ile tekil okunur). 409 `cakisma`'da form SİLİNMEZ: kayıt yeniden okunur, kullanıcının dokunmadığı
 * alanlar sunucu değerine çekilir, dokunulan korunur, ikisi de değiştiyse alan işaretlenir; sonraki kayıt
 * yeni sürümle gider (otomatik yeniden gönderme YOK).
 *
 * Yerleşim: `row` her alan bir sütun (kısa tanımlar); `panel` düzenleme formu tablo genişliğinde ızgara
 * (çok alanlı tanımlar; `inList: false` alanlar yalnız formda).
 */
@Component({
  selector: 'rc-tanim-crud',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    TranslocoPipe,
    Icon,
    Alan,
    FormErrors,
    TextArea,
    TextInput,
    Checkbox,
    MoneyInput,
    NumberInput,
    Selection,
    DatePicker,
  ],
  styleUrl: './definition-crud.scss',
  templateUrl: './definition-crud.html',
})
export class DefinitionCrud implements OnInit {
  readonly baslik = input.required<string>();
  readonly alanlar = input.required<readonly TanimAlani[]>();
  readonly kaynak = input.required<DefinitionSource>();
  readonly layout = input<'row' | 'panel'>('row');
  /** Başarılı oluştur/güncelle/sil sonrası (sayfanın bağlı listeleri tazelensin). */
  readonly changed = output();

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly liste = new TemelStore<readonly DefinitionRow[]>(
    () => this.kaynak().listele(),
    {
      oncekiVeriyiKoru: true,
    },
  );
  protected readonly submission = formSubmission();
  private readonly deleteLock = new SubmitLock();
  protected readonly isDeleting = this.deleteLock.gonderiliyor;

  protected readonly editing = signal<Edit | null>(null);
  protected readonly toDelete = signal<string | null>(null);
  protected readonly deleteError = signal<string | null>(null);
  /** Sürümsüz satırın düzenlemesi açılırken tekil okuma sürüyor (satır kimliği) / başarısız oldu. */
  protected readonly opening = signal<string | null>(null);
  protected readonly openError = signal<string | null>(null);
  protected readonly suggestionLists = signal<Readonly<Record<string, readonly string[]>>>({});
  protected form = new FormGroup<Record<string, FormControl<unknown>>>({});
  /** Düzenlenen kaydın sunucu hâli (sürüm + birleştirme tabanı). */
  private readonly base = signal<DefinitionRow | null>(null);
  private suggestionSub = new Subscription();

  /** Tüm liste (sayfanın seçim listeleri için de; ör. şube birleştirme). */
  readonly rows = computed(() => this.liste.veri() ?? []);
  protected readonly formFields = computed(() => this.alanlar().filter((a) => !a.hidden));
  /** Satır yerleşiminde düzenleme satırı sütunlarla aynı olduğundan her form alanı sütundur. */
  protected readonly columns = computed(() =>
    this.layout() === 'row'
      ? this.formFields()
      : this.formFields().filter((a) => a.inList !== false),
  );

  protected readonly titleId = uniqueId('rc-tanim');

  constructor() {
    this.destroyRef.onDestroy(() => this.suggestionSub.unsubscribe());
  }

  ngOnInit(): void {
    this.liste.yukle();
  }

  /** Sayfanın `canDeactivate`'i için: açık satır formu kirli mi? */
  hasUnsavedChanges(): boolean {
    return this.editing() !== null && this.form.dirty;
  }

  /** Dışarıdaki bir işlemden (ör. toplu ekleme, birleştirme) sonra liste yeniden yüklenir. */
  reload(): void {
    this.liste.yenile();
  }

  protected yeni(): void {
    this.openError.set(null);
    this.openEdit({ tur: 'yeni' }, null);
  }

  protected edit(row: DefinitionRow): void {
    const source = this.kaynak();
    this.openError.set(null);
    if ((row.surum !== undefined && row.surum !== null) || !source.read) {
      this.openEdit({ tur: 'satir', id: row.id }, row);
      return;
    }
    this.opening.set(row.id);
    source
      .read(row.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          this.opening.set(null);
          this.openEdit({ tur: 'satir', id: row.id }, fresh);
        },
        error: (error: unknown) => {
          this.opening.set(null);
          this.openError.set(toApiError(error).detay);
        },
      });
  }

  protected cancel(): void {
    this.editing.set(null);
    this.suggestionSub.unsubscribe();
  }

  protected kaydet(): void {
    const d = this.editing();
    if (d === null) return;
    const value: DefinitionValue = this.form.getRawValue();
    const version = d.tur === 'satir' ? this.base()?.surum : undefined;
    this.submission.gonder(
      this.form,
      (key) =>
        d.tur === 'yeni'
          ? this.kaynak().olustur(value, key)
          : this.kaynak().guncelle(d.id, value, key, version),
      {
        gecersiz: () => this.focusFirstError(),
        basarili: () => {
          this.cancel();
          this.liste.yenile();
          this.changed.emit();
        },
        hata: (h) => {
          if (h.kod === 'cakisma' && d.tur === 'satir') this.mergeLatest(d.id);
        },
      },
    );
  }

  protected confirmDelete(id: string): void {
    this.deleteError.set(null);
    this.toDelete.set(id);
  }

  protected remove(id: string): void {
    this.deleteLock
      .gonder((key) => this.kaynak().sil(id, key))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toDelete.set(null);
          this.liste.yenile();
          this.changed.emit();
        },
        error: (error: unknown) => this.deleteError.set(toApiError(error).detay),
      });
  }

  protected isEditing(row: DefinitionRow): boolean {
    const d = this.editing();
    return d?.tur === 'satir' && d.id === row.id;
  }

  protected optionsOf(alan: TanimAlani): DefinitionOptions {
    const s = alan.secenekler;
    return typeof s === 'function' ? s() : (s ?? []);
  }

  /** Şablonlar `ngTemplateOutlet` ile başka yerde açıldığı için `formControlName` değil doğrudan kontrol. */
  protected controlOf(alan: TanimAlani): FormControl<unknown> {
    return this.form.controls[alan.ad] ?? new FormControl<unknown>(null);
  }

  protected suggestionsOf(alan: TanimAlani): readonly string[] {
    return this.suggestionLists()[alan.ad] ?? [];
  }

  protected datalistId(alan: TanimAlani): string {
    return `${this.titleId}-dl-${alan.ad}`;
  }

  protected show(alan: TanimAlani, value: unknown): string {
    if (value === null || value === undefined || value === '') return '';
    switch (alan.tur) {
      case 'onay':
        return value === true ? '✓' : '';
      case 'para':
        // Kayan noktaya girmeden (değer invariant metin ya da JSON sayısı).
        return `${formatDecimal(invariantDecimal(value as string | number, { kesir: 2 }), 2)} ₺`;
      case 'secim':
        return this.optionsOf(alan).find((s) => sameValue(s.deger, value))?.etiket ?? String(value);
      case 'date':
        return tarihBicimle(value as string);
      case 'textarea': {
        const text = String(value);
        return text.length > TEXTAREA_PREVIEW ? `${text.slice(0, TEXTAREA_PREVIEW)}…` : text;
      }
      case 'sayi': {
        const fraction = alan.fraction ?? 0;
        return fraction > 0
          ? formatDecimal(invariantDecimal(value as string | number, { kesir: fraction }), fraction)
          : String(value);
      }
      default:
        return String(value);
    }
  }

  private openEdit(d: Edit, row: DefinitionRow | null): void {
    const controls: Record<string, FormControl<unknown>> = {};
    for (const alan of this.alanlar()) {
      const validators: ValidatorFn[] = [];
      if (alan.zorunlu) {
        validators.push(alan.tur === 'onay' ? Validators.requiredTrue : Validators.required);
      }
      if (alan.azamiUzunluk) validators.push(Validators.maxLength(alan.azamiUzunluk));
      const defaultValue = alan.defaultValue ?? (alan.tur === 'onay' ? false : null);
      controls[alan.ad] = new FormControl<unknown>(
        row ? (row[alan.ad] ?? null) : defaultValue,
        validators,
      );
    }
    this.form = new FormGroup(controls);
    this.base.set(row);
    this.submission.kilit.yenile();
    this.toDelete.set(null);
    this.editing.set(d);
    this.watchSuggestions();
    afterNextRender(
      () =>
        this.element.nativeElement
          .querySelector<HTMLElement>('.duzenleme input, .duzenleme select')
          ?.focus(),
      { injector: this.injector },
    );
  }

  /** 409 `cakisma`: güncel kayıt okunur ve KİRLİ forma birleştirilir (form silinmez, yeniden gönderilmez). */
  private mergeLatest(id: string): void {
    const source = this.kaynak();
    if (!source.read) return;
    source
      .read(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          const d = this.editing();
          if (d?.tur !== 'satir' || d.id !== id) return;
          const base = this.base() ?? fresh;
          const message = this.t('form.tanim.cakismaAlan');
          for (const alan of this.alanlar()) {
            const control = this.form.controls[alan.ad];
            if (!control || !(alan.ad in fresh)) continue;
            const value = fresh[alan.ad] ?? null;
            if (alan.hidden || control.pristine) {
              control.setValue(value, { emitEvent: false });
            } else if (!sameValue(value, base[alan.ad]) && !sameValue(value, control.value)) {
              control.setErrors({ ...(control.errors ?? {}), [SERVER_ERROR]: [message] });
              control.markAsTouched();
            }
          }
          this.base.set(fresh);
        },
        // Okuma da başarısızsa form olduğu gibi kalır (bant interceptor'da); sonraki kayıt yine denenebilir.
        error: () => undefined,
      });
  }

  private watchSuggestions(): void {
    this.suggestionSub.unsubscribe();
    this.suggestionSub = new Subscription();
    this.suggestionLists.set({});
    for (const alan of this.alanlar()) {
      const fetch = alan.suggestions;
      const control = this.form.controls[alan.ad];
      if (alan.tur !== 'datalist' || !fetch || !control) continue;
      this.suggestionSub.add(
        control.valueChanges
          .pipe(
            startWith(control.value),
            map((v) => (typeof v === 'string' ? v.trim() : '')),
            debounceTime(SUGGESTION_DELAY_MS),
            distinctUntilChanged(),
            switchMap((q) => fetch(q).pipe(catchError(() => of([] as readonly string[])))),
          )
          .subscribe((list) => this.suggestionLists.update((s) => ({ ...s, [alan.ad]: list }))),
      );
    }
  }

  private focusFirstError(): void {
    afterNextRender(
      () =>
        this.element.nativeElement
          .querySelector<HTMLElement>('.duzenleme [aria-invalid="true"]')
          ?.focus(),
      { injector: this.injector },
    );
  }
}
