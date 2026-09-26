import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { EN_FAZLA_BOYUT, type Sayfa } from '@core/api/sayfa';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { SayiPipe, TarihPipe, TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Secim } from '@shared/form/kontroller/secim';
import { Ikon } from '@shared/ikon/ikon';

import {
  PLATFORM_API,
  type PlatformTenantDetail,
  type PlatformTenantRow,
  TENANT_STATUSES,
  type TenantStatus,
  tenantStatus,
  tenantStatusBadge,
  toggleTarget,
  toNumber,
} from '../platform-model';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { PlatformSessionService } from '../platform-session';

interface ListQuery {
  readonly q: string | null;
  readonly durum: TenantStatus | null;
}

/**
 * `/app/platform/kiracilar` — tenant console (Blazor `TenantConsole` parity): create a tenant with its
 * first Admin user, list every tenant (code, name, status, users, vehicles, active rentals, last login,
 * created) with detail link and the confirmed suspend/resume toggle. Closed tenants have no toggle (they
 * are reopened from the detail page). Adds code/name search and a status filter (server side).
 */
@Component({
  selector: 'rc-tenant-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    Secim,
    SayfaBandi,
    SayiPipe,
    TarihPipe,
    TarihSaatPipe,
  ],
  templateUrl: './tenant-list-page.html',
  styleUrls: ['../platform-page.scss'],
})
export class TenantListPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly session = inject(PlatformSessionService);
  private readonly t = ceviriFonksiyonu();

  protected readonly list = new TemelStore(
    (p: ListQuery) =>
      this.api.get<Sayfa<PlatformTenantRow>>(`${PLATFORM_API}/kiracilar`, {
        parametreler: { q: p.q, durum: p.durum, boyut: EN_FAZLA_BOYUT, sirala: 'kod' },
      }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly rows = computed(() => this.list.veri()?.kayitlar ?? []);
  /** Server total beyond the first page (platform scale is small; shown as a note, not paged). */
  protected readonly truncated = computed(() => {
    const page = this.list.veri();
    return page !== undefined && page.toplam > page.kayitlar.length;
  });

  protected readonly filter = new FormGroup({
    q: new FormControl<string | null>(null),
    durum: new FormControl<TenantStatus | null>(null),
  });
  protected readonly statusOptions: readonly SecenekOgesi<TenantStatus>[] = TENANT_STATUSES.map(
    (s) => ({ deger: s, etiket: this.t(`platform.durum.${s}`) }),
  );

  protected readonly createOpen = signal(false);
  protected readonly createForm = new FormGroup({
    kod: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(64)]),
    ad: new FormControl<string | null>(null, Validators.required),
    adminKullanici: new FormControl<string | null>(null, Validators.required),
    adminSifre: new FormControl<string | null>(null, [
      Validators.required,
      Validators.minLength(6),
    ]),
  });
  protected readonly create = formGonderimi();
  /** Row being toggled (double-click guard per row). */
  protected readonly busyId = signal<string | null>(null);

  protected readonly status = tenantStatus;
  protected readonly badge = tenantStatusBadge;
  protected readonly toggle = toggleTarget;
  protected readonly num = toNumber;

  constructor() {
    this.load();
    // Blazor opened the create panel when there were no tenants yet.
    effect(() => {
      const d = this.list.durum();
      if (d.tur === 'hazir' && d.veri.toplam === 0 && !this.hasFilter()) this.createOpen.set(true);
    });
    effect(() => {
      const error = this.list.hata();
      if (error) this.session.handleSessionLoss(error);
    });
  }

  private hasFilter(): boolean {
    const v = this.filter.getRawValue();
    return !!(v.q ?? '').trim() || v.durum !== null;
  }

  protected load(): void {
    const v = this.filter.getRawValue();
    this.list.yukle({ q: (v.q ?? '').trim() || null, durum: v.durum });
  }

  protected clearFilter(): void {
    this.filter.reset({ q: null, durum: null });
    this.load();
  }

  protected submitCreate(): void {
    const v = this.createForm.getRawValue();
    this.create.gonder(
      this.createForm,
      (key) =>
        this.api.post<PlatformTenantDetail>(
          `${PLATFORM_API}/kiracilar`,
          {
            kod: (v.kod ?? '').trim(),
            ad: (v.ad ?? '').trim(),
            adminKullanici: (v.adminKullanici ?? '').trim(),
            adminSifre: v.adminSifre ?? '',
          },
          { islemAnahtari: key },
        ),
      {
        basarili: (created) => {
          this.toast.basari(this.t('platform.firmalar.olusturuldu', { kod: created.kod }));
          this.createForm.reset();
          void this.router.navigate(['/platform/kiracilar', created.id]);
        },
        hata: (e) => this.session.handleSessionLoss(e),
      },
    );
  }

  protected async toggleStatus(row: PlatformTenantRow): Promise<void> {
    const target = toggleTarget(tenantStatus(row.durum));
    if (target === null || this.busyId() !== null) return;
    const ok = await this.confirm.sor({
      baslik: this.t(
        target === 'Pasif'
          ? 'platform.durumDegisimi.pasifBaslik'
          : 'platform.durumDegisimi.aktifBaslik',
      ),
      mesaj: this.t(
        target === 'Pasif'
          ? 'platform.durumDegisimi.pasifMesaj'
          : 'platform.durumDegisimi.aktifMesaj',
        { kod: row.kod },
      ),
      tehlikeli: target === 'Pasif',
    });
    if (!ok) return;
    this.busyId.set(row.id);
    try {
      await firstValueFrom(
        this.api.post<PlatformTenantDetail>(`${PLATFORM_API}/kiracilar/${row.id}/durum`, {
          durum: target,
          onayKod: null,
        }),
      );
      this.toast.basari(this.t('platform.islemTamam'));
      this.list.yenile();
    } catch (e: unknown) {
      if (!this.session.handleSessionLoss(e)) {
        const error = apiHatasinaCevir(e);
        if (error.kod === 'dogrulama' || error.kod === 'bilinmeyen') this.toast.hata(error.detay);
      }
    } finally {
      this.busyId.set(null);
    }
  }
}
