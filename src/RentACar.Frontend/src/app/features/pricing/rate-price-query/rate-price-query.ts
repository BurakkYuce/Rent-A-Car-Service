import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { clearServerErrors, applyServerErrors } from '@core/form/sunucu-hatalari';
import { type DayText, bugun } from '@core/form/tarih-girdisi';
import { requestContext } from '@core/oturum/request-context';
import { textValue } from '@features/planlama-ortak/form-yardimcilari';
import { MoneyPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { NumberInput } from '@shared/form/kontroller/number-input';
import { DatePicker } from '@shared/form/tarih/date-picker';

type RateMatrixPrice = Schema<'RateMatrixPriceDto'>;

const toNum = (v: number | string): number => (typeof v === 'number' ? v : Number(v));

/**
 * Tarife matrisi "Fiyat Sorgula (çözümleme)" paneli (Blazor `/tarife-matris` üst bölümü): kanal / şube / araç grubu /
 * tarih / gün için motorla AYNI kuralla ("yalnız onaylı + aktif", en dar pencere → en yeni → kod) çözülen günlük ve
 * toplam fiyat — `GET /tarife-matris/fiyat`. Salt okuma; UI hesap yapmaz.
 */
@Component({
  selector: 'rc-rate-price-query',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormErrors,
    TextInput,
    MoneyPipe,
    NumberInput,
    DatePicker,
  ],
  template: `
    <details class="rc-bolum acilir">
      <summary>{{ 'fiyatTarife.fiyatSorgu.baslik' | transloco }}</summary>
      <p class="not">{{ 'fiyatTarife.fiyatSorgu.aciklama' | transloco }}</p>
      <form class="form" [formGroup]="form" (ngSubmit)="run()">
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'fiyatTarife.alan.kanal' | transloco">
            <rc-metin-girdisi formControlName="kanal" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'fiyatTarife.alan.sube' | transloco">
            <rc-metin-girdisi formControlName="sube" [azamiUzunluk]="64" />
          </rc-alan>
          <rc-alan [etiket]="'fiyatTarife.alan.aracGrupKod' | transloco">
            <rc-metin-girdisi formControlName="aracGrupKod" [azamiUzunluk]="32" />
          </rc-alan>
          <rc-alan [etiket]="'fiyatTarife.alan.paraBirimi' | transloco">
            <rc-metin-girdisi formControlName="paraBirimi" [azamiUzunluk]="8" />
          </rc-alan>
          <rc-alan [etiket]="'fiyatTarife.fiyatSorgu.tarih' | transloco">
            <rc-tarih-secici formControlName="tarih" />
          </rc-alan>
          <rc-alan [etiket]="'fiyatTarife.fiyatSorgu.gun' | transloco">
            <rc-sayi-girdisi formControlName="gun" />
          </rc-alan>
        </div>
        <rc-form-hatalari [hatalar]="errors()" />
        <div class="rc-form-eylemler">
          <button type="submit" class="rc-dugme rc-dugme--kucuk" [disabled]="busy()">
            {{ 'fiyatTarife.fiyatSorgu.sorgula' | transloco }}
          </button>
        </div>
      </form>
      @if (notFound()) {
        <p class="not" role="status">{{ 'fiyatTarife.fiyatSorgu.yok' | transloco }}</p>
      }
      @if (result(); as r) {
        <p class="sonraki" role="status">
          {{
            'fiyatTarife.fiyatSorgu.sonuc'
              | transloco
                : {
                    kod: r.kod,
                    ad: r.ad,
                    gunluk: (num(r.gunlukFiyat) | para: r.paraBirimi ?? 'TRY'),
                    gun: r.gun,
                    toplam: (num(r.toplamFiyat) | para: r.paraBirimi ?? 'TRY'),
                  }
          }}
        </p>
      }
    </details>
  `,
  styleUrl: '../pricing.scss',
})
export class RatePriceQuery {
  private readonly api = inject(ApiIstemcisi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly num = toNum;
  protected readonly busy = signal(false);
  protected readonly result = signal<RateMatrixPrice | null>(null);
  protected readonly notFound = signal(false);
  protected readonly errors = signal<readonly string[]>([]);
  protected readonly form = new FormGroup({
    kanal: new FormControl<string | null>(null),
    sube: new FormControl<string | null>(null),
    aracGrupKod: new FormControl<string | null>(null),
    paraBirimi: new FormControl<string | null>(null),
    tarih: new FormControl<DayText | null>(bugun(), Validators.required),
    gun: new FormControl<number | null>(1, [
      Validators.required,
      Validators.min(1),
      Validators.max(3650),
    ]),
  });

  protected run(): void {
    if (this.busy()) return;
    clearServerErrors(this.form);
    this.errors.set([]);
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.busy.set(true);
    this.notFound.set(false);
    this.result.set(null);
    this.api
      .get<RateMatrixPrice>('/api/ui/v1/tarife-matris/fiyat', {
        parametreler: {
          kanal: textValue(v.kanal),
          sube: textValue(v.sube),
          aracGrupKod: textValue(v.aracGrupKod),
          paraBirimi: textValue(v.paraBirimi),
          tarih: v.tarih,
          gun: v.gun,
        },
        context: requestContext({ sessiz: true }),
      })
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (r) => this.result.set(r),
        error: (raw: unknown) => {
          const e = toApiError(raw);
          if (e.status === 404) {
            this.notFound.set(true);
            return;
          }
          const unmatched = applyServerErrors(this.form, e.alanlar);
          this.errors.set(unmatched.length > 0 ? unmatched : e.alanlar ? [] : [e.detay]);
        },
      });
  }
}
