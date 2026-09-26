import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { Selection } from '@shared/form/kontroller/selection';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { pagedDefinitionSource } from '../paged-source';
import { vehicleGroupFields } from './vehicle-group-fields';

type Unmatched = Schema<'UnmatchedGroupValueDto'>;
type BrandRow = Schema<'DefinitionDto'>;
type AssignResult = Schema<'GroupAssignResult'>;

const ROOT: ApiPath = '/api/ui/v1/arac-gruplari';
/** Grubu BOŞ araçların seçenek anahtarı (gerçek grup değeriyle çakışmaz: sunucu `bos: true` alır). */
export const EMPTY_GROUP_KEY = '\u0000bos';

/**
 * F11.2c araç grupları (Blazor `VehicleGroupList`, OperationsWrite): tanım CRUD'u (panel, 38 alan) + tanılama:
 * hiçbir aktif gruba eşleşmeyen filo `Grup` değerleri ve bunları tanımlı gruba toplu taşıma (`/ata`; araçların
 * `Grup` alanı güncellenir, denetime yazılır).
 */
@Component({
  selector: 'rc-vehicle-group-page',
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
  templateUrl: './vehicle-group-page.html',
})
export class VehicleGroupPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly crud = viewChild(DefinitionCrud);

  /** Marka önerisi aktif marka tanımlarından (Blazor `_markaOpts`; seç veya yaz). */
  protected readonly fields = vehicleGroupFields(this.t, {
    brand: (q) =>
      this.api
        .get<readonly BrandRow[]>('/api/ui/v1/markalar', { parametreler: { q, aktif: true } })
        .pipe(map((list) => list.slice(0, 20).map((b) => b.ad))),
  });
  protected readonly source = pagedDefinitionSource(ROOT);

  protected readonly unmatched = signal<readonly Unmatched[] | null>(null);
  protected readonly unmatchedError = signal<string | null>(null);
  protected readonly valueOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.unmatched() ?? []).map((u) => ({
      deger: u.bos ? EMPTY_GROUP_KEY : u.grup,
      etiket: u.bos ? this.t('tanimlar.vehicleGroup.eslesmeyen.bos') : u.grup,
    })),
  );
  protected readonly groupOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.crud()?.rows() ?? [])
      .filter((g) => g['aktif'] === true)
      .map((g) => ({ deger: g.id, etiket: String(g['ad'] ?? '') })),
  );

  protected readonly assignForm = new FormGroup({
    kaynak: new FormControl<string | null>(null, Validators.required),
    hedefGrupId: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly assignSubmit = formSubmission();

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    this.loadUnmatched();
  }

  hasUnsavedChanges(): boolean {
    return this.crud()?.hasUnsavedChanges() ?? false;
  }

  /** Grup eklenip adı değişince eşleşme de değişir. */
  protected loadUnmatched(): void {
    this.unmatchedError.set(null);
    this.api
      .get<readonly Unmatched[]>(`${ROOT}/eslesmeyen`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (list) => this.unmatched.set(list),
        error: (e: unknown) => this.unmatchedError.set(toApiError(e).detay),
      });
  }

  protected assign(): void {
    const v = this.assignForm.getRawValue();
    const empty = v.kaynak === EMPTY_GROUP_KEY;
    this.assignSubmit.gonder(
      this.assignForm,
      (key) =>
        this.api.post<AssignResult>(
          `${ROOT}/ata`,
          { hedefGrupId: v.hedefGrupId, kaynak: empty ? null : v.kaynak, bos: empty },
          { islemAnahtari: key },
        ),
      {
        basarili: (r) => {
          this.toast.basari(
            this.t('tanimlar.vehicleGroup.eslesmeyen.tamam', { sayi: String(r.tasinan) }),
          );
          this.assignForm.reset();
          this.loadUnmatched();
          this.crud()?.reload();
        },
      },
    );
  }
}
