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
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { Secim } from '@shared/form/kontroller/secim';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import { mergeServerValues } from '../shared/version-merge';
import {
  COLOR_FIELDS,
  PRICE_TYPES,
  SECRET_FLAGS,
  SETTINGS_FIELDS,
  SMTP_PORTS,
  type SecretField,
  type SettingsDto,
  settingsBody,
  settingsFormValue,
  settingsServerValues,
} from './settings-model';
import { SettingsActions } from './settings-actions';

const ROOT = '/api/ui/v1/ayarlar' as const;
type VehicleGroupPage = Sema<'SayfaOfVehicleGroupDto'>;

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
  eFaturaKullanici: 128,
  eFaturaSifre: 256,
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
  whatsAppNumarasi: 32,
  ...Object.fromEntries(COLOR_FIELDS.map((c) => [c, 7])),
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
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
    OnayKutusu,
    SayiGirdisi,
    Secim,
    SettingsActions,
  ],
  styleUrl: '../system.scss',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly form = new FormGroup(
    Object.fromEntries(
      SETTINGS_FIELDS.map((name) => {
        const max = MAX_LENGTH[name];
        return [name, new FormControl<unknown>(null, max ? [Validators.maxLength(max)] : [])];
      }),
    ) as Record<string, FormControl<unknown>>,
  );
  protected readonly colorFields = COLOR_FIELDS;
  protected readonly maxLength = MAX_LENGTH;

  /** Sunucunun son okunan hâli (birleştirme tabanı + `surum` + sır bayrakları). */
  protected readonly dto = signal<SettingsDto | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly submit = formGonderimi();

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
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
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

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected secretPlaceholder(field: SecretField): string {
    return this.t(this.secretSet()[field] ? 'sistem.ayarlar.sirKayitli' : 'sistem.ayarlar.sirBos');
  }

  protected save(): void {
    const body = settingsBody(this.form.getRawValue(), this.dto()?.surum);
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
        error: (e: unknown) => this.loadError.set(apiHatasinaCevir(e).detay),
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
