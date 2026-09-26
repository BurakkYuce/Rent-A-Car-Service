import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Observable, forkJoin, map, of } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi, type ApiPath } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { trSearchKey } from '@core/metin/tr-normalize';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';
import { Checkbox } from '@shared/form/kontroller/checkbox';
import { Selection } from '@shared/form/kontroller/selection';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import { restDefinitionSource } from '@shared/form/tanim-crud/definition-source';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { branchFields } from '../definition-catalog';

type BranchService = Schema<'BranchServiceDto'>;
type MergePreview = Schema<'BranchMergePreviewDto'>;
type MergeResult = Schema<'BranchMergeResultDto'>;
interface ServiceRow extends BranchService {
  readonly subeId: string;
  readonly subeAd: string;
}

const ROOT: ApiPath = '/api/ui/v1/subeler';

/**
 * F11.2a şubeler (Blazor `BranchList`, ManageUsers): genel tanım CRUD'u (panel yerleşim, 29 alan + Durum) + şubeye özel
 * ücretsiz hizmetler + birleştirme (önce önizleme, sonra `onay: true` ile geri alınamaz birleştirme).
 */
@Component({
  selector: 'rc-branch-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    DefinitionCrud,
    Alan,
    FormErrors,
    TextInput,
    Checkbox,
    Selection,
    PageBand,
  ],
  styleUrl: '../definitions.scss',
  templateUrl: './branch-page.html',
})
export class BranchPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly crud = viewChild(DefinitionCrud);

  protected readonly branches = computed(() => this.crud()?.rows() ?? []);
  protected readonly fields = branchFields(this.t, {
    il: (q) => of(this.distinct('il', q)),
    ilce: (q) => of(this.distinct('ilce', q)),
  });
  protected readonly source = restDefinitionSource(ROOT);

  protected readonly branchOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.branches().map((b) => ({ deger: b.id, etiket: String(b['ad'] ?? '') })),
  );
  protected readonly activeBranchOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    this.branches()
      .filter((b) => b['aktif'] === true)
      .map((b) => ({ deger: b.id, etiket: String(b['ad'] ?? '') })),
  );

  // ---- ücretsiz hizmetler
  protected readonly services = signal<readonly ServiceRow[] | null>(null);
  protected readonly servicesError = signal<string | null>(null);
  protected readonly serviceForm = new FormGroup({
    subeId: new FormControl<string | null>(null, Validators.required),
    hizmetAdi: new FormControl<string | null>(null, [
      Validators.required,
      Validators.maxLength(128),
    ]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(512)),
  });
  protected readonly serviceSubmit = formSubmission();
  protected readonly serviceBusy = signal(false);

  // ---- birleştirme
  protected readonly mergeForm = new FormGroup({
    kaynakId: new FormControl<string | null>(null, Validators.required),
    hedefId: new FormControl<string | null>(null, Validators.required),
  });
  protected readonly confirmMerge = new FormControl<boolean>(false, { nonNullable: true });
  protected readonly confirmed = toSignal(this.confirmMerge.valueChanges, { initialValue: false });
  protected readonly preview = signal<MergePreview | null>(null);
  protected readonly previewSubmit = formSubmission();
  protected readonly mergeSubmit = formSubmission();

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    // Şube listesi (kimlikler) değişince hizmetler yeniden yüklenir (şube sayısı azdır; Blazor da hepsini okur).
    effect(() => {
      const ids = this.branches().map((b) => b.id);
      untracked(() => this.loadServices(ids));
    });
    // Seçim değişince eski önizleme geçersiz: onay kutusu ve birleştir düğmesi kalkar.
    this.mergeForm.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.preview.set(null);
      this.confirmMerge.setValue(false);
    });
  }

  hasUnsavedChanges(): boolean {
    return (this.crud()?.hasUnsavedChanges() ?? false) || this.serviceForm.dirty;
  }

  protected addService(): void {
    const v = this.serviceForm.getRawValue();
    this.serviceSubmit.gonder(
      this.serviceForm,
      (key) =>
        this.api.post<BranchService>(
          `${ROOT}/${encodeURIComponent(v.subeId ?? '')}/hizmetler`,
          { hizmetAdi: v.hizmetAdi, aciklama: v.aciklama },
          { islemAnahtari: key },
        ),
      {
        basarili: () => {
          this.serviceForm.reset({ subeId: v.subeId, hizmetAdi: null, aciklama: null });
          this.toast.basari(this.t('tanimlar.branch.hizmet.eklendi'));
          this.loadServices(this.branches().map((b) => b.id));
        },
      },
    );
  }

  protected async removeService(row: ServiceRow): Promise<void> {
    if (this.serviceBusy()) return;
    const yes = await this.confirm.ask({
      baslik: this.t('tanimlar.branch.hizmet.silBaslik'),
      mesaj: this.t('tanimlar.branch.hizmet.silMesaj', { ad: row.hizmetAdi }),
      onayEtiketi: this.t('tanimlar.branch.hizmet.sil'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.serviceBusy.set(true);
    this.api
      .delete(`${ROOT}/${encodeURIComponent(row.subeId)}/hizmetler/${encodeURIComponent(row.id)}`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.serviceBusy.set(false);
          this.toast.basari(this.t('tanimlar.branch.hizmet.silindi'));
          this.loadServices(this.branches().map((b) => b.id));
        },
        error: (e: unknown) => {
          this.serviceBusy.set(false);
          this.servicesError.set(toApiError(e).detay);
        },
      });
  }

  protected showPreview(): void {
    const v = this.mergeForm.getRawValue();
    this.previewSubmit.gonder(
      this.mergeForm,
      () =>
        this.api.get<MergePreview>(`${ROOT}/birlestir/onizleme`, {
          parametreler: { kaynakId: v.kaynakId ?? '', hedefId: v.hedefId ?? '' },
        }),
      { basarili: (p) => this.preview.set(p) },
    );
  }

  protected async merge(): Promise<void> {
    const p = this.preview();
    const v = this.mergeForm.getRawValue();
    if (p === null || !this.confirmMerge.value) return;
    const yes = await this.confirm.ask({
      baslik: this.t('tanimlar.branch.birlestir.onayBaslik'),
      mesaj: this.t('tanimlar.branch.birlestir.onayMesaj', {
        kaynak: p.kaynakAd,
        hedef: p.hedefAd,
      }),
      onayEtiketi: this.t('tanimlar.branch.birlestir.birlestir'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.mergeSubmit.gonder(
      this.mergeForm,
      (key) =>
        this.api.post<MergeResult>(
          `${ROOT}/birlestir`,
          { kaynakId: v.kaynakId, hedefId: v.hedefId, onay: true },
          { islemAnahtari: key },
        ),
      {
        basarili: (r) => {
          this.toast.basari(
            this.t('tanimlar.branch.birlestir.tamam', { sayi: String(r.tasinanKayit) }),
          );
          this.mergeForm.reset();
          this.crud()?.reload();
        },
      },
    );
  }

  private distinct(field: 'il' | 'ilce', q: string): readonly string[] {
    const key = trSearchKey(q);
    const seen = new Set<string>();
    const out: string[] = [];
    for (const b of this.branches()) {
      const v = typeof b[field] === 'string' ? (b[field] as string).trim() : '';
      const k = trSearchKey(v);
      if (v === '' || seen.has(k) || (key !== '' && !k.includes(key))) continue;
      seen.add(k);
      out.push(v);
    }
    return out.sort((a, b) => a.localeCompare(b, 'tr'));
  }

  private loadServices(ids: readonly string[]): void {
    const names = new Map(this.branches().map((b) => [b.id, String(b['ad'] ?? '')]));
    const calls: Observable<readonly ServiceRow[]>[] = ids.map((id) =>
      this.api
        .get<readonly BranchService[]>(`${ROOT}/${encodeURIComponent(id)}/hizmetler`)
        .pipe(map((l) => l.map((h) => ({ ...h, subeId: id, subeAd: names.get(id) ?? '' })))),
    );
    this.servicesError.set(null);
    (calls.length === 0 ? of([] as (readonly ServiceRow[])[]) : forkJoin(calls))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (lists) => this.services.set(lists.flat()),
        error: (e: unknown) =>
          this.servicesError.set(
            `${this.t('tanimlar.branch.hizmet.yuklenemedi')} ${toApiError(e).detay}`,
          ),
      });
  }
}
