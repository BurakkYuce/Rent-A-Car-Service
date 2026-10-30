import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

import { toApiError } from '@core/api/api-error';
import { ApiIstemcisi } from '@core/api/api-client';
import type { Schema } from '@core/api/ui-types';
import { pageLeaveGuard } from '@core/form/unsaved-changes';
import { ConfirmService } from '@core/feedback/confirm-service';
import { ToastService } from '@core/feedback/toast-service';
import { translationFunction } from '@core/i18n/translation';
import { Alan } from '@shared/form/field/field';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/controls/text-input';
import { Checkbox } from '@shared/form/controls/checkbox';
import { NumberInput } from '@shared/form/controls/number-input';
import { Selection } from '@shared/form/controls/selection';
import { RadioGroup } from '@shared/form/controls/radio-group';
import type { SecenekOgesi } from '@shared/form/controls/option';

import { PageBand } from '../../../shell/page-band/page-band';
import { mergeServerValues } from '../shared/version-merge';
import {
  COLOR_FIELDS,
  EINVOICE_SCENARIOS,
  PRICE_TYPES,
  SECRET_CLEAR_FLAGS,
  SECRET_FLAGS,
  SETTINGS_FIELDS,
  SITE_THEMES,
  SMTP_PORTS,
  type SecretField,
  type SettingsDto,
  type SiteTheme,
  integratorOptions,
  paletteAfterThemeChange,
  palettesFor,
  secretsToClear,
  settingsBody,
  settingsFormValue,
  settingsServerValues,
  themePreviewUrl,
} from './settings-model';
import { SettingsActions } from './settings-actions';
import { SiteColorInput } from './site-color-input';
import { SitePalettePicker } from './site-palette-picker';

const ROOT = '/api/ui/v1/ayarlar' as const;
type VehicleGroupPage = Schema<'SayfaOfVehicleGroupDto'>;

/** Uç sınırları (`ValidateSettings`) — istemci yalnız erken uyarır; asıl kural sunucuda. */
const MAX_LENGTH: Readonly<Partial<Record<string, number>>> = {
  firmaUnvan: 256,
  firmaMarka: 128,
  firmaVergiDairesi: 128,
  firmaVergiNo: 32,
  firmaAdres: 512,
  firmaTel: 64,
  firmaMobilTel: 64,
  firmaEmail: 128,
  firmaIl: 64,
  firmaIlce: 64,
  eFaturaKullanici: 128,
  eFaturaSifre: 256,
  eFaturaEntegrator: 32,
  eFaturaGondericiEtiket: 256,
  eFaturaPostaKutusuEtiket: 256,
  eFaturaSenaryo: 16,
  smsBaslik: 64,
  smsApiKey: 256,
  posMerchantId: 128,
  posApiKey: 256,
  logoUrl: 512,
  varsayilanDoviz: 3,
  smtpHost: 256,
  smtpKullanici: 256,
  smtpSifre: 256,
  smtpGonderenAdres: 256,
  smtpGonderenAd: 128,
  faturaSeriKodu: 3,
  eFaturaSeriKodu: 3,
  eArsivSeriKodu: 3,
  whatsAppNumarasi: 32,
  ...Object.fromEntries(COLOR_FIELDS.map((c) => [c, 7])),
  siteVurguRengi: 7,
  siteDepozitoAciklama: 256,
  siteSigortaOzeti: 512,
  siteIptalKosulu: 256,
  siteGoogleProfilUrl: 512,
};

/**
 * F11.2b firma ayarları (Blazor `Ayarlar`, ManageUsers). Tam değiştirme PUT'u (`surum` zorunlu); 409 `cakisma` formu
 * SİLMEZ — güncel kayıt okunur ve kirli forma birleştirilir. Sır alanları yalnız yazılabilir: yanıtta yoktur, boş =
 * korunur, parola kutuları `autocomplete="new-password"`, kayıttan sonra formdan silinir, tarayıcı deposuna yazılmaz.
 */
@Component({
  selector: 'rc-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormErrors,
    TextInput,
    Checkbox,
    NumberInput,
    Selection,
    RadioGroup,
    SettingsActions,
    SitePalettePicker,
    SiteColorInput,
  ],
  styleUrl: '../system.scss',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = translationFunction();

  protected readonly form = new FormGroup({
    ...(Object.fromEntries(
      SETTINGS_FIELDS.map((name) => {
        const max = MAX_LENGTH[name];
        return [name, new FormControl<unknown>(null, max ? [Validators.maxLength(max)] : [])];
      }),
    ) as Record<string, FormControl<unknown>>),
    // "Kayıtlı değeri sil" kutuları (yalnız sır kayıtlıyken görünür; kayıtta onay diyaloğu).
    ...(Object.fromEntries(
      Object.values(SECRET_CLEAR_FLAGS).map((flag) => [flag, new FormControl<unknown>(false)]),
    ) as Record<string, FormControl<unknown>>),
  });
  protected readonly clearFlags = SECRET_CLEAR_FLAGS;
  protected readonly colorFields = COLOR_FIELDS;
  protected readonly maxLength = MAX_LENGTH;

  /** Sunucunun son okunan hâli (birleştirme tabanı + `surum` + sır bayrakları). */
  protected readonly dto = signal<SettingsDto | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submit = formSubmission();

  /** Site teması radyo kartları: ad + bir cümle açıklama (etiket tek metin). */
  protected readonly themeKeys = SITE_THEMES;
  protected readonly themeOptions: readonly SecenekOgesi<string>[] = SITE_THEMES.map((k) => ({
    deger: k,
    etiket: `${this.themeName(k)}: ${this.t(`sistem.ayarlar.tema.${k}.aciklama`)}`,
  }));

  protected themeName(key: SiteTheme): string {
    return this.t(`sistem.ayarlar.tema.${key}.ad`);
  }

  /** Formun canlı değeri (kaydedilmemiş seçim dahil): palet listesi ve önizleme bağlantıları bundan türer. */
  private readonly formValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  private readonly selectedTheme = computed(() => {
    const theme = this.formValue()['siteTemasi'];
    return typeof theme === 'string' && theme !== '' ? theme : SITE_THEMES[0];
  });

  /** Seçili temanın palet setleri (ilk = varsayılan); tema değişince liste değişir. */
  protected readonly palettes = computed(() => palettesFor(this.dto(), this.selectedTheme()));

  /** Özel ana renk yokken renk seçicinin gösterdiği renk: seçili setin (yoksa varsayılan setin) ana rengi. */
  protected readonly paletteDefaultColor = computed(() => {
    const list = this.palettes();
    const key = this.formValue()['sitePaleti'];
    return (list.find((p) => p.anahtar === key) ?? list[0])?.ana ?? null;
  });

  /** "Seçili görünümü önizle": formdaki tema + palet + kendi ana renk (kaydetmeden). */
  protected currentPreviewUrl(host: string): string {
    const v = this.formValue();
    const palette = v['sitePaleti'];
    return themePreviewUrl(
      host,
      this.selectedTheme(),
      typeof palette === 'string' ? palette : null,
      v['siteVurguRengi'],
    );
  }

  /** Tema önizlemesi: o tema + (geçerliyse) kendi ana renk; palet temaya özgü olduğundan taşınmaz. */
  protected previewUrl(host: string, key: SiteTheme): string {
    return themePreviewUrl(host, key, null, this.formValue()['siteVurguRengi']);
  }

  protected readonly priceTypes: readonly SecenekOgesi<string>[] = PRICE_TYPES.map((p) => ({
    deger: p,
    etiket: p,
  }));
  protected readonly smtpPorts: readonly SecenekOgesi<number>[] = SMTP_PORTS.map((p) => ({
    deger: p,
    etiket: String(p),
  }));
  protected readonly triState: readonly SecenekOgesi<boolean>[] = [
    { deger: true, etiket: this.t('sistem.ortak.evet') },
    { deger: false, etiket: this.t('sistem.ortak.hayir') },
  ];
  protected readonly groups = signal<readonly SecenekOgesi<string>[]>([]);

  /** e-Belge entegratörü: kayıtlı adaptörler (bu sürümde boş olabilir) + senaryo seçenekleri. */
  protected readonly integrators = computed(() => integratorOptions(this.dto()));
  protected readonly scenarios: readonly SecenekOgesi<string>[] = EINVOICE_SCENARIOS.map((s) => ({
    deger: s,
    etiket: this.t(`sistem.ayarlar.eBelge.senaryo.${s}`),
  }));

  protected readonly secretSet = computed(() => {
    const d = this.dto();
    const flag = (f: SecretField) =>
      d !== null && (d as unknown as Record<string, unknown>)[SECRET_FLAGS[f]] === true;
    return {
      eFaturaSifre: flag('eFaturaSifre'),
      smsApiKey: flag('smsApiKey'),
      posApiKey: flag('posApiKey'),
      smtpSifre: flag('smtpSifre'),
    };
  });

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    // Tema değişince yeni temaya ait olmayan palet seçimi temizlenir (null = temanın varsayılan seti); aksi halde
    // sunucu kaydı 400 errors.sitePaleti ile reddederdi.
    this.form.controls['siteTemasi'].valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((theme) => {
        const control = this.form.controls['sitePaleti'];
        const next = paletteAfterThemeChange(
          control.value,
          palettesFor(this.dto(), typeof theme === 'string' ? theme : null),
        );
        if (next !== control.value) control.setValue(next);
      });
    this.load();
    this.api
      .get<VehicleGroupPage>('/api/ui/v1/arac-gruplari', {
        parametreler: { aktif: true, boyut: 200, sirala: 'ad' },
      })
      .pipe(
        map((p) => p.kayitlar.map((g) => ({ deger: g.id, etiket: g.ad }))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (l) => this.groups.set(l), error: () => this.groups.set([]) });
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected secretPlaceholder(field: SecretField): string {
    return this.t(this.secretSet()[field] ? 'sistem.ayarlar.sirKayitli' : 'sistem.ayarlar.sirBos');
  }

  protected async save(): Promise<void> {
    const value = this.form.getRawValue();
    const clearing = secretsToClear(value);
    if (clearing.length > 0) {
      const names = clearing.map((f) => this.t(`sistem.ayarlar.alan.${f}`)).join(', ');
      const yes = await this.confirm.ask({
        baslik: this.t('sistem.ayarlar.sirSilOnayBaslik'),
        mesaj: this.t('sistem.ayarlar.sirSilOnayMesaj', { alanlar: names }),
        onayEtiketi: this.t('sistem.ayarlar.sirSilOnayla'),
        tehlikeli: true,
      });
      if (!yes) return;
    }
    const body = settingsBody(value, this.dto()?.surum);
    this.submit.gonder(
      this.form,
      (key) => this.api.put<SettingsDto>(ROOT, body, { islemAnahtari: key }),
      {
        gecersiz: () => this.focusFirstInvalid(),
        basarili: (dto) => {
          // İstek sürerken yazılan değer silinmesin: form hâlâ gönderilen hâldeyse (pristine) sunucu hâline
          // sıfırlanır (gönderilen sırlar formdan düşer), değilse yalnız sürüm tazelenir ve dokunulmayan alanlar
          // birleşir.
          if (this.form.pristine) this.apply(dto);
          else this.refreshed(dto);
          this.toast.basari(this.t('sistem.ayarlar.kaydedildi'));
        },
        hata: (h) => {
          if (h.kod === 'cakisma') this.mergeLatest();
        },
      },
    );
  }

  /** Alt bileşenin işlemi (logo, site aç, alan adı) yeni ayar döndürdü: sürüm tazelenir, kirli alanlar korunur. */
  protected refreshed(dto: SettingsDto): void {
    const base = this.dto();
    mergeServerValues(
      this.form,
      base ? settingsServerValues(base) : null,
      settingsServerValues(dto),
      this.t('form.tanim.cakismaAlan'),
    );
    this.dto.set(dto);
  }

  protected load(): void {
    this.loadError.set(null);
    this.api
      .get<SettingsDto>(ROOT)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (dto) => this.apply(dto),
        error: (e: unknown) => this.loadError.set(toApiError(e).detay),
      });
  }

  private apply(dto: SettingsDto): void {
    this.form.reset(settingsFormValue(dto));
    this.dto.set(dto);
  }

  private mergeLatest(): void {
    this.api
      .get<SettingsDto>(ROOT)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fresh) => this.refreshed(fresh),
        error: () => undefined,
      });
  }

  private focusFirstInvalid(): void {
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
      { injector: this.injector },
    );
  }
}
