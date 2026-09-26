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

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { tabContext } from '@core/sekme/tab-state';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { MoneyPipe, NumberPipe, DatePipe, DateTimePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { NumberInput } from '@shared/form/kontroller/number-input';
import { Icon } from '@shared/ikon/icon';

import { SCORECARD_ROLES } from '../vehicle-guards';
import { STATUS_BADGE, toNumber, vehicleStatus } from '../vehicle-model';
import { VehicleDetailStore } from '../vehicle.store';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { PlateChipComponent } from '@shared/plaka/plaka';

/** KM kaydı kaynağı → etiket anahtarı (Blazor: Kira dönüşü / Servis çıkışı / Manuel). */
export function kmSourceKey(
  source: string,
): 'arac.km.kaynak.Donus' | 'arac.km.kaynak.Servis' | 'arac.km.kaynak.Manuel' {
  if (source === 'Donus') return 'arac.km.kaynak.Donus';
  if (source === 'Servis') return 'arac.km.kaynak.Servis';
  return 'arac.km.kaynak.Manuel';
}

/**
 * Araç detayı (`/app/araclar/:id/detay`) — Blazor `VehicleDetail.razor` paritesi: başlık (grup, şube, durum,
 * KM), kira / servis / ceza / hasar geçmişi, KM zaman serisi (son 10) + manuel KM girişi (OperationsWrite;
 * odometre geriye gidemez — sunucu kuralı). Ön muhasebe karnesi bağlantısı finans rollerine.
 */
@Component({
  selector: 'rc-vehicle-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PlateChipComponent,
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormErrors,
    Icon,
    MoneyPipe,
    NumberInput,
    NumberPipe,
    DatePipe,
    DateTimePipe,
  ],
  providers: [FetchPolicy, VehicleDetailStore],
  templateUrl: './vehicle-detail.html',
  styleUrl: '../vehicle-screens.scss',
})
export class VehicleDetail {
  protected readonly store = inject(VehicleDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(SessionService);
  private readonly toast = inject(ToastService);
  private readonly tab = tabContext();
  private readonly t = translationFunction();
  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly detail = computed(() => this.store.detail.veri() ?? null);
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly canScorecard = computed(() =>
    SCORECARD_ROLES.includes(this.session.ben()?.rol ?? ''),
  );
  protected readonly num = toNumber;
  protected readonly kmSourceKey = kmSourceKey;

  protected readonly kmForm = new FormGroup({
    km: new FormControl<number | null>(null, [Validators.required, Validators.min(0)]),
  });
  protected readonly submission = formSubmission();

  constructor() {
    inject(FetchPolicy).connect({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.reset(),
      sekmeyeDonunce: 'yenile',
    });
    effect(() => {
      const d = this.detail();
      if (d) this.tab.etiketAyarla(this.t('arac.detaySayfasi.sekmeEtiketi', { plaka: d.plaka }));
    });
  }

  protected badge(status: string): string {
    const s = vehicleStatus(status);
    return `rc-rozet ${s ? STATUS_BADGE[s] : ''}`;
  }

  protected statusLabel(status: string): string {
    const s = vehicleStatus(status);
    return s ? this.t(`arac.durumlar.${s}`) : status;
  }

  protected enterKm(): void {
    const km = this.kmForm.controls.km.value;
    this.submission.gonder(
      this.kmForm,
      (key) =>
        this.api.post<null>(
          `/api/ui/v1/araclar/${encodeURIComponent(this.id)}/km`,
          { km, tarih: null },
          { islemAnahtari: key },
        ),
      {
        basarili: () => {
          this.toast.basari(this.t('arac.km.kaydedildi'));
          this.kmForm.reset({ km: null });
          this.store.detail.yenile();
        },
      },
    );
  }
}
