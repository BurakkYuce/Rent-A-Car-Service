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
import { invariantOndalik, ondalikBicimle } from '@core/form/ondalik';
import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { GonderimKilidi } from '@core/form/gonderim-kilidi';
import { SUNUCU_HATASI } from '@core/form/sunucu-hatalari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { Ikon } from '../../ikon/ikon';
import { Alan } from '../alan/alan';
import { tekilKimlik } from '../alan/alan-baglami';
import { formGonderimi } from '../form-gonderimi';
import { FormHatalari } from '../form-hatalari';
import { MetinAlani } from '../kontroller/metin-alani';
import { MetinGirdisi } from '../kontroller/metin-girdisi';
import { OnayKutusu } from '../kontroller/onay-kutusu';
import { ParaGirdisi } from '../kontroller/para-girdisi';
import { SayiGirdisi } from '../kontroller/sayi-girdisi';
import { Secim } from '../kontroller/secim';
import { TarihSecici } from '../tarih/tarih-secici';
import type {
  DefinitionOptions,
  TanimAlani,
  TanimDegeri,
  TanimKaynagi,
  TanimSatiri,
} from './tanim-kaynagi';

type Duzenleme = { readonly tur: 'yeni' } | { readonly tur: 'satir'; readonly id: string };

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
    Ikon,
    Alan,
    FormHatalari,
    MetinAlani,
    MetinGirdisi,
    OnayKutusu,
    ParaGirdisi,
    SayiGirdisi,
    Secim,
    TarihSecici,
  ],
  styleUrl: './tanim-crud.scss',
  templateUrl: './tanim-crud.html',
})
export class TanimCrud implements OnInit {
  readonly baslik = input.required<string>();
  readonly alanlar = input.required<readonly TanimAlani[]>();
  readonly kaynak = input.required<TanimKaynagi>();
  readonly layout = input<'row' | 'panel'>('row');
  /** Başarılı oluştur/güncelle/sil sonrası (sayfanın bağlı listeleri tazelensin). */
  readonly changed = output();

  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly liste = new TemelStore<readonly TanimSatiri[]>(() => this.kaynak().listele(), {
    oncekiVeriyiKoru: true,
  });
  protected readonly gonderim = formGonderimi();
  private readonly silmeKilidi = new GonderimKilidi();
  protected readonly siliniyor = this.silmeKilidi.gonderiliyor;

  protected readonly duzenleme = signal<Duzenleme | null>(null);
  protected readonly silinecek = signal<string | null>(null);
  protected readonly silmeHatasi = signal<string | null>(null);
  /** Sürümsüz satırın düzenlemesi açılırken tekil okuma sürüyor (satır kimliği) / başarısız oldu. */
  protected readonly opening = signal<string | null>(null);
  protected readonly openError = signal<string | null>(null);
  protected readonly suggestionLists = signal<Readonly<Record<string, readonly string[]>>>({});
  protected form = new FormGroup<Record<string, FormControl<unknown>>>({});
  /** Düzenlenen kaydın sunucu hâli (sürüm + birleştirme tabanı). */
  private readonly base = signal<TanimSatiri | null>(null);
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

  protected readonly baslikKimligi = tekilKimlik('rc-tanim');

  constructor() {
    this.destroyRef.onDestroy(() => this.suggestionSub.unsubscribe());
  }

  ngOnInit(): void {
    this.liste.yukle();
  }

  /** Sayfanın `canDeactivate`'i için: açık satır formu kirli mi? */
  kaydedilmemisDegisiklikVar(): boolean {
    return this.duzenleme() !== null && this.form.dirty;
  }

  /** Dışarıdaki bir işlemden (ör. toplu ekleme, birleştirme) sonra liste yeniden yüklenir. */
  reload(): void {
    this.liste.yenile();
  }

  protected yeni(): void {
    this.openError.set(null);
    this.duzenlemeyiAc({ tur: 'yeni' }, null);
  }

  protected duzenle(satir: TanimSatiri): void {
    const kaynak = this.kaynak();
    this.openError.set(null);
    if ((satir.surum !== undefined && satir.surum !== null) || !kaynak.read) {
      this.duzenlemeyiAc({ tur: 'satir', id: satir.id }, satir);
      return;
    }
    this.opening.set(satir.id);
    kaynak
      .read(satir.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          this.opening.set(null);
          this.duzenlemeyiAc({ tur: 'satir', id: satir.id }, fresh);
        },
        error: (hata: unknown) => {
          this.opening.set(null);
          this.openError.set(apiHatasinaCevir(hata).detay);
        },
      });
  }

  protected vazgec(): void {
    this.duzenleme.set(null);
    this.suggestionSub.unsubscribe();
  }

  protected kaydet(): void {
    const d = this.duzenleme();
    if (d === null) return;
    const deger: TanimDegeri = this.form.getRawValue();
    const version = d.tur === 'satir' ? this.base()?.surum : undefined;
    this.gonderim.gonder(
      this.form,
      (anahtar) =>
        d.tur === 'yeni'
          ? this.kaynak().olustur(deger, anahtar)
          : this.kaynak().guncelle(d.id, deger, anahtar, version),
      {
        gecersiz: () => this.ilkHatayaOdaklan(),
        basarili: () => {
          this.vazgec();
          this.liste.yenile();
          this.changed.emit();
        },
        hata: (h) => {
          if (h.kod === 'cakisma' && d.tur === 'satir') this.mergeLatest(d.id);
        },
      },
    );
  }

  protected silmeyiSor(id: string): void {
    this.silmeHatasi.set(null);
    this.silinecek.set(id);
  }

  protected sil(id: string): void {
    this.silmeKilidi
      .gonder((anahtar) => this.kaynak().sil(id, anahtar))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.silinecek.set(null);
          this.liste.yenile();
          this.changed.emit();
        },
        error: (hata: unknown) => this.silmeHatasi.set(apiHatasinaCevir(hata).detay),
      });
  }

  protected duzenleniyor(satir: TanimSatiri): boolean {
    const d = this.duzenleme();
    return d?.tur === 'satir' && d.id === satir.id;
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
    return `${this.baslikKimligi}-dl-${alan.ad}`;
  }

  protected goster(alan: TanimAlani, deger: unknown): string {
    if (deger === null || deger === undefined || deger === '') return '';
    switch (alan.tur) {
      case 'onay':
        return deger === true ? '✓' : '';
      case 'para':
        // Kayan noktaya girmeden (değer invariant metin ya da JSON sayısı).
        return `${ondalikBicimle(invariantOndalik(deger as string | number, { kesir: 2 }), 2)} ₺`;
      case 'secim':
        return this.optionsOf(alan).find((s) => sameValue(s.deger, deger))?.etiket ?? String(deger);
      case 'date':
        return tarihBicimle(deger as string);
      case 'textarea': {
        const text = String(deger);
        return text.length > TEXTAREA_PREVIEW ? `${text.slice(0, TEXTAREA_PREVIEW)}…` : text;
      }
      case 'sayi': {
        const fraction = alan.fraction ?? 0;
        return fraction > 0
          ? ondalikBicimle(
              invariantOndalik(deger as string | number, { kesir: fraction }),
              fraction,
            )
          : String(deger);
      }
      default:
        return String(deger);
    }
  }

  private duzenlemeyiAc(d: Duzenleme, satir: TanimSatiri | null): void {
    const kontroller: Record<string, FormControl<unknown>> = {};
    for (const alan of this.alanlar()) {
      const dogrulayicilar: ValidatorFn[] = [];
      if (alan.zorunlu) {
        dogrulayicilar.push(alan.tur === 'onay' ? Validators.requiredTrue : Validators.required);
      }
      if (alan.azamiUzunluk) dogrulayicilar.push(Validators.maxLength(alan.azamiUzunluk));
      const varsayilan = alan.defaultValue ?? (alan.tur === 'onay' ? false : null);
      kontroller[alan.ad] = new FormControl<unknown>(
        satir ? (satir[alan.ad] ?? null) : varsayilan,
        dogrulayicilar,
      );
    }
    this.form = new FormGroup(kontroller);
    this.base.set(satir);
    this.gonderim.kilit.yenile();
    this.silinecek.set(null);
    this.duzenleme.set(d);
    this.watchSuggestions();
    afterNextRender(
      () =>
        this.eleman.nativeElement
          .querySelector<HTMLElement>('.duzenleme input, .duzenleme select')
          ?.focus(),
      { injector: this.injector },
    );
  }

  /** 409 `cakisma`: güncel kayıt okunur ve KİRLİ forma birleştirilir (form silinmez, yeniden gönderilmez). */
  private mergeLatest(id: string): void {
    const kaynak = this.kaynak();
    if (!kaynak.read) return;
    kaynak
      .read(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          const d = this.duzenleme();
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
              control.setErrors({ ...(control.errors ?? {}), [SUNUCU_HATASI]: [message] });
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

  private ilkHatayaOdaklan(): void {
    afterNextRender(
      () =>
        this.eleman.nativeElement
          .querySelector<HTMLElement>('.duzenleme [aria-invalid="true"]')
          ?.focus(),
      { injector: this.injector },
    );
  }
}
