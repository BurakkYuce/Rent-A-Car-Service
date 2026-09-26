import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { NumberInput } from '@shared/form/kontroller/number-input';

type Suggestion = Schema<'ServiceDefinitionSuggestionDto'>;

interface SuggestionRow {
  readonly key: string;
  readonly suggestion: Suggestion;
  readonly form: FormGroup<{
    kod: FormControl<string | null>;
    bakimKm: FormControl<number | null>;
  }>;
}

/** Blazor varsayılanı (öneri kabul formu). */
const DEFAULT_KM = 15000;

/**
 * "Filodan Öneriler" (Blazor `/servis-tanimlari` alt bölümü): filoda VAR ama tanımı OLMAYAN Marka · Tip · Yakıt ·
 * Vites kombinasyonları (`GET /servis-tanimlari/oneriler`, hiçbir şey yazmaz). "Ekle" = normal `POST /servis-tanimlari`
 * (kod + bakım km düzenlenebilir; araç tipi = kombinasyon etiketi). Satır başına AYRI form (`track` kombinasyon):
 * liste yenilenince bir satırın yazılmış değeri başka satıra geçmez.
 */
@Component({
  selector: 'rc-service-definition-suggestions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, TranslocoPipe, Alan, FormErrors, TextInput, NumberInput],
  styleUrl: '../pricing.scss',
  template: `
    <section class="rc-bolum" aria-labelledby="rc-servis-oneri-baslik">
      <h2 id="rc-servis-oneri-baslik">{{ 'fiyatTarife.servisTanimlari.oneriler' | transloco }}</h2>
      <p class="not">{{ 'fiyatTarife.servisTanimlari.oneriAciklama' | transloco }}</p>
      @switch (store.tur()) {
        @case ('hata') {
          <p class="rc-form-mesaji rc-form-mesaji--hata" role="alert">
            {{ 'fiyatTarife.yuklenemedi' | transloco }}
          </p>
        }
        @case ('hazir') {
          @if ((store.veri() ?? []).length === 0) {
            <p class="not">{{ 'fiyatTarife.servisTanimlari.oneriYok' | transloco }}</p>
          }
          @for (r of store.veri() ?? []; track r.key) {
            <form class="satir-form oneri" [formGroup]="r.form" (ngSubmit)="accept(r)">
              <span class="oneri__etiket">
                {{ r.suggestion.etiket }}
                <span class="rozet-sayac">{{
                  'fiyatTarife.servisTanimlari.aracSayisi'
                    | transloco: { adet: r.suggestion.aracSayisi }
                }}</span>
              </span>
              <rc-alan [etiket]="'fiyatTarife.alan.kod' | transloco">
                <rc-metin-girdisi formControlName="kod" [azamiUzunluk]="32" />
              </rc-alan>
              <rc-alan [etiket]="'fiyatTarife.alan.bakimKm' | transloco">
                <rc-sayi-girdisi formControlName="bakimKm" />
              </rc-alan>
              <button
                type="submit"
                class="rc-dugme rc-dugme--kucuk"
                [disabled]="submission.gonderiliyor()"
              >
                {{ 'fiyatTarife.ekle' | transloco }}
              </button>
            </form>
          }
          <rc-form-hatalari [hatalar]="submission.genelHatalar()" />
        }
        @default {
          <p class="not" role="status">{{ 'fiyatTarife.yukleniyor' | transloco }}</p>
        }
      }
    </section>
  `,
})
export class ServiceDefinitionSuggestions {
  /** Öneri kabul edildi: tanım listesi yenilenmeli. */
  readonly accepted = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly t = translationFunction();

  protected readonly store = new TemelStore(() =>
    this.api
      .get<readonly Suggestion[]>('/api/ui/v1/servis-tanimlari/oneriler')
      .pipe(map((list) => list.map((s) => this.row(s)))),
  );
  protected readonly submission = formSubmission();

  constructor() {
    this.store.yukle();
  }

  protected accept(r: SuggestionRow): void {
    const v = r.form.getRawValue();
    const s = r.suggestion;
    const body = {
      kod: v.kod?.trim() ?? '',
      aracTipi: s.etiket,
      bakimKm: v.bakimKm,
      marka: s.marka,
      tip: s.tip,
      yakit: s.yakit,
      vites: s.vites,
      aciklama: null,
      aktif: true,
    };
    this.submission.gonder(
      r.form,
      (key) => this.api.post<unknown>('/api/ui/v1/servis-tanimlari', body, { islemAnahtari: key }),
      {
        basarili: () => {
          this.toast.basari(this.t('fiyatTarife.servisTanimlari.oneriEklendi', { kod: body.kod }));
          this.store.yenile();
          this.accepted.emit();
        },
      },
    );
  }

  private row(s: Suggestion): SuggestionRow {
    return {
      key: [s.marka, s.tip, s.yakit, s.vites].join('|'),
      suggestion: s,
      form: new FormGroup({
        kod: new FormControl<string | null>(s.onerilenKod, [
          Validators.required,
          Validators.maxLength(32),
        ]),
        bakimKm: new FormControl<number | null>(DEFAULT_KM, [
          Validators.required,
          Validators.min(0),
        ]),
      }),
    };
  }
}
