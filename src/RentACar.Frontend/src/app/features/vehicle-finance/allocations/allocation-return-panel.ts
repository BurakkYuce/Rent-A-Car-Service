import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import {
  type AbstractControl,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  type ValidationErrors,
  Validators,
} from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { toNumber } from '@features/vehicles/vehicle-model';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { TarihSaatSecici } from '@shared/form/tarih/tarih-saat-secici';

import type { Allocation } from '../finance-model';
import { ALLOCATIONS, recordPath } from '../finance.store';
import {
  TIME_PATTERN,
  type AllocationReturnValue,
  allocationReturnRequest,
} from './allocation-model';

/**
 * Satırın "Teslim Al" formu (Blazor satır içi form). Sayfa bunu `@for (…; track id)` ile çizer: başka satıra
 * geçince bileşen ve formu YENİDEN oluşur — eski satırın değeri yeni satıra taşınmaz. Kilit altında durum çitli
 * (ikinci teslim 400); dönüş KM ≥ çıkış KM sunucuda da denetlenir.
 */
@Component({
  selector: 'rc-allocation-return-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
    SayiGirdisi,
    TarihSaatSecici,
  ],
  template: `
    <section class="bolum" [attr.aria-labelledby]="headingId">
      <h2 [id]="headingId">
        {{ 'aracFinans.baf.teslimBaslik' | transloco: { no: row().no, plaka: row().plaka } }}
      </h2>
      <form class="form" [formGroup]="form" (ngSubmit)="submit()">
        <div class="rc-form-izgara">
          <rc-alan [etiket]="'aracFinans.baf.donusKm' | transloco" [ipucu]="kmHint()">
            <rc-sayi-girdisi formControlName="donusKm" />
          </rc-alan>
          <rc-alan [etiket]="'aracFinans.baf.donusYakit' | transloco">
            <rc-sayi-girdisi formControlName="donusYakit" />
          </rc-alan>
          <rc-alan
            [etiket]="'aracFinans.baf.donusTarihi' | transloco"
            [ipucu]="'aracFinans.baf.donusTarihiIpucu' | transloco"
          >
            <rc-tarih-saat-secici formControlName="donusTarihi" />
          </rc-alan>
          <rc-alan [etiket]="'aracFinans.baf.donusOfisi' | transloco">
            <rc-metin-girdisi
              formControlName="donusSube"
              [azamiUzunluk]="100"
              liste="dl-baf-sube"
            />
          </rc-alan>
          <rc-alan [etiket]="'aracFinans.baf.donusSaat' | transloco">
            <rc-metin-girdisi formControlName="donusSaat" [azamiUzunluk]="5" yerTutucu="ss:dd" />
          </rc-alan>
        </div>
        <rc-form-hatalari [hatalar]="submission.genelHatalar()" />
        <div class="form__eylemler">
          <button
            type="submit"
            class="rc-dugme rc-dugme--birincil rc-dugme--kucuk"
            [disabled]="submission.gonderiliyor()"
          >
            {{
              submission.gonderiliyor()
                ? ('form.gonderiliyor' | transloco)
                : ('aracFinans.baf.teslimAl' | transloco)
            }}
          </button>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
            [disabled]="submission.gonderiliyor()"
            (click)="closed.emit()"
          >
            {{ 'aracFinans.vazgec' | transloco }}
          </button>
        </div>
      </form>
    </section>
  `,
  styleUrl: '../vehicle-finance.scss',
})
export class AllocationReturnPanel {
  readonly row = input.required<Allocation>();
  readonly closed = output<void>();
  readonly returned = output<void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly t = ceviriFonksiyonu();

  private static seq = 0;
  protected readonly headingId = `rc-baf-teslim-${++AllocationReturnPanel.seq}`;
  protected readonly submission = formGonderimi();
  protected readonly kmHint = computed(() =>
    this.t('aracFinans.baf.donusKmIpucu', { km: toNumber(this.row().cikisKm) ?? 0 }),
  );

  protected readonly form = new FormGroup({
    donusKm: new FormControl<number | null>(null, [
      Validators.required,
      (c: AbstractControl) => this.minKm(c),
    ]),
    donusYakit: new FormControl<number | null>(null, [Validators.min(0), Validators.max(100)]),
    donusTarihi: new FormControl<string | null>(null),
    donusSube: new FormControl<string | null>(null, Validators.maxLength(100)),
    donusSaat: new FormControl<string | null>(null, Validators.pattern(TIME_PATTERN)),
  });

  constructor() {
    afterNextRender(() => {
      this.host.nativeElement.querySelector<HTMLInputElement>('input')?.focus();
    });
  }

  protected submit(): void {
    const row = this.row();
    const body = allocationReturnRequest(this.form.getRawValue() as AllocationReturnValue);
    this.submission.gonder(
      this.form,
      (key) =>
        this.api.post<Allocation>(recordPath(ALLOCATIONS, row.id, '/teslim-al'), body, {
          islemAnahtari: key,
        }),
      {
        basarili: () => {
          this.toast.basari(this.t('aracFinans.baf.teslimAlindi', { no: row.no }));
          this.returned.emit();
        },
      },
    );
  }

  private minKm(c: AbstractControl): ValidationErrors | null {
    const v: unknown = c.value;
    if (typeof v !== 'number') return null;
    const min = toNumber(this.row().cikisKm) ?? 0;
    return v < min ? { kmAz: { mesaj: this.t('aracFinans.baf.kmAz', { km: min }) } } : null;
  }
}
