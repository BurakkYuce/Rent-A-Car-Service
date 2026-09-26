import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import { TemelStore } from '@core/veri/temel-store';
import { DatePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formSubmission } from '@shared/form/form-submission';
import { FormErrors } from '@shared/form/form-errors';
import { TextInput } from '@shared/form/kontroller/text-input';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

type CompanyDocument = Schema<'CompanyDocumentDto'>;

/** Sunucu sınırları (`FirmaDokumanService.MaxBayt` / `MaxDokuman`); asıl dayatma serviste. */
export const MAX_DOCUMENT_BYTES = 3 * 1024 * 1024;
export const MAX_DOCUMENTS = 10;

/** Bayt → KB metni (Blazor: en az 1 KB). */
export function kilobytes(size: number | string): number {
  return Math.max(1, Math.floor(Number(size) / 1024));
}

/**
 * F11.2a dokümanlar (Blazor `Dokumanlar`): liste her oturuma açık; yükle/sil yalnız OperationsWrite (uç kapısıyla
 * aynı). İndirme mevcut çerezli indirme bağlantısı (`indirmeYolu`) — yeni dosya ucu yok.
 */
@Component({
  selector: 'rc-documents-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    DatePipe,
    Alan,
    FormErrors,
    TextInput,
    PageBand,
  ],
  styleUrl: '../definitions.scss',
  templateUrl: './documents-page.html',
})
export class DocumentsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  private readonly session = inject(SessionService);
  protected readonly canWrite = computed(() => this.session.izinVar('OperationsWrite'));
  protected readonly list = new TemelStore<readonly CompanyDocument[]>(() =>
    this.api.get<readonly CompanyDocument[]>('/api/ui/v1/dokumanlar'),
  );
  protected readonly rows = computed(() => this.list.veri() ?? []);
  protected readonly full = computed(() => this.rows().length >= MAX_DOCUMENTS);

  protected readonly form = new FormGroup({
    baslik: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(200)]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1000)),
  });
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly upload = formSubmission();
  protected readonly deleting = signal<string | null>(null);
  protected readonly deleteError = signal<string | null>(null);
  protected readonly kilobytes = kilobytes;

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
    this.list.yukle();
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty || this.file() !== null;
  }

  protected picked(): void {
    const f = this.fileInput()?.nativeElement.files?.[0] ?? null;
    this.fileError.set(
      f && f.size > MAX_DOCUMENT_BYTES ? this.t('tanimlar.document.cokBuyuk') : null,
    );
    this.file.set(f && f.size <= MAX_DOCUMENT_BYTES ? f : null);
  }

  protected save(): void {
    const f = this.file();
    if (!f) {
      this.form.markAllAsTouched();
      this.fileError.set(this.t('tanimlar.document.dosyaSecilmedi'));
      return;
    }
    const v = this.form.getRawValue();
    this.upload.gonder(
      this.form,
      (key) => {
        const body = new FormData();
        body.append('dosya', f, f.name);
        if (v.baslik) body.append('baslik', v.baslik);
        if (v.aciklama) body.append('aciklama', v.aciklama);
        return this.api.post<CompanyDocument>('/api/ui/v1/dokumanlar', body, {
          islemAnahtari: key,
        });
      },
      {
        esleme: { dosya: 'baslik' },
        basarili: () => {
          this.form.reset();
          this.file.set(null);
          const el = this.fileInput()?.nativeElement;
          if (el) el.value = '';
          this.toast.basari(this.t('tanimlar.document.yuklendi'));
          this.list.yenile();
        },
      },
    );
  }

  protected async remove(d: CompanyDocument): Promise<void> {
    if (this.deleting() !== null) return;
    const yes = await this.confirm.ask({
      baslik: this.t('tanimlar.document.silBaslik'),
      mesaj: this.t('tanimlar.document.silMesaj', { baslik: d.baslik }),
      onayEtiketi: this.t('tanimlar.document.sil'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.deleting.set(d.id);
    this.deleteError.set(null);
    this.api
      .delete(`/api/ui/v1/dokumanlar/${encodeURIComponent(d.id)}`)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(null);
          this.toast.basari(this.t('tanimlar.document.silindi'));
          this.list.yenile();
        },
        error: (e: unknown) => {
          this.deleting.set(null);
          this.deleteError.set(toApiError(e).detay);
        },
      });
  }
}
