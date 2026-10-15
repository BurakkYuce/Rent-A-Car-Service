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
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { enumLabel } from '@core/bicim/enum-label';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import { toNumber } from '@features/vehicles/vehicle-model';
import { MoneyPipe, DateTimePipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import type { SecenekOgesi } from '@shared/form/kontroller/secenek';
import { Selection } from '@shared/form/kontroller/selection';
import { Icon } from '@shared/ikon/icon';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import {
  HGS,
  HGS_MAX_BYTES,
  type HgsField,
  type HgsImportResult,
  acceptedFile,
  mappingJson,
} from './hgs-model';

type MappingForm = FormGroup<Record<string, FormControl<string | null>>>;

/** Sunucu sayıları JSON'da sayı ya da metin gelebilir (üretilen tip `number | string`). */
const count = (v: number | string): number => toNumber(v) ?? 0;

/**
 * HGS geçişlerini dosyadan içe aktar (`/app/hgs/ice-aktar`) — iki adım:
 * (1) **Önizle**: dosya sunucuda okunur, hiçbir şey yazılmaz; sütun eşlemesi (öneri sunucudan) + kaç satır kiraya
 * eşleşti / kira dışı (firma gideri) / mükerrer / hatalı. Kullanıcı eşlemeyi değiştirip yeniden önizler.
 * (2) **Onayla**: aynı dosya + aynı eşleme yazılır. Mükerrer koruması sunucuda (doğal anahtar unique index'i) — iki kez
 * onaylamak tek kayıt üretir. Biçim tahmini (tarih, Türkçe sayı, "1.250" belirsizliği) tamamen sunucuda.
 */
@Component({
  selector: 'rc-hgs-import',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PageBand,
    ReactiveFormsModule,
    RouterLink,
    TranslocoPipe,
    Alan,
    Icon,
    Selection,
    MoneyPipe,
    DateTimePipe,
  ],
  templateUrl: './hgs-import.html',
  styleUrl: './hgs.scss',
})
export class HgsImport {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly num = toNumber;
  protected readonly file = signal<File | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly preview = signal<HgsImportResult | null>(null);
  protected readonly imported = signal<HgsImportResult | null>(null);
  /** Önizleme sonrası eşleme değişti mi (onay için yeniden önizleme şart). */
  protected readonly mappingDirty = signal(false);
  protected mappingForm: MappingForm = new FormGroup({});

  protected readonly fields = computed<readonly HgsField[]>(() => this.preview()?.alanlar ?? []);
  protected readonly headerOptions = computed<readonly SecenekOgesi<string>[]>(() =>
    (this.preview()?.basliklar ?? []).map((h) => ({ deger: h, etiket: h })),
  );
  /** Yazılacak satır var ve önizleme güncel mi. */
  protected readonly canConfirm = computed(() => {
    const p = this.preview();
    if (p === null || this.mappingDirty() || this.busy()) return false;
    return count(p.eslesen) + count(p.kiraDisi) + count(p.belirsiz) > 0;
  });
  /** Önizleme satırları kesik mi (sayılar dosyanın tamamı için). */
  protected readonly truncated = computed(() => {
    const p = this.preview();
    return p !== null && p.satirlar.length < count(p.toplam);
  });

  protected picked(): void {
    const f = this.fileInput()?.nativeElement.files?.[0] ?? null;
    this.preview.set(null);
    this.imported.set(null);
    this.mappingDirty.set(false);
    this.error.set(null);
    if (f && (f.size > HGS_MAX_BYTES || !acceptedFile(f.name))) {
      this.file.set(null);
      const el = this.fileInput()?.nativeElement;
      if (el) el.value = '';
      this.error.set(
        this.t(f.size > HGS_MAX_BYTES ? 'finansBelge.hgs.cokBuyuk' : 'finansBelge.hgs.bicim'),
      );
      return;
    }
    this.file.set(f);
  }

  /** Önizle — ilk seferde sunucu önerisiyle, sonra kullanıcının eşlemesiyle. */
  protected runPreview(): void {
    const f = this.file();
    if (!f || this.busy()) return;
    this.send(`${HGS}/ice-aktar/onizle`, f, this.currentMapping(), (r) => {
      this.preview.set(r);
      this.mappingDirty.set(false);
      this.buildMappingForm(r.alanlar);
    });
  }

  protected async confirmImport(): Promise<void> {
    const f = this.file();
    const p = this.preview();
    if (!f || !p || !this.canConfirm()) return;
    const yes = await this.confirm.ask({
      baslik: this.t('finansBelge.hgs.onayBaslik'),
      mesaj: this.t('finansBelge.hgs.onayMesaj', {
        eslesen: p.eslesen,
        kiraDisi: p.kiraDisi,
        belirsiz: p.belirsiz,
        mukerrer: p.mukerrer,
        hatali: p.hatali,
      }),
    });
    if (!yes || this.busy()) return;
    this.send(`${HGS}/ice-aktar`, f, this.currentMapping(), (r) => {
      this.imported.set(r);
      this.preview.set(r);
      this.toast.basari(this.t('finansBelge.hgs.aktarildi', { adet: r.eklenen }));
    });
  }

  protected statusLabel(s: string): string {
    return enumLabel(s);
  }

  protected station(r: { girisIstasyonu?: string | null; cikisIstasyonu?: string | null }): string {
    if (r.girisIstasyonu && r.cikisIstasyonu) return `${r.girisIstasyonu} → ${r.cikisIstasyonu}`;
    return r.cikisIstasyonu ?? r.girisIstasyonu ?? '—';
  }

  private currentMapping(): string | null {
    const p = this.preview();
    if (!p) return null;
    return mappingJson(p.alanlar, this.mappingForm.getRawValue());
  }

  private buildMappingForm(fields: readonly HgsField[]): void {
    const controls: Record<string, FormControl<string | null>> = {};
    for (const f of fields) controls[f.kod] = new FormControl<string | null>(f.baslik ?? null);
    this.mappingForm = new FormGroup(controls);
    this.mappingForm.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.mappingDirty.set(true));
  }

  private send(
    url: `/api/ui/v1/${string}`,
    file: File,
    mapping: string | null,
    done: (r: HgsImportResult) => void,
  ): void {
    const body = new FormData();
    body.append('dosya', file, file.name);
    if (mapping !== null) body.append('eslesme', mapping);
    this.busy.set(true);
    this.error.set(null);
    this.api
      .post<HgsImportResult>(url, body)
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: done,
        error: (raw: unknown) => {
          const e = toApiError(raw);
          const messages = e.alanlar ? Object.values(e.alanlar).flat() : [];
          if (e.status === 413) this.error.set(this.t('finansBelge.hgs.cokBuyuk'));
          else if (messages.length > 0) this.error.set(messages.join(' '));
          else if (!genelGosterilir(e)) this.error.set(e.detay);
        },
      });
  }
}
