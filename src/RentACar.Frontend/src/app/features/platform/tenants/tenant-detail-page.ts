import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom, type Observable } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { TemelStore } from '@core/veri/temel-store';
import { MoneyPipe, NumberPipe, DatePipe, DateTimePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextArea } from '@shared/form/kontroller/text-area';
import { TextInput } from '@shared/form/kontroller/text-input';

import {
  infoFromDetail,
  logoInfo,
  PLAN_SUGGESTIONS,
  PLATFORM_API,
  type PlatformTenantDetail,
  type TenantInfoValue,
  type TenantStatus,
  tenantStatus,
  tenantStatusBadge,
  toggleTarget,
  toNumber,
  updateBody,
} from '../platform-model';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { PlatformSessionService } from '../platform-session';

type InfoKey = keyof TenantInfoValue;
const INFO_KEYS: readonly InfoKey[] = ['ad', 'yetkiliAd', 'eposta', 'telefon', 'plan', 'notlar'];

/**
 * `/app/platform/kiracilar/:id` — tenant detail (Blazor `TenantDetail` parity): status + dates, metric
 * cards (owner cross-tenant counts, 30-day ledger revenue as ONE aggregate), info form (full-replace PUT
 * with `surum`; 409 `cakisma` → the fresh record is reloaded and merged into untouched fields, typed
 * fields are kept), access management (suspend/resume, close with the typed tenant code — checked on the
 * server —, reopen), PDF logo, "Web Sitesi" module licence, new-UI pilot switch, public site (read-only).
 */
@Component({
  selector: 'rc-tenant-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormErrors,
    TextArea,
    TextInput,
    MoneyPipe,
    PageBand,
    NumberPipe,
    DatePipe,
    DateTimePipe,
  ],
  templateUrl: './tenant-detail-page.html',
  styleUrls: ['../platform-page.scss'],
})
export class TenantDetailPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly session = inject(PlatformSessionService);
  private readonly t = translationFunction();

  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';
  private readonly root = `${PLATFORM_API}/kiracilar/${encodeURIComponent(this.id)}` as const;

  protected readonly store = new TemelStore(() => this.api.get<PlatformTenantDetail>(this.root), {
    oncekiVeriyiKoru: true,
  });
  protected readonly detail = computed(() => this.store.veri());
  protected readonly notFound = computed(() => this.store.hata()?.status === 404);
  protected readonly status = computed<TenantStatus>(() =>
    tenantStatus(this.detail()?.durum ?? 'Pasif'),
  );
  protected readonly toggle = computed(() => toggleTarget(this.status()));
  protected readonly logoText = computed(() => {
    const d = this.detail();
    return d ? logoInfo(d.logo) : null;
  });

  protected readonly badge = tenantStatusBadge;
  protected readonly num = toNumber;
  protected readonly plans = PLAN_SUGGESTIONS;

  protected readonly info = new FormGroup({
    ad: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(256)]),
    yetkiliAd: new FormControl<string | null>(null, Validators.maxLength(128)),
    eposta: new FormControl<string | null>(null, [Validators.email, Validators.maxLength(256)]),
    telefon: new FormControl<string | null>(null, Validators.maxLength(32)),
    plan: new FormControl<string | null>(null, Validators.maxLength(64)),
    notlar: new FormControl<string | null>(null, Validators.maxLength(2000)),
  });
  protected readonly infoSubmit = formSubmission();

  protected readonly closeForm = new FormGroup({
    onayKod: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly closeSubmit = formSubmission();

  /** One mutation at a time (switches, logo, status). */
  protected readonly busy = signal(false);
  protected readonly logoFile = signal<File | null>(null);
  protected readonly logoError = signal<string | null>(null);
  /** Cache-buster for the preview image (the logo is not part of the tenant row version). */
  protected readonly logoVersion = signal(0);
  protected readonly logoUrl = computed(() => `${this.root}/logo?v=${this.logoVersion()}`);

  constructor() {
    this.store.yukle();
    effect(() => {
      const d = this.detail();
      if (d) this.mergeInfo(d);
    });
    effect(() => {
      const error = this.store.hata();
      if (error) this.session.handleSessionLoss(error);
    });
  }

  /** Server values go into UNTOUCHED fields only (first load: all; after 409: the user's typing is kept). */
  private mergeInfo(d: PlatformTenantDetail): void {
    const server = infoFromDetail(d);
    for (const key of INFO_KEYS) {
      const control = this.info.controls[key];
      if (!control.dirty) control.setValue(server[key], { emitEvent: false });
    }
  }

  protected saveInfo(): void {
    const d = this.detail();
    if (!d) return;
    const body = updateBody(this.info.getRawValue(), d.surum);
    this.infoSubmit.gonder(this.info, () => this.api.put<PlatformTenantDetail>(this.root, body), {
      basarili: () => {
        this.toast.basari(this.t('platform.islemTamam'));
        this.store.yenile();
      },
      hata: (e) => {
        if (this.session.handleSessionLoss(e)) return;
        // Stale version: reload; untouched fields take the new values, the user re-saves.
        if (e.kod === 'cakisma') this.store.yenile();
      },
    });
  }

  protected closeTenant(): void {
    const code = this.closeForm.getRawValue().onayKod ?? '';
    this.closeSubmit.gonder(
      this.closeForm,
      () =>
        this.api.post<PlatformTenantDetail>(`${this.root}/durum`, {
          durum: 'Kapali',
          onayKod: code.trim(),
        }),
      {
        basarili: () => {
          this.closeForm.reset();
          this.toast.basari(this.t('platform.detay.kapatildi'));
          this.store.yenile();
        },
        hata: (e) => this.session.handleSessionLoss(e),
      },
    );
  }

  protected async toggleStatus(): Promise<void> {
    const d = this.detail();
    const target = this.toggle();
    if (!d || target === null) return;
    const suspend = target === 'Pasif';
    await this.mutate(
      {
        baslik: suspend
          ? 'platform.durumDegisimi.pasifBaslik'
          : 'platform.durumDegisimi.aktifBaslik',
        mesaj: suspend ? 'platform.durumDegisimi.pasifMesaj' : 'platform.durumDegisimi.aktifMesaj',
        tehlikeli: suspend,
      },
      () => this.api.post(`${this.root}/durum`, { durum: target, onayKod: null }),
    );
  }

  protected async reopen(): Promise<void> {
    await this.mutate(
      { baslik: 'platform.detay.yenidenAcBaslik', mesaj: 'platform.detay.yenidenAcMesaj' },
      () => this.api.post(`${this.root}/durum`, { durum: 'Aktif', onayKod: null }),
    );
  }

  protected async setWebSiteModule(on: boolean): Promise<void> {
    await this.mutate(
      on
        ? { baslik: 'platform.detay.modulAcBaslik', mesaj: 'platform.detay.modulAcMesaj' }
        : {
            baslik: 'platform.detay.modulKapatBaslik',
            mesaj: 'platform.detay.modulKapatMesaj',
            tehlikeli: true,
          },
      () => this.api.post(`${this.root}/web-sitesi-modulu`, { aktif: on }),
    );
  }

  protected async setPilot(on: boolean): Promise<void> {
    await this.mutate(
      on
        ? { baslik: 'platform.detay.pilotAcBaslik', mesaj: 'platform.detay.pilotAcMesaj' }
        : {
            baslik: 'platform.detay.pilotKapatBaslik',
            mesaj: 'platform.detay.pilotKapatMesaj',
            tehlikeli: true,
          },
      () => this.api.post(`${this.root}/yeni-arayuz-pilot`, { aktif: on }),
    );
  }

  protected async deleteLogo(): Promise<void> {
    const done = await this.mutate(
      {
        baslik: 'platform.detay.logoSilBaslik',
        mesaj: 'platform.detay.logoSilMesaj',
        tehlikeli: true,
      },
      () => this.api.delete(`${this.root}/logo`),
    );
    if (done) this.logoVersion.update((v) => v + 1);
  }

  protected pickLogo(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.logoFile.set(input.files?.[0] ?? null);
    this.logoError.set(null);
  }

  protected async uploadLogo(input: HTMLInputElement): Promise<void> {
    const file = this.logoFile();
    if (this.busy()) return;
    if (!file) {
      this.logoError.set(this.t('platform.detay.logoSecilmedi'));
      return;
    }
    const form = new FormData();
    form.append('logo', file, file.name);
    this.busy.set(true);
    this.logoError.set(null);
    try {
      await firstValueFrom(this.api.post(`${this.root}/logo`, form));
      input.value = '';
      this.logoFile.set(null);
      this.logoVersion.update((v) => v + 1);
      this.toast.basari(this.t('platform.islemTamam'));
      this.store.yenile();
    } catch (e: unknown) {
      if (!this.session.handleSessionLoss(e)) {
        const error = toApiError(e);
        if (error.kod === 'dogrulama' || error.kod === 'bilinmeyen') {
          this.logoError.set(error.alanlar?.['logo']?.[0] ?? error.detay);
        }
      }
    } finally {
      this.busy.set(false);
    }
  }

  /** Confirm → call → toast + reload. Validation errors of switches go to a toast (no form field). */
  private async mutate(
    question: { baslik: CeviriAnahtari; mesaj: CeviriAnahtari; tehlikeli?: boolean },
    call: () => Observable<unknown>,
  ): Promise<boolean> {
    const d = this.detail();
    if (!d || this.busy()) return false;
    const ok = await this.confirm.ask({
      baslik: this.t(question.baslik),
      mesaj: this.t(question.mesaj, { kod: d.kod }),
      tehlikeli: question.tehlikeli ?? false,
    });
    if (!ok) return false;
    this.busy.set(true);
    try {
      await firstValueFrom(call());
      this.toast.basari(this.t('platform.islemTamam'));
      this.store.yenile();
      return true;
    } catch (e: unknown) {
      if (!this.session.handleSessionLoss(e)) {
        const error = toApiError(e);
        if (error.kod === 'dogrulama' || error.kod === 'bilinmeyen') this.toast.hata(error.detay);
      }
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
