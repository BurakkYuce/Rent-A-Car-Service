import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@jsverse/transloco';
import { type Observable, finalize } from 'rxjs';

import { toApiError } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ConfirmService } from '@core/geri-bildirim/confirm-service';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { genelGosterilir } from '@core/oturum/session-interceptor';
import type { StoreState } from '@core/veri/temel-store';
import { Icon } from '@shared/ikon/icon';

import type { VehiclePhoto } from '../vehicle-model';

/** Sunucu sınırları (VehiclePhotoService): araç başına 20, dosya ≤ 2 MB, PNG/JPEG/WebP. */
export const MAX_PHOTOS = 20;
export const MAX_PHOTO_BYTES = 2 * 1024 * 1024;
export const PHOTO_TYPES = 'image/png,image/jpeg,image/webp';

/**
 * Araç fotoğraf galerisi (Blazor `VehicleEdit` "Fotoğraflar"): küçük resimler, ilki kapak, yukarı/aşağı
 * sırala, onaylı sil, yükle (multipart `foto`). İşlemler ANINDA kaydedilir (ana formun Kaydet'ini beklemez);
 * her işlemden sonra sunucunun güncel listesi yüklenir. Yazma yalnız `yazabilir` ile.
 */
@Component({
  selector: 'rc-vehicle-photos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Icon],
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
    }
    .izgara {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(9rem, 1fr));
      gap: var(--rc-bosluk-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .foto {
      position: relative;
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-1);
      min-width: 0;
    }
    .foto img {
      width: 100%;
      aspect-ratio: 4 / 3;
      object-fit: cover;
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-md);
      background: var(--rc-yuzey-alt);
    }
    .kapak {
      position: absolute;
      inset-block-start: var(--rc-bosluk-1);
      inset-inline-start: var(--rc-bosluk-1);
    }
    .eylemler,
    .yukleme {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-1);
      align-items: center;
    }
    .not {
      margin: 0;
      color: var(--rc-metin-ikincil);
      font-size: var(--rc-yazi-xs);
    }
    .hata {
      margin: 0;
      color: var(--rc-hata-metin);
      font-size: var(--rc-yazi-sm);
    }
    input[type='file'] {
      max-width: 100%;
    }
  `,
  template: `
    <p class="not">{{ 'arac.foto.aciklama' | transloco }}</p>
    <p class="not" aria-live="polite">
      {{ 'arac.foto.sayac' | transloco: { sayi: photos().length, enFazla: max } }}
    </p>
    @if (kaynak().tur === 'hata') {
      <p class="hata" role="alert">{{ loadError() }}</p>
    }
    <ul class="izgara" [attr.aria-label]="'arac.foto.baslik' | transloco">
      @for (p of photos(); track p.id; let i = $index; let last = $last) {
        <li class="foto">
          <img
            [src]="thumbUrl(p)"
            [alt]="'arac.foto.alt' | transloco: { sira: i + 1 }"
            loading="lazy"
          />
          @if (i === 0) {
            <span class="rc-rozet rc-rozet--bilgi kapak">{{ 'arac.foto.kapak' | transloco }}</span>
          }
          @if (yazabilir()) {
            <span class="eylemler">
              <button
                type="button"
                class="rc-dugme rc-dugme--kucuk rc-dugme--ikon"
                [disabled]="busy() || i === 0"
                [attr.aria-label]="'arac.foto.yukari' | transloco: { sira: i + 1 }"
                (click)="move(p, 'yukari')"
              >
                <rc-ikon ad="arrow-up" [boyut]="14" />
              </button>
              <button
                type="button"
                class="rc-dugme rc-dugme--kucuk rc-dugme--ikon"
                [disabled]="busy() || last"
                [attr.aria-label]="'arac.foto.asagi' | transloco: { sira: i + 1 }"
                (click)="move(p, 'asagi')"
              >
                <rc-ikon ad="arrow-down" [boyut]="14" />
              </button>
              <button
                type="button"
                class="rc-dugme rc-dugme--kucuk rc-dugme--hayalet"
                [disabled]="busy()"
                [attr.aria-label]="'arac.foto.silEtiket' | transloco: { sira: i + 1 }"
                (click)="remove(p)"
              >
                <rc-ikon ad="trash" [boyut]="14" />
                {{ 'arac.sil' | transloco }}
              </button>
            </span>
          }
        </li>
      } @empty {
        @if (kaynak().tur === 'hazir') {
          <li class="not">{{ 'arac.foto.bos' | transloco }}</li>
        }
      }
    </ul>
    @if (yazabilir()) {
      <div class="yukleme">
        <label class="not" [attr.for]="inputId">{{ 'arac.foto.sec' | transloco }}</label>
        <input #fileInput [id]="inputId" type="file" [attr.accept]="accept" (change)="picked()" />
        <button
          type="button"
          class="rc-dugme rc-dugme--kucuk"
          [disabled]="busy() || !file() || photos().length >= max"
          (click)="upload()"
        >
          {{ 'arac.foto.yukle' | transloco }}
        </button>
      </div>
      @if (error(); as e) {
        <p class="hata" role="alert">{{ e }}</p>
      }
    }
  `,
})
export class VehiclePhotos {
  readonly aracId = input.required<string>();
  readonly plaka = input('');
  readonly kaynak = input.required<StoreState<readonly VehiclePhoto[]>>();
  readonly yazabilir = input(false);
  /** Her işlemden sonra (listeyi yeniden yükleme sayfanın store'unda). */
  readonly yenile = input.required<() => void>();

  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = translationFunction();
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  protected readonly max = MAX_PHOTOS;
  protected readonly accept = PHOTO_TYPES;
  protected readonly inputId = `rc-arac-foto-${Math.random().toString(36).slice(2, 9)}`;
  protected readonly busy = signal(false);
  protected readonly file = signal<File | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly photos = computed<readonly VehiclePhoto[]>(() => {
    const k = this.kaynak();
    if (k.tur === 'hazir') return k.veri;
    if (k.tur === 'yukleniyor') return k.onceki ?? [];
    return [];
  });
  protected readonly loadError = computed(() => {
    const k = this.kaynak();
    return k.tur === 'hata' ? k.hata.detay : null;
  });

  protected thumbUrl(p: VehiclePhoto): string {
    return `/api/ui/v1/araclar/${encodeURIComponent(this.aracId())}/fotograflar/${encodeURIComponent(p.id)}/kucuk`;
  }

  protected picked(): void {
    this.error.set(null);
    const f = this.fileInput()?.nativeElement.files?.[0] ?? null;
    if (f && f.size > MAX_PHOTO_BYTES) {
      this.error.set(this.t('arac.foto.cokBuyuk'));
      this.file.set(null);
      return;
    }
    this.file.set(f);
  }

  protected upload(): void {
    const f = this.file();
    if (!f) return;
    const body = new FormData();
    body.append('foto', f, f.name);
    this.run(this.api.post<VehiclePhoto>(this.base(), body), () => {
      this.file.set(null);
      const el = this.fileInput()?.nativeElement;
      if (el) el.value = '';
      this.toast.basari(this.t('arac.foto.yuklendi'));
    });
  }

  protected move(p: VehiclePhoto, direction: 'yukari' | 'asagi'): void {
    this.run(
      this.api.post<readonly VehiclePhoto[]>(
        `${this.base()}/${encodeURIComponent(p.id)}/${direction}`,
        null,
      ),
      () => undefined,
    );
  }

  protected async remove(p: VehiclePhoto): Promise<void> {
    if (this.busy()) return;
    const yes = await this.confirm.ask({
      baslik: this.t('arac.foto.silBaslik'),
      mesaj: this.t('arac.foto.silMesaj', { plaka: this.plaka() }),
      onayEtiketi: this.t('arac.sil'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.run(
      this.api.delete<readonly VehiclePhoto[]>(`${this.base()}/${encodeURIComponent(p.id)}`),
      () => this.toast.basari(this.t('arac.foto.silindi')),
    );
  }

  private base(): `/api/ui/v1/${string}` {
    return `/api/ui/v1/araclar/${encodeURIComponent(this.aracId())}/fotograflar`;
  }

  private run(request: Observable<unknown>, done: () => void): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    request
      .pipe(
        finalize(() => this.busy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          done();
          this.yenile()();
        },
        error: (raw: unknown) => {
          const e = toApiError(raw);
          if (!genelGosterilir(e)) this.error.set(e.alanlar?.['foto']?.[0] ?? e.detay);
          this.yenile()();
        },
      });
  }
}
