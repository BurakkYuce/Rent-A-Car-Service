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
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { sekmeBaglami } from '@core/sekme/sekme-durumu';
import { FetchPolicy } from '@core/veri/fetch-policy';
import { ParaPipe, SayiPipe, TarihPipe, TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { Ikon } from '@shared/ikon/ikon';

import { SCORECARD_ROLES } from '../vehicle-guards';
import { STATUS_BADGE, toNumber, vehicleStatus } from '../vehicle-model';
import { VehicleDetailStore } from '../vehicle.store';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
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
    SayfaBandi,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    ParaPipe,
    SayiGirdisi,
    SayiPipe,
    TarihPipe,
    TarihSaatPipe,
  ],
  providers: [FetchPolicy, VehicleDetailStore],
  templateUrl: './vehicle-detail.html',
  styleUrl: '../vehicle-screens.scss',
})
export class VehicleDetail {
  protected readonly store = inject(VehicleDetailStore);
  private readonly api = inject(ApiIstemcisi);
  private readonly session = inject(OturumServisi);
  private readonly toast = inject(ToastServisi);
  private readonly tab = sekmeBaglami();
  private readonly t = ceviriFonksiyonu();
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
  protected readonly submission = formGonderimi();

  constructor() {
    inject(FetchPolicy).baglan({
      parametre: signal(this.id).asReadonly(),
      yukle: (id) => this.store.detail.yukle(id),
      sifirla: () => this.store.detail.sifirla(),
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
