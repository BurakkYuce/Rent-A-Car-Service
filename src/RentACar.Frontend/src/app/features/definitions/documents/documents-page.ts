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

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemelStore } from '@core/veri/temel-store';
import { TarihPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';

type CompanyDocument = Sema<'CompanyDocumentDto'>;

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
    TarihPipe,
    Alan,
    FormHatalari,
    MetinGirdisi,
  ],
  styleUrl: '../definitions.scss',
  templateUrl: './documents-page.html',
})
export class DocumentsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  private readonly session = inject(OturumServisi);
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
  protected readonly upload = formGonderimi();
  protected readonly deleting = signal<string | null>(null);
  protected readonly deleteError = signal<string | null>(null);
  protected readonly kilobytes = kilobytes;

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    this.list.yukle();
  }

  kaydedilmemisDegisiklikVar(): boolean {
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
    const yes = await this.confirm.sor({
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
          this.deleteError.set(apiHatasinaCevir(e).detay);
        },
      });
  }
}
