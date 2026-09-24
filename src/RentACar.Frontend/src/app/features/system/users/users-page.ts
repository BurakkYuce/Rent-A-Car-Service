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
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemelStore } from '@core/veri/temel-store';
import { TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { Secim } from '@shared/form/kontroller/secim';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';

import {
  type ExceptionRow,
  type UserDto,
  canManageAccount,
  creatableRoles,
  exceptionRows,
  exceptionTargets,
  grantablePermissions,
} from './users-model';

const ROOT = '/api/ui/v1/kullanicilar' as const;
const PASSWORD_MAX = 128;

/**
 * F11.2b kullanıcı yönetimi (Blazor `UserList`, ManageUsers): liste, oluştur, aktif/pasif, parola sıfırla, kullanıcı
 * bazlı izin istisnaları. Admin hesabına dokunan her düğme yalnız Admin rolüne görünür (M2; sunucu da 403 verir).
 * Parolalar yalnız bellekte, `autocomplete="new-password"`; işlem sonrası formdan silinir.
 */
@Component({
  selector: 'rc-users-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    TarihPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
    Secim,
  ],
  styleUrl: '../system.scss',
  templateUrl: './users-page.html',
})
export class UsersPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly session = inject(OturumServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly actorId = computed(() => this.session.ben()?.kullanici.id ?? null);
  protected readonly actorRole = computed(() => this.session.ben()?.rol ?? null);

  protected readonly list = new TemelStore<readonly UserDto[]>(() =>
    this.api.get<readonly UserDto[]>(ROOT),
  );
  protected readonly users = computed(() => this.list.veri() ?? []);
  protected readonly exceptions = computed<readonly ExceptionRow[]>(() =>
    exceptionRows(this.users(), this.actorId(), this.actorRole()),
  );
  protected readonly branches = signal<readonly SecenekOgesi<string>[]>([]);
  protected readonly roleOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    creatableRoles(this.actorRole()).map((r) => ({ deger: r, etiket: this.roleLabel(r) })),
  );
  protected readonly permissionOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    grantablePermissions(this.actorRole()).map((p) => ({ deger: p, etiket: p })),
  );
  protected readonly targetOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    exceptionTargets(this.users(), this.actorId(), this.actorRole()).map((u) => ({
      deger: u.id,
      etiket: `${u.kullaniciAdi} (${this.roleLabel(u.rol)})`,
    })),
  );
  protected readonly kindOptions: readonly SecenekOgesi<boolean>[] = [
    { deger: true, etiket: this.t('sistem.kullanici.istisna.ver') },
    { deger: false, etiket: this.t('sistem.kullanici.istisna.yasak') },
  ];

  protected readonly createForm = new FormGroup({
    kullaniciAdi: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(128),
    ]),
    gorunenAd: new FormControl<string | null>(null, Validators.maxLength(256)),
    rol: new FormControl<string | null>('Operator', Validators.required),
    atanmisSube: new FormControl<string | null>(null),
    sifre: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(PASSWORD_MAX),
    ]),
  });
  protected readonly createSubmit = formGonderimi();

  /** Parola sıfırlanan kullanıcı (satır altında açılan form). */
  protected readonly resetting = signal<UserDto | null>(null);
  protected readonly resetForm = new FormGroup({
    sifre: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(PASSWORD_MAX),
    ]),
  });
  protected readonly resetSubmit = formGonderimi();

  protected readonly exceptionForm = new FormGroup({
    kullaniciId: new FormControl<string | null>(null, Validators.required),
    izin: new FormControl<string | null>(null, Validators.required),
    ver: new FormControl<boolean | null>(true, Validators.required),
  });
  protected readonly exceptionSubmit = formGonderimi();

  protected readonly busy = signal<string | null>(null);
  protected readonly rowError = signal<string | null>(null);
  protected readonly canManage = (u: UserDto) => canManageAccount(this.actorRole(), u);

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.list.yukle();
    this.api
      .get<readonly { ad?: unknown; aktif?: unknown }[]>('/api/ui/v1/subeler')
      .pipe(
        map((rows) =>
          rows
            .filter((b) => b.aktif === true && typeof b.ad === 'string')
            .map((b) => ({ deger: b.ad as string, etiket: b.ad as string })),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (l) => this.branches.set(l), error: () => this.branches.set([]) });
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.createForm.dirty || this.resetForm.dirty || this.exceptionForm.dirty;
  }

  protected roleLabel(role: string): string {
    const known = ['Admin', 'Yonetici', 'Operator', 'Muhasebe'];
    return known.includes(role) ? this.t(`sistem.kullanici.rol.${role as 'Admin'}`) : role;
  }

  protected create(): void {
    const v = this.createForm.getRawValue();
    this.createSubmit.gonder(
      this.createForm,
      (key) => this.api.post<UserDto>(ROOT, v, { islemAnahtari: key }),
      {
        basarili: () => {
          this.createForm.reset({ rol: 'Operator' });
          this.toast.basari(this.t('sistem.kullanici.olusturuldu'));
          this.list.yenile();
        },
      },
    );
  }

  protected async toggleActive(u: UserDto): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t(
        u.aktif ? 'sistem.kullanici.pasiflestirBaslik' : 'sistem.kullanici.aktiflestirBaslik',
        {
          ad: u.kullaniciAdi,
        },
      ),
      mesaj: this.t(
        u.aktif ? 'sistem.kullanici.pasiflestirMesaj' : 'sistem.kullanici.aktiflestirMesaj',
      ),
      onayEtiketi: this.t(
        u.aktif ? 'sistem.kullanici.pasiflestir' : 'sistem.kullanici.aktiflestir',
      ),
      tehlikeli: u.aktif,
    });
    if (!yes) return;
    this.busy.set(u.id);
    this.rowError.set(null);
    this.api
      .post<UserDto>(`${ROOT}/${encodeURIComponent(u.id)}/aktif`, { aktif: !u.aktif })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(null);
          this.toast.basari(this.t('sistem.ortak.kaydedildi'));
          this.list.yenile();
        },
        error: (e: unknown) => {
          this.busy.set(null);
          this.rowError.set(apiHatasinaCevir(e).detay);
        },
      });
  }

  protected openReset(u: UserDto): void {
    this.resetForm.reset();
    this.resetSubmit.kilit.yenile();
    this.resetting.set(u);
  }

  protected resetPassword(): void {
    const u = this.resetting();
    if (!u) return;
    const v = this.resetForm.getRawValue();
    this.resetSubmit.gonder(
      this.resetForm,
      (key) =>
        this.api.post(`${ROOT}/${encodeURIComponent(u.id)}/sifre`, v, { islemAnahtari: key }),
      {
        basarili: () => {
          this.resetForm.reset();
          this.resetting.set(null);
          this.toast.basari(this.t('sistem.kullanici.parolaSifirlandi', { ad: u.kullaniciAdi }));
        },
      },
    );
  }

  protected saveException(): void {
    const v = this.exceptionForm.getRawValue();
    this.exceptionSubmit.gonder(
      this.exceptionForm,
      (key) =>
        this.api.put<UserDto>(
          `${ROOT}/${encodeURIComponent(v.kullaniciId ?? '')}/istisnalar/${encodeURIComponent(v.izin ?? '')}`,
          { ver: v.ver === true },
          { islemAnahtari: key },
        ),
      {
        basarili: () => {
          this.exceptionForm.reset({ ver: true });
          this.toast.basari(this.t('sistem.kullanici.istisna.kaydedildi'));
          this.list.yenile();
        },
      },
    );
  }

  protected async removeException(x: ExceptionRow): Promise<void> {
    if (this.busy() !== null) return;
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.kullanici.istisna.kaldirBaslik'),
      mesaj: this.t('sistem.kullanici.istisna.kaldirMesaj', { ad: x.kullaniciAdi, izin: x.izin }),
      onayEtiketi: this.t('sistem.kullanici.istisna.kaldir'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.busy.set(`${x.kullaniciId}:${x.izin}`);
    this.rowError.set(null);
    this.api
      .delete(
        `${ROOT}/${encodeURIComponent(x.kullaniciId)}/istisnalar/${encodeURIComponent(x.izin)}`,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busy.set(null);
          this.toast.basari(this.t('sistem.kullanici.istisna.kaldirildi'));
          this.list.yenile();
        },
        error: (e: unknown) => {
          this.busy.set(null);
          this.rowError.set(apiHatasinaCevir(e).detay);
        },
      });
  }
}
