import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { Selection } from '@shared/form/kontroller/selection';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import { restDefinitionSource } from '@shared/form/tanim-crud/definition-source';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { reservationSourceFields } from './reservation-source-fields';

type ReflectResult = Schema<'ReflectRatesResult'>;

const ROOT: ApiPath = '/api/ui/v1/rezervasyon-kaynaklari';

/**
 * F11.2c rezervasyon kaynakları (Blazor `ReservationSourceList`, OperationsWrite): genel tanım CRUD'u (panel) +
 * "Aşağıya Yansıt" (seçili kaynağın kira/hizmet/drop oranları diğer AKTİF kaynaklara kopyalanır; yalnız kaynak
 * tablosu — kayıtlı rezervasyon/fatura değişmez). Oran ve bilgi alanlarının hesaba GİRMEDİĞİ ekranda yazılı.
 */
@Component({
  selector: 'rc-reservation-source-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    DefinitionCrud,
    Alan,
    FormErrors,
    Selection,
    PageBand,
  ],
  styleUrl: '../definitions.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'tanimlar.reservationSource.baslik' | transloco" ikon="inbox" />
    <div class="rc-sayfa">
      <p class="not">{{ 'tanimlar.reservationSource.aciklama' | transloco }}</p>
      <p class="not">
        <strong>{{ 'tanimlar.reservationSource.oranNotu' | transloco }}</strong>
      </p>
      <p class="not">{{ 'tanimlar.reservationSource.kuralNotu' | transloco }}</p>
      <rc-tanim-crud
        layout="panel"
        [baslik]="'tanimlar.reservationSource.tablo' | transloco"
        [alanlar]="fields"
        [kaynak]="source"
      />

      <section class="rc-bolum" aria-labelledby="rk-yansit-baslik">
        <h2 id="rk-yansit-baslik">{{ 'tanimlar.reservationSource.yansit.baslik' | transloco }}</h2>
        <p class="not">{{ 'tanimlar.reservationSource.yansit.aciklama' | transloco }}</p>
        <rc-form-hatalari [hatalar]="reflectSubmit.genelHatalar()" />
        <div class="rc-form-izgara" [formGroup]="reflectForm">
          <rc-alan [etiket]="'tanimlar.reservationSource.yansit.kaynak' | transloco">
            <rc-secim formControlName="kaynakId" [secenekler]="sourceOptions()" />
          </rc-alan>
          <div class="eylemler">
            <button
              type="button"
              class="rc-dugme"
              [disabled]="reflectSubmit.gonderiliyor()"
              (click)="reflect()"
            >
              {{ 'tanimlar.reservationSource.yansit.dugme' | transloco }}
            </button>
          </div>
        </div>
      </section>
    </div>
  `,
})
export class ReservationSourcePage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();
  private readonly crud = viewChild(DefinitionCrud);

  protected readonly fields = reservationSourceFields(this.t);
  protected readonly source = restDefinitionSource(ROOT);
  protected readonly sourceOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.crud()?.rows() ?? []).map((s) => ({
      deger: s.id,
      etiket: `${String(s['kod'] ?? '')} — ${String(s['ad'] ?? '')}`,
    })),
  );

  protected readonly reflectForm = new FormGroup({
    kaynakId: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly reflectSubmit = formSubmission();

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.crud()?.hasUnsavedChanges() ?? false;
  }

  protected async reflect(): Promise<void> {
    const id = this.reflectForm.getRawValue().kaynakId;
    if (!id) {
      this.reflectForm.markAllAsTouched();
      return;
    }
    const yes = await this.confirm.ask({
      baslik: this.t('tanimlar.reservationSource.yansit.onayBaslik'),
      mesaj: this.t('tanimlar.reservationSource.yansit.onayMesaj'),
      onayEtiketi: this.t('tanimlar.reservationSource.yansit.dugme'),
    });
    if (!yes) return;
    this.reflectSubmit.gonder(
      this.reflectForm,
      (key) =>
        this.api.post<ReflectResult>(
          `${ROOT}/${encodeURIComponent(id)}/yansit`,
          {},
          { islemAnahtari: key },
        ),
      {
        basarili: (r) => {
          this.toast.basari(
            this.t('tanimlar.reservationSource.yansit.tamam', { sayi: String(r.guncellenen) }),
          );
          this.crud()?.reload();
        },
      },
    );
  }
}
