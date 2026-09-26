import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { type UnsavedChangesOwner, pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { MoneyPipe, NumberPipe, DatePipe } from '@shared/bicim/bicim-pipe';
import { Icon } from '@shared/ikon/icon';
import { PlateChipComponent } from '@shared/plaka/plaka';

import { mergeServerValues } from '@features/planlama-ortak/form-yardimcilari';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';
import { FleetIdentityFields, profileControls } from './fleet-identity-fields';
import {
  type FleetRental,
  type FiloKunyeDegeri,
  fleetStatus,
  profileValues,
  profileBody,
  count,
} from './filo-modeli';
import { FleetDetailStore } from './filo.store';

/**
 * Filo sözleşmesi detayı (`/app/filo-kiralama/:id`): sözleşme özeti + SUNUCUNUN taksit planı (salt-hesap,
 * deftere yazmaz), künye düzenleme (Blazor "Künye" formu; para/süre alanları yok), Tamamla ve İptal
 * (OperationsDelete; sunucunun `yetkiler`'ine göre görünür). Künye PUT'u tam değiştirmedir (`surum`); bayat
 * sürüm 409 `cakisma` → güncel kayıt kirli forma birleştirilir, form silinmez.
 */
@Component({
  selector: 'rc-filo-detay',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    FleetIdentityFields,
    FormErrors,
    Icon,
    MoneyPipe,
    NumberPipe,
    DatePipe,
    PlateChipComponent,
    PageBand,
  ],
  providers: [FetchPolicy, FleetDetailStore],
  templateUrl: './fleet-detail.html',
  styleUrl: './filo.scss',
})
export class FleetDetail implements UnsavedChangesOwner {
  protected readonly store = inject(FleetDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly approval = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly bant = inject(WarningBannerService);
  private readonly teardown = inject(DestroyRef);
  private readonly sekme = tabContext();
  private readonly t = translationFunction();
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly form = new FormGroup(profileControls());
  protected readonly submission = formSubmission();
  protected readonly busy = signal(false);
  /** Son okunan sunucu hâli: `surum` PUT'a gider, birleştirmenin tabanıdır. */
  protected readonly taban = signal<FleetRental | null>(null);

  protected readonly record = computed(() => this.store.detay.veri() ?? null);
  protected readonly durum = computed(() => {
    const k = this.record();
    return k ? fleetStatus(k.durum) : null;
  });
  protected readonly count = count;

  constructor() {
    inject(FetchPolicy).connect({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detay.yukle(id),
      sifirla: () => this.store.detay.reset(),
      // Başka oturum künyeyi değiştirmiş ya da sözleşmeyi kapatmış olabilir: dönüşte taze kayıt (kirli form korunur).
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const d = this.store.detay.durum();
      if (d.tur === 'hazir') untracked(() => this.detailLoaded(d.veri));
    });
    pageLeaveGuard(() => this.form.dirty);
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected statusLabel(): string {
    const d = this.durum();
    return d ? this.t(`filoKiralama.durumlar.${d}`) : (this.record()?.durum ?? '');
  }

  protected vatPercent(k: FleetRental): number | null {
    const o = count(k.kdvOrani);
    return o === null ? null : o * 100;
  }

  protected kaydet(): void {
    const floor = this.taban();
    if (floor === null || this.store.detay.isLoading()) return;
    const body = profileBody(this.form.getRawValue() as FiloKunyeDegeri, floor);
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.put<FleetRental>(
          `/api/ui/v1/filo-kiralama/${encodeURIComponent(this.id)}/kunye`,
          body,
          { islemAnahtari: key },
        ),
      {
        basarili: () => {
          this.toast.basari(this.t('filoKiralama.kunyeKaydedildi'));
          this.store.detay.yenile();
        },
        // Bayat sürüm: güncel kayıt okunur, kirli forma birleştirilir (yeniden gönderim YOK).
        hata: (h) => {
          if (h.kod === 'cakisma') this.store.detay.yenile();
        },
      },
    );
  }

  protected tamamla(): void {
    this.islem('tamamla', 'filoKiralama.tamamlandi');
  }

  protected async iptal(): Promise<void> {
    const k = this.record();
    if (!k || this.busy()) return;
    const yes = await this.approval.ask({
      baslik: this.t('filoKiralama.iptalBaslik'),
      mesaj: this.t('filoKiralama.iptalMesaj', { no: k.no }),
      onayEtiketi: this.t('filoKiralama.iptal'),
      tehlikeli: true,
    });
    if (yes) this.islem('iptal', 'filoKiralama.iptalEdildi');
  }

  private islem(
    path: 'tamamla' | 'iptal',
    notification: 'filoKiralama.tamamlandi' | 'filoKiralama.iptalEdildi',
  ): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.api
      .post<FleetRental>(`/api/ui/v1/filo-kiralama/${encodeURIComponent(this.id)}/${path}`, null)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.teardown),
      )
      .subscribe({
        next: () => {
          this.toast.basari(this.t(notification));
          this.store.detay.yenile();
        },
        error: (raw: unknown) => {
          const error = toApiError(raw);
          if (!genelGosterilir(error)) this.toast.hata(error.detay);
          this.store.detay.yenile();
        },
      });
  }

  /** Temiz form sunucu hâline sıfırlanır; kirli formda dokunulan alanlar korunur (çakışan işaretlenir). */
  private detailLoaded(k: FleetRental): void {
    this.sekme.etiketAyarla(this.t('filoKiralama.sekmeEtiketi', { no: k.no }));
    const newItem = profileValues(k);
    const old = this.taban();
    if (!this.form.dirty || old === null) {
      this.form.reset({ ...newItem });
    } else {
      const conflicting = mergeServerValues(
        this.form,
        { ...newItem },
        { ...profileValues(old) },
        this.t('filoKiralama.cakismaAlan'),
      );
      if (conflicting.length > 0) {
        this.bant.show({
          tur: 'uyari',
          mesaj: this.t('filoKiralama.cakismaBant', { sayi: conflicting.length }),
          kod: 'cakisma',
        });
      }
    }
    this.taban.set(k);
    if (k.yetkiler.kunye) this.form.enable({ emitEvent: false });
    else this.form.disable({ emitEvent: false });
  }
}
