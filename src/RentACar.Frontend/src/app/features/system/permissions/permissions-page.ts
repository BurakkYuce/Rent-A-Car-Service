import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { IZINLER } from '@core/oturum/oturum-tipleri';
import { TemelStore } from '@core/veri/temel-store';
import { TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { Secim } from '@shared/form/kontroller/secim';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { ROLES } from '../users/users-model';

type ScreenPermission = Sema<'ScreenPermissionDto'>;
type PermissionGroup = Sema<'PermissionGroupDto'>;
type RolePermissions = Sema<'RolePermissionsDto'>;
type CountResult = Sema<'CountResult'>;

const ROOT = '/api/ui/v1/yetki' as const;

/**
 * F11.2b ekran yetkileri (Blazor `Yetki`, ManageUsers): rol-izin matrisinin (salt okunur, floor) üstüne ekran bazlı
 * sıkılaştırma (override), rol kopyalama ve yetki grubu şablonları. Override yetkiyi GENİŞLETEMEZ (sunucu kuralı).
 * Geri alınamaz ya da geniş etkili işlemler (sil, kopyala, şablon uygula/sil) onay sorar.
 */
@Component({
  selector: 'rc-permissions-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    SayfaBandi,
    ReactiveFormsModule,
    TranslocoPipe,
    TarihSaatPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
    OnayKutusu,
    Secim,
  ],
  styleUrl: '../system.scss',
  templateUrl: './permissions-page.html',
})
export class PermissionsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly roles = ROLES;
  protected readonly permissions = IZINLER;
  protected readonly screens = new TemelStore<readonly ScreenPermission[]>(() =>
    this.api.get<readonly ScreenPermission[]>(`${ROOT}/ekranlar`),
  );
  protected readonly groups = new TemelStore<readonly PermissionGroup[]>(() =>
    this.api.get<readonly PermissionGroup[]>(`${ROOT}/gruplar`),
  );
  protected readonly matrix = new TemelStore<readonly RolePermissions[]>(() =>
    this.api.get<readonly RolePermissions[]>(`${ROOT}/matris`),
  );
  protected readonly roleOptions: readonly SecenekOgesi<string>[] = ROLES.map((r) => ({
    deger: r,
    etiket: r,
  }));

  protected readonly overrideForm = new FormGroup({
    ekranKodu: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(64),
    ]),
    aktif: new FormControl<boolean | null>(true),
    Admin: new FormControl<boolean | null>(false),
    Yonetici: new FormControl<boolean | null>(false),
    Operator: new FormControl<boolean | null>(false),
    Muhasebe: new FormControl<boolean | null>(false),
  });
  protected readonly overrideSubmit = formGonderimi();

  protected readonly copyForm = new FormGroup({
    kaynak: new FormControl<string | null>(null, Validators.required),
    hedef: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly copySubmit = formGonderimi();

  protected readonly groupForm = new FormGroup({
    ad: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(128)]),
  });
  protected readonly groupSubmit = formGonderimi();

  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  protected readonly allows = (row: RolePermissions, p: string) => row.izinler.includes(p);
  protected readonly screenRows = computed(() => this.screens.veri() ?? []);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.screens.yukle();
    this.groups.yukle();
    this.matrix.yukle();
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.overrideForm.dirty || this.copyForm.dirty || this.groupForm.dirty;
  }

  /** Mevcut override'ı forma al (güncelleme = aynı ekran koduyla yeniden kaydet). */
  protected edit(s: ScreenPermission): void {
    this.overrideForm.reset({
      ekranKodu: s.ekranKodu,
      aktif: s.aktif,
      ...Object.fromEntries(ROLES.map((r) => [r, s.roller.includes(r)])),
    });
  }

  protected saveOverride(): void {
    const v = this.overrideForm.getRawValue();
    const body = {
      ekranKodu: v.ekranKodu,
      aktif: v.aktif === true,
      roller: ROLES.filter((r) => v[r] === true),
    };
    this.overrideSubmit.gonder(
      this.overrideForm,
      (key) => this.api.post(`${ROOT}/ekranlar`, body, { islemAnahtari: key }),
      {
        basarili: () => {
          this.overrideForm.reset({
            aktif: true,
            Admin: false,
            Yonetici: false,
            Operator: false,
            Muhasebe: false,
          });
          this.toast.basari(this.t('sistem.ortak.kaydedildi'));
          this.screens.yenile();
        },
      },
    );
  }

  protected async removeOverride(s: ScreenPermission): Promise<void> {
    const ok = await this.ask('sistem.yetki.silBaslik', 'sistem.yetki.silMesaj', {
      kod: s.ekranKodu,
    });
    if (ok) {
      this.act(this.api.delete(`${ROOT}/ekranlar/${encodeURIComponent(s.ekranKodu)}`), () =>
        this.screens.yenile(),
      );
    }
  }

  protected async copyRole(): Promise<void> {
    const v = this.copyForm.getRawValue();
    this.copyForm.markAllAsTouched();
    if (this.copyForm.invalid) return;
    const ok = await this.ask('sistem.yetki.kopyalaBaslik', 'sistem.yetki.kopyalaMesaj', {
      kaynak: v.kaynak ?? '',
      hedef: v.hedef ?? '',
    });
    if (!ok) return;
    this.copySubmit.gonder(
      this.copyForm,
      (key) => this.api.post<CountResult>(`${ROOT}/kopyala`, v, { islemAnahtari: key }),
      {
        basarili: (r) => {
          this.copyForm.reset();
          this.toast.basari(this.t('sistem.yetki.kopyalandi', { adet: String(r.adet) }));
          this.screens.yenile();
        },
      },
    );
  }

  protected saveGroup(): void {
    const v = this.groupForm.getRawValue();
    this.groupSubmit.gonder(
      this.groupForm,
      (key) => this.api.post(`${ROOT}/gruplar`, v, { islemAnahtari: key }),
      {
        basarili: () => {
          this.groupForm.reset();
          this.toast.basari(this.t('sistem.yetki.grupKaydedildi'));
          this.groups.yenile();
        },
      },
    );
  }

  protected async applyGroup(g: PermissionGroup): Promise<void> {
    const ok = await this.ask('sistem.yetki.uygulaBaslik', 'sistem.yetki.uygulaMesaj', {
      ad: g.ad,
    });
    if (!ok) return;
    this.act(this.api.post<CountResult>(`${ROOT}/gruplar/uygula`, { ad: g.ad }), () =>
      this.screens.yenile(),
    );
  }

  protected async removeGroup(g: PermissionGroup): Promise<void> {
    const ok = await this.ask('sistem.yetki.grupSilBaslik', 'sistem.yetki.grupSilMesaj', {
      ad: g.ad,
    });
    if (!ok) return;
    this.act(this.api.delete(`${ROOT}/gruplar`, { parametreler: { ad: g.ad } }), () =>
      this.groups.yenile(),
    );
  }

  private ask(
    title:
      | 'sistem.yetki.silBaslik'
      | 'sistem.yetki.kopyalaBaslik'
      | 'sistem.yetki.uygulaBaslik'
      | 'sistem.yetki.grupSilBaslik',
    message:
      | 'sistem.yetki.silMesaj'
      | 'sistem.yetki.kopyalaMesaj'
      | 'sistem.yetki.uygulaMesaj'
      | 'sistem.yetki.grupSilMesaj',
    params: Record<string, string>,
  ): Promise<boolean> {
    return this.confirm.sor({
      baslik: this.t(title, params),
      mesaj: this.t(message, params),
      onayEtiketi: this.t('sistem.yetki.onayla'),
      tehlikeli: true,
    });
  }

  private act(request: Observable<unknown>, after: () => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.basari(this.t('sistem.ortak.kaydedildi'));
        after();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.actionError.set(apiHatasinaCevir(e).detay);
      },
    });
  }
}
