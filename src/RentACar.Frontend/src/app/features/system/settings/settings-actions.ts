import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
  type OnInit,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Observable, switchMap } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';

import {
  type DomainDto,
  type SendTestResult,
  type SettingsDto,
  needsTxtRecord,
} from './settings-model';

const ROOT = '/api/ui/v1/ayarlar' as const;
/** `LogoKurallari.MaxBayt` (asıl kural sunucuda). */
export const MAX_LOGO_BYTES = 1024 * 1024;

type TestKind = 'eposta' | 'sms' | 'whatsapp';

/**
 * F11.2b ayarlar — ana formun DIŞINDAKİ işlemler (Blazor'da da ayrı formlar): PDF logosu, halka açık site, özel alan
 * adı (DNS TXT sahiplik doğrulaması talimatıyla) ve dürüst test gönderimleri (e-posta / SMS / WhatsApp; gerçek mesaj
 * gönderir, önce onay sorulur, sonuç sunucunun söylediğidir — "iletildi" ≠ "teslim edildi"). Ayarı değiştiren her
 * işlemden sonra güncel ayar `updated` ile sayfaya verilir (sürüm tazelenir, kirli form alanları korunur).
 */
@Component({
  selector: 'rc-settings-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, TarihPipe, Alan, FormHatalari, MetinGirdisi],
  styleUrl: '../system.scss',
  templateUrl: './settings-actions.html',
})
export class SettingsActions implements OnInit {
  readonly dto = input.required<SettingsDto>();
  readonly updated = output<SettingsDto>();

  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();
  private readonly logoInput = viewChild<ElementRef<HTMLInputElement>>('logoInput');

  protected readonly needsTxtRecord = needsTxtRecord;
  /** Önbellek kırıcı: logo değişince görsel yeniden istenir. */
  private readonly logoVersion = signal(0);
  protected readonly logoSrc = computed(() => `${ROOT}/logo?v=${this.logoVersion()}`);
  protected readonly logoError = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly domainErrors = signal<Readonly<Record<string, string>>>({});

  protected readonly domainForm = new FormGroup({
    host: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(253)]),
  });
  protected readonly domainSubmit = formGonderimi();

  protected readonly testForms: Readonly<Record<TestKind, FormGroup>> = {
    eposta: new FormGroup({
      alici: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(254)]),
    }),
    sms: new FormGroup({
      telefon: new FormControl<string | null>(null, [
        Validators.required,
        Validators.maxLength(16),
      ]),
    }),
    whatsapp: new FormGroup({
      telefon: new FormControl<string | null>(null, [
        Validators.required,
        Validators.maxLength(16),
      ]),
    }),
  };
  protected readonly testSubmits: Readonly<Record<TestKind, ReturnType<typeof formGonderimi>>> = {
    eposta: formGonderimi(),
    sms: formGonderimi(),
    whatsapp: formGonderimi(),
  };
  protected readonly testResults = signal<Readonly<Partial<Record<TestKind, SendTestResult>>>>({});
  protected readonly testKinds: readonly TestKind[] = ['eposta', 'sms', 'whatsapp'];

  // ---- logo
  ngOnInit(): void {
    // Blazor paritesi: test alıcısı firmanın e-postası / WhatsApp numarasıyla ön dolu (yalnız bellekte).
    const d = this.dto();
    this.testForms.eposta.reset({ alici: d.firmaEmail ?? null });
    this.testForms.sms.reset({ telefon: d.whatsAppNumarasi ?? null });
    this.testForms.whatsapp.reset({ telefon: d.whatsAppNumarasi ?? null });
  }

  protected uploadLogo(): void {
    const file = this.logoInput()?.nativeElement.files?.[0] ?? null;
    this.logoError.set(null);
    if (!file) {
      this.logoError.set(this.t('sistem.ayarlar.logo.secilmedi'));
      return;
    }
    if (file.size > MAX_LOGO_BYTES) {
      this.logoError.set(this.t('sistem.ayarlar.logo.buyuk'));
      return;
    }
    const body = new FormData();
    body.append('dosya', file, file.name);
    this.run('logo', this.api.post(`${ROOT}/logo`, body), 'sistem.ayarlar.logo.yuklendi', () => {
      const el = this.logoInput()?.nativeElement;
      if (el) el.value = '';
      this.logoVersion.update((v) => v + 1);
    });
  }

  protected async removeLogo(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.ayarlar.logo.kaldirBaslik'),
      mesaj: this.t('sistem.ayarlar.logo.kaldirMesaj'),
      onayEtiketi: this.t('sistem.ayarlar.logo.kaldir'),
      tehlikeli: true,
    });
    if (yes) this.run('logo', this.api.delete(`${ROOT}/logo`), 'sistem.ayarlar.logo.kaldirildi');
  }

  // ---- site ve alan adları
  protected async openSite(): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.ayarlar.site.acBaslik'),
      mesaj: this.t('sistem.ayarlar.site.acMesaj'),
      onayEtiketi: this.t('sistem.ayarlar.site.ac'),
    });
    if (yes) {
      this.run('site', this.api.post(`${ROOT}/web-sitesi-ac`, {}), 'sistem.ayarlar.site.acildi');
    }
  }

  protected addDomain(): void {
    const host = this.domainForm.getRawValue().host;
    this.domainSubmit.gonder(
      this.domainForm,
      (key) => this.api.post<SettingsDto>(`${ROOT}/domainler`, { host }, { islemAnahtari: key }),
      {
        basarili: (dto) => {
          this.domainForm.reset();
          this.updated.emit(dto);
          this.toast.basari(this.t('sistem.ayarlar.domain.eklendi'));
        },
      },
    );
  }

  protected verifyDomain(d: DomainDto): void {
    if (this.busy() !== null) return;
    this.busy.set(`dogrula:${d.host}`);
    this.domainErrors.update((e) => ({ ...e, [d.host]: '' }));
    this.api
      .post<SettingsDto>(`${ROOT}/domainler/dogrula`, { host: d.host })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (dto) => {
          this.busy.set(null);
          this.updated.emit(dto);
          this.toast.basari(this.t('sistem.ayarlar.domain.dogrulandi', { host: d.host }));
        },
        error: (e: unknown) => {
          this.busy.set(null);
          const h = apiHatasinaCevir(e);
          this.domainErrors.update((x) => ({
            ...x,
            [d.host]: h.alanlar?.['host']?.[0] ?? h.detay,
          }));
        },
      });
  }

  protected domainKind(d: DomainDto): string {
    return this.t(d.tur === 'Custom' ? 'sistem.ayarlar.domain.ozel' : 'sistem.ayarlar.domain.alt');
  }

  protected domainStatus(d: DomainDto): string {
    const known = ['Active', 'PendingVerification', 'Failed'];
    return known.includes(d.durum)
      ? this.t(`sistem.ayarlar.domain.durum.${d.durum}` as CeviriAnahtari)
      : d.durum;
  }

  // ---- test gönderimleri (gerçek mesaj: önce onay)
  protected async sendTest(kind: TestKind): Promise<void> {
    const form = this.testForms[kind];
    const yes = await this.confirm.sor({
      baslik: this.t(`sistem.ayarlar.test.${kind}.baslik` as CeviriAnahtari),
      mesaj: this.t(`sistem.ayarlar.test.${kind}.onay` as CeviriAnahtari),
      onayEtiketi: this.t('sistem.ayarlar.test.gonder'),
    });
    if (!yes) return;
    this.testResults.update((r) => ({ ...r, [kind]: undefined }));
    this.testSubmits[kind].gonder(
      form,
      (key) =>
        this.api.post<SendTestResult>(`${ROOT}/test/${kind}`, form.getRawValue(), {
          islemAnahtari: key,
        }),
      { basarili: (r) => this.testResults.update((x) => ({ ...x, [kind]: r })) },
    );
  }

  /** İşlem → güncel ayar okunur ve sayfaya verilir (logo/site ucu tam ayar dönmeyebilir). */
  private run(tag: string, action: Observable<unknown>, done: CeviriAnahtari, after?: () => void) {
    if (this.busy() !== null) return;
    this.busy.set(tag);
    this.actionError.set(null);
    action
      .pipe(
        switchMap(() => this.api.get<SettingsDto>(ROOT)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (dto) => {
          this.busy.set(null);
          after?.();
          this.updated.emit(dto);
          this.toast.basari(this.t(done));
        },
        error: (e: unknown) => {
          this.busy.set(null);
          const h = apiHatasinaCevir(e);
          (tag === 'logo' ? this.logoError : this.actionError).set(
            h.alanlar?.['dosya']?.[0] ?? h.detay,
          );
        },
      });
  }
}
