import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { AramaSecim } from '@shared/form/arama-secim/arama-secim';
import { type SecimSecenegi, sunucuSecimKaynagi } from '@shared/form/arama-secim/secim-kaynagi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { ParaGirdisi } from '@shared/form/kontroller/para-girdisi';
import { SayiGirdisi } from '@shared/form/kontroller/sayi-girdisi';
import { TarihSecici } from '@shared/form/tarih/tarih-secici';

import type { DocumentResult, PenaltyType } from '../document-model';
import { type PenaltyForm, penaltyRequest } from '../document-requests';
import { PENALTIES } from '../document.store';

/** Blazor formundaki kalem yuvası (canlı 3 + 2 ek). Boş tutarlı kalem gönderilmez. */
const LINE_SLOTS = 5;

function lineGroup(required: boolean) {
  return new FormGroup({
    tutar: new FormControl<string | null>(null, required ? Validators.required : null),
    sebep: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
}

/**
 * Yeni ceza (`POST /cezalar`, OperationsWrite). Defter YAZMAZ (yansıtma ve ödeme ayrı işlem); toplam = Σ kalem
 * sunucuda. Şubeye bağlı kullanıcı araç vermek zorundadır (sunucu `aracId` alan hatası). Hatada form korunur.
 */
@Component({
  selector: 'rc-penalty-create-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    AramaSecim,
    FormHatalari,
    MetinGirdisi,
    ParaGirdisi,
    SayiGirdisi,
    TarihSecici,
  ],
  templateUrl: './penalty-create-form.html',
  styleUrl: '../finance-documents.scss',
})
export class PenaltyCreateForm {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  /** Ceza türü önerileri (seç veya yaz). */
  readonly types = input<readonly PenaltyType[] | undefined>(undefined);
  readonly saved = output<DocumentResult>();
  readonly dirtyChange = output<boolean>();

  protected readonly vehicles = sunucuSecimKaynagi('arac');
  protected readonly customers = sunucuSecimKaynagi('musteri');
  protected readonly typeNames = computed(() => (this.types() ?? []).map((x) => x.ad));

  protected readonly lines = new FormArray(
    Array.from({ length: LINE_SLOTS }, (_, i) => lineGroup(i === 0)),
  );
  protected readonly form = new FormGroup({
    cezaTuru: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(128),
    ]),
    tebligTarihi: new FormControl<string | null>(null),
    saat: new FormControl<string | null>(null, Validators.maxLength(8)),
    vadeGun: new FormControl<number | null>(15, [Validators.min(0), Validators.max(3650)]),
    yer: new FormControl<string | null>(null, Validators.maxLength(256)),
    cepTel: new FormControl<string | null>(null, Validators.maxLength(32)),
    makbuzNo: new FormControl<string | null>(null, Validators.maxLength(64)),
    islemSube: new FormControl<string | null>(null, Validators.maxLength(128)),
    arac: new FormControl<SecimSecenegi | null>(null),
    cari: new FormControl<SecimSecenegi | null>(null),
    sebep: new FormControl<string | null>(null, Validators.maxLength(512)),
    kalemler: this.lines,
  });
  protected readonly submission = formGonderimi();

  constructor() {
    this.form.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.dirtyChange.emit(this.form.dirty));
  }

  protected submit(): void {
    const value = this.form.getRawValue() as PenaltyForm;
    const body = penaltyRequest(value);
    // Boş yuvalar gönderilmez: sunucunun `kalemler[j]`'si j. DOLU yuvadır → alan hatası doğru yuvaya yazılsın.
    const mapping: Record<string, string> = {
      aracId: 'arac',
      cariId: 'cari',
      kalemler: 'kalemler.0.tutar',
    };
    value.kalemler
      .map((k, slot) => ({ slot, filled: (k.tutar?.trim() ?? '') !== '' }))
      .filter((x) => x.filled)
      .forEach(({ slot }, j) => {
        mapping[`kalemler[${j}].tutar`] = `kalemler.${slot}.tutar`;
        mapping[`kalemler[${j}].sebep`] = `kalemler.${slot}.sebep`;
      });
    this.submission.gonder(
      this.form,
      (key) => this.api.post<DocumentResult>(PENALTIES, body, { islemAnahtari: key }),
      {
        esleme: mapping,
        basarili: (r) => {
          this.toast.basari(this.t('finansBelge.ceza.kaydedildi', { no: r.no }));
          this.form.reset({ vadeGun: 15 });
          this.submission.kilit.yenile();
          this.dirtyChange.emit(false);
          this.saved.emit(r);
        },
      },
    );
  }
}
