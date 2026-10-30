import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { toApiError } from '@core/api/api-error';
import { ApiIstemcisi } from '@core/api/api-client';
import type { Schema } from '@core/api/ui-types';
import { pageLeaveGuard } from '@core/form/unsaved-changes';
import { ToastService } from '@core/feedback/toast-service';
import { translationFunction } from '@core/i18n/translation';
import { genelGosterilir } from '@core/session/session-interceptor';
import { TemelStore } from '@core/data/base-store';
import { Alan } from '@shared/form/field/field';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { Checkbox } from '@shared/form/controls/checkbox';
import type { SecenekOgesi } from '@shared/form/controls/option';
import { Selection } from '@shared/form/controls/selection';
import { TextInput } from '@shared/form/controls/text-input';

import { PageBand } from '../../../shell/page-band/page-band';
import { mergeServerValues } from '../shared/version-merge';

type KabisSettings = Schema<'KabisAyarDto'>;
type ConnectionTest = Schema<'KabisBaglantiTestiDto'>;

const ROOT = '/api/ui/v1/ayarlar/kabis' as const;

/** Formun sunucu değerleri (birleştirme tabanı). Şifre yanıtta YOK — her zaman boş başlar. */
export function kabisSettingsValues(s: KabisSettings): Record<string, unknown> {
  return {
    etkin: s.etkin,
    kurum: s.kurum === 'Jandarma' ? 'Jandarma' : 'Emniyet',
    kullanici: s.kullanici,
    teslimiEngelle: s.teslimiEngelle,
  };
}

/**
 * KABİS (Kiralık Araç Bildirim Sistemi) ayarları — ManageUsers. Otomatik bildirim aç/kapa, kurum (Emniyet/Jandarma),
 * kullanıcı adı, şifre (YALNIZ yazılır: yanıtta hiç dönmez, boş bırakılırsa kayıtlı korunur) ve "teslimi engelle".
 * PUT tam değiştirme `surum` ile; 409 `cakisma` formu silmez (güncel değerler birleşir). Bağlantı sınaması dürüst:
 * gerçek bağlantı yapılandırılmadıysa başarı göstermez.
 */
@Component({
  selector: 'rc-kabis-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormErrors,
    Checkbox,
    Selection,
    TextInput,
  ],
  styleUrl: '../system.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'sistem.kabis.baslik' | transloco" ikon="shield" />
    <div class="rc-sayfa">
      <p class="not">{{ 'sistem.kabis.aciklama' | transloco }}</p>
      @switch (settings.tur()) {
        @case ('hata') {
          <div class="rc-form-mesaji rc-form-mesaji--hata" role="alert">
            {{ 'sistem.ortak.yuklenemedi' | transloco }} {{ settings.hata()?.detay }}
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="settings.yenile()">
              {{ 'sistem.ortak.yenidenDene' | transloco }}
            </button>
          </div>
        }
        @case ('hazir') {
          <section class="rc-bolum">
            <rc-form-hatalari [hatalar]="submit.genelHatalar()" />
            <div class="rc-form-izgara" [formGroup]="form">
              <rc-onay-kutusu formControlName="etkin">{{
                'sistem.kabis.etkin' | transloco
              }}</rc-onay-kutusu>
              <rc-alan [etiket]="'sistem.kabis.kurum' | transloco">
                <rc-secim formControlName="kurum" [secenekler]="authorities" />
              </rc-alan>
              <rc-alan [etiket]="'sistem.kabis.kullanici' | transloco">
                <rc-metin-girdisi formControlName="kullanici" [azamiUzunluk]="128" />
              </rc-alan>
              <rc-alan
                [etiket]="'sistem.kabis.sifre' | transloco"
                [ipucu]="'sistem.kabis.sifreIpucu' | transloco"
              >
                <rc-metin-girdisi
                  formControlName="sifre"
                  tur="password"
                  otomatikTamamlama="new-password"
                  [azamiUzunluk]="256"
                  [yerTutucu]="
                    (passwordSet() ? 'sistem.ayarlar.sirKayitli' : 'sistem.ayarlar.sirBos')
                      | transloco
                  "
                />
              </rc-alan>
              @if (passwordSet()) {
                <rc-onay-kutusu formControlName="sifreTemizle">{{
                  'sistem.ayarlar.sirSil' | transloco
                }}</rc-onay-kutusu>
              }
              <rc-onay-kutusu formControlName="teslimiEngelle">{{
                'sistem.kabis.teslimiEngelle' | transloco
              }}</rc-onay-kutusu>
              <p class="not">{{ 'sistem.kabis.teslimiEngelleIpucu' | transloco }}</p>
              <div class="eylemler">
                <button
                  type="button"
                  class="rc-dugme rc-dugme--birincil"
                  [disabled]="submit.gonderiliyor()"
                  (click)="save()"
                >
                  {{ 'sistem.ortak.kaydet' | transloco }}
                </button>
                <button
                  type="button"
                  class="rc-dugme"
                  [disabled]="testing() || form.dirty"
                  [attr.title]="form.dirty ? ('sistem.kabis.onceKaydet' | transloco) : null"
                  (click)="test()"
                >
                  {{ 'sistem.kabis.test' | transloco }}
                </button>
              </div>
              @if (testResult(); as r) {
                <p
                  [class]="r.ok ? 'rc-form-mesaji' : 'rc-form-mesaji rc-form-mesaji--uyari'"
                  role="status"
                  data-testid="kabis-test-sonucu"
                >
                  {{ r.mesaj }}
                </p>
              }
            </div>
          </section>
          <p class="not">{{ 'sistem.kabis.durustNot' | transloco }}</p>
        }
        @default {
          <p class="bos" role="status">{{ 'sistem.ortak.yukleniyor' | transloco }}</p>
        }
      }
    </div>
  `,
})
export class KabisSettingsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();

  protected readonly authorities: readonly SecenekOgesi<string>[] = [
    { deger: 'Emniyet', etiket: this.t('sistem.kabis.emniyet') },
    { deger: 'Jandarma', etiket: this.t('sistem.kabis.jandarma') },
  ];
  protected readonly settings = new TemelStore<KabisSettings>(() =>
    this.api.get<KabisSettings>(ROOT),
  );
  protected readonly passwordSet = signal(false);
  protected readonly testing = signal(false);
  protected readonly testResult = signal<ConnectionTest | null>(null);
  protected readonly form = new FormGroup({
    etkin: new FormControl<boolean | null>(false),
    kurum: new FormControl<string | null>('Emniyet', Validators.required),
    kullanici: new FormControl<string | null>(null, Validators.maxLength(128)),
    sifre: new FormControl<string | null>(null, Validators.maxLength(256)),
    sifreTemizle: new FormControl<boolean | null>(false),
    teslimiEngelle: new FormControl<boolean | null>(false),
  });
  protected readonly submit = formSubmission();
  private current: KabisSettings | null = null;

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    // Yükleme (ve hata sonrası "Yeniden dene") bitince form sunucu değerleriyle dolar; kayıt sonrası doğrudan dolar.
    effect(() => {
      const s = this.settings.veri();
      if (s) untracked(() => this.fill(s));
    });
    this.settings.yukle();
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  private fill(s: KabisSettings): void {
    this.current = s;
    this.passwordSet.set(s.sifreTanimli);
    this.form.reset({ ...kabisSettingsValues(s), sifre: null, sifreTemizle: false });
  }

  protected save(): void {
    const v = this.form.getRawValue();
    const body = {
      etkin: v.etkin === true,
      kurum: v.kurum ?? 'Emniyet',
      kullanici: v.kullanici?.trim() || null,
      sifre: v.sifre?.trim() || null,
      sifreTemizle: v.sifreTemizle === true,
      teslimiEngelle: v.teslimiEngelle === true,
      surum: this.current?.surum ?? null,
    };
    this.submit.gonder(
      this.form,
      (key) => this.api.put<KabisSettings>(ROOT, body, { islemAnahtari: key }),
      {
        basarili: (s) => {
          this.fill(s);
          this.testResult.set(null);
          this.toast.basari(this.t('sistem.ortak.kaydedildi'));
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.mergeLatest();
        },
      },
    );
  }

  protected test(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.api
      .post<ConnectionTest>(`${ROOT}/baglanti-testi`, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (r) => {
          this.testing.set(false);
          this.testResult.set(r);
        },
        error: (raw: unknown) => {
          this.testing.set(false);
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
        },
      });
  }

  private mergeLatest(): void {
    const base = this.current;
    this.api
      .get<KabisSettings>(ROOT)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => {
          mergeServerValues(
            this.form,
            base ? kabisSettingsValues(base) : null,
            kabisSettingsValues(fresh),
            this.t('form.tanim.cakismaAlan'),
          );
          this.current = fresh;
          this.passwordSet.set(fresh.sifreTanimli);
        },
        error: () => undefined,
      });
  }
}
