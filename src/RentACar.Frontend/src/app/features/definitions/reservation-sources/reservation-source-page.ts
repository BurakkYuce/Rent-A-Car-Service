import { ChangeDetectionStrategy, Component, computed, inject, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { Secim } from '@shared/form/kontroller/secim';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { TanimCrud } from '@shared/form/tanim-crud/tanim-crud';
import { restTanimKaynagi } from '@shared/form/tanim-crud/tanim-kaynagi';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { reservationSourceFields } from './reservation-source-fields';

type ReflectResult = Sema<'ReflectRatesResult'>;

const ROOT: ApiYolu = '/api/ui/v1/rezervasyon-kaynaklari';

/**
 * F11.2c rezervasyon kaynakları (Blazor `ReservationSourceList`, OperationsWrite): genel tanım CRUD'u (panel) +
 * "Aşağıya Yansıt" (seçili kaynağın kira/hizmet/drop oranları diğer AKTİF kaynaklara kopyalanır; yalnız kaynak
 * tablosu — kayıtlı rezervasyon/fatura değişmez). Oran ve bilgi alanlarının hesaba GİRMEDİĞİ ekranda yazılı.
 */
@Component({
  selector: 'rc-reservation-source-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, TanimCrud, Alan, FormHatalari, Secim, SayfaBandi],
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
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();
  private readonly crud = viewChild(TanimCrud);

  protected readonly fields = reservationSourceFields(this.t);
  protected readonly source = restTanimKaynagi(ROOT);
  protected readonly sourceOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.crud()?.rows() ?? []).map((s) => ({
      deger: s.id,
      etiket: `${String(s['kod'] ?? '')} — ${String(s['ad'] ?? '')}`,
    })),
  );

  protected readonly reflectForm = new FormGroup({
    kaynakId: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly reflectSubmit = formGonderimi();

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.crud()?.kaydedilmemisDegisiklikVar() ?? false;
  }

  protected async reflect(): Promise<void> {
    const id = this.reflectForm.getRawValue().kaynakId;
    if (!id) {
      this.reflectForm.markAllAsTouched();
      return;
    }
    const yes = await this.confirm.sor({
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
