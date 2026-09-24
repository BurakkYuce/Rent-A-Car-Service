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
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';

import { LISTINGS_ROOT } from './website-hub';

type ListingDetail = Sema<'ListingDetailDto'>;
type ListingFeature = Sema<'ListingFeatureDto'>;
type ListingPhoto = Sema<'ListingPhotoDto'>;
type FeaturesResult = Sema<'ListingFeaturesResultDto'>;

/** Sunucu sınırı (`VehiclePhotoService`): 2 MB; asıl kural sunucuda. */
export const MAX_PHOTO_BYTES = 2 * 1024 * 1024;
/** `OzellikSnapshot.MaxSatir`. */
export const MAX_FEATURE_ROWS = 30;

type FeatureRow = FormGroup<{
  etiket: FormControl<string | null>;
  deger: FormControl<string | null>;
  gorunur: FormControl<boolean | null>;
}>;

function featureRow(f?: ListingFeature): FeatureRow {
  return new FormGroup({
    // Sunucu (`OzellikSnapshot`): ad ≤ 60, değer ≤ 160; boş satır sessizce düşer (zorunlu değil).
    etiket: new FormControl<string | null>(f?.etiket ?? null, Validators.maxLength(60)),
    deger: new FormControl<string | null>(f?.deger ?? null, Validators.maxLength(160)),
    gorunur: new FormControl<boolean | null>(f?.gorunur ?? true),
  });
}

/**
 * F11.2b ilan sihirbazı adım 3/3 (Blazor `IlanOzellik`): fotoğraflar (üye araçlarda saklanır) + teknik özellik satırları.
 * Satırlar TAMAMEN değiştirilir (`surum` zorunlu). 409 `cakisma`'da yazdığınız satırlar SİLİNMEZ: sürüm tazelenir,
 * isterseniz güncel satırları yükleyip karşılaştırırsınız. Fotoğrafsız ilan taslakta kalır (sunucu söyler).
 */
@Component({
  selector: 'rc-listing-features',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, TranslocoPipe, FormHatalari, MetinGirdisi, OnayKutusu],
  styleUrl: '../system.scss',
  templateUrl: './listing-features.html',
})
export class ListingFeatures {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly session = inject(OturumServisi);
  private readonly t = ceviriFonksiyonu();
  private readonly photoInput = viewChild<ElementRef<HTMLInputElement>>('photoInput');
  protected readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly module = computed(() => this.session.ben()?.moduller.webSitesi === true);
  protected readonly detail = signal<ListingDetail | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly rows = new FormArray<FeatureRow>([]);
  protected readonly form = new FormGroup({ satirlar: this.rows });
  protected readonly submit = formGonderimi();
  /** 409 sonrası: sunucudaki güncel satırlar (kullanıcı isterse forma yükler). */
  protected readonly serverRows = signal<readonly ListingFeature[] | null>(null);
  protected readonly photoError = signal<string | null>(null);
  protected readonly photoBusy = signal(false);
  protected readonly maxRows = MAX_FEATURE_ROWS;

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
    if (this.module()) this.load();
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.form.dirty;
  }

  protected photoSrc(p: ListingPhoto): string {
    return `/api/ui/v1/araclar/${encodeURIComponent(p.aracId)}/fotograflar/${encodeURIComponent(p.fotoId)}/kucuk`;
  }

  protected load(): void {
    this.loadError.set(null);
    this.fetch().subscribe({
      next: (d) => this.apply(d),
      error: (e: unknown) => this.loadError.set(apiHatasinaCevir(e).detay),
    });
  }

  protected addRow(): void {
    this.rows.push(featureRow());
    this.form.markAsDirty();
  }

  protected removeRow(index: number): void {
    this.rows.removeAt(index);
    this.form.markAsDirty();
  }

  protected loadServerRows(): void {
    const fresh = this.serverRows();
    if (fresh === null) return;
    this.rows.clear();
    for (const f of fresh) this.rows.push(featureRow(f));
    this.serverRows.set(null);
    this.form.markAsDirty();
  }

  protected save(): void {
    const d = this.detail();
    if (d === null) return;
    const body = {
      satirlar: this.rows.getRawValue().map((r) => ({
        etiket: r.etiket ?? '',
        deger: r.deger ?? '',
        gorunur: r.gorunur === true,
      })),
      surum: d.surum ?? null,
    };
    this.submit.gonder(
      this.form,
      (key) =>
        this.api.put<FeaturesResult>(
          `${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(this.id)}/ozellikler`,
          body,
          { islemAnahtari: key },
        ),
      {
        esleme: { satirlar: 'satirlar' },
        basarili: (r) => {
          this.apply(r.ilan);
          this.toast.basari(
            this.t(r.yayinda ? 'sistem.web.ozellik.yayinda' : 'sistem.web.ozellik.taslakta'),
          );
        },
        hata: (h) => {
          if (h.kod !== 'cakisma') return;
          this.fetch().subscribe({
            next: (fresh) => {
              this.detail.set(fresh);
              this.serverRows.set(fresh.ozellikler);
            },
            error: () => undefined,
          });
        },
      },
    );
  }

  protected uploadPhoto(): void {
    const file = this.photoInput()?.nativeElement.files?.[0] ?? null;
    this.photoError.set(null);
    if (!file) {
      this.photoError.set(this.t('sistem.web.ozellik.fotoSecilmedi'));
      return;
    }
    if (file.size > MAX_PHOTO_BYTES) {
      this.photoError.set(this.t('sistem.web.ozellik.fotoBuyuk'));
      return;
    }
    const body = new FormData();
    body.append('foto', file, file.name);
    this.photoBusy.set(true);
    this.api
      .post<readonly ListingPhoto[]>(
        `${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(this.id)}/fotograflar`,
        body,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (photos) => {
          this.photoBusy.set(false);
          const el = this.photoInput()?.nativeElement;
          if (el) el.value = '';
          this.setPhotos(photos);
          this.toast.basari(this.t('sistem.web.ozellik.fotoYuklendi'));
        },
        error: (e: unknown) => this.photoFailed(e),
      });
  }

  protected async removePhoto(p: ListingPhoto): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.web.ozellik.fotoSilBaslik', { plaka: p.plaka }),
      mesaj: this.t('sistem.web.ozellik.fotoSilMesaj'),
      onayEtiketi: this.t('sistem.ortak.sil'),
      tehlikeli: true,
    });
    if (!yes) return;
    this.photoBusy.set(true);
    this.api
      .delete<readonly ListingPhoto[]>(
        `${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(this.id)}/fotograflar/${encodeURIComponent(p.aracId)}/${encodeURIComponent(p.fotoId)}`,
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (photos) => {
          this.photoBusy.set(false);
          this.setPhotos(photos);
        },
        error: (e: unknown) => this.photoFailed(e),
      });
  }

  private photoFailed(e: unknown): void {
    this.photoBusy.set(false);
    const h = apiHatasinaCevir(e);
    this.photoError.set(h.alanlar?.['foto']?.[0] ?? h.detay);
  }

  private setPhotos(photos: readonly ListingPhoto[]): void {
    const d = this.detail();
    if (d) this.detail.set({ ...d, fotograflar: [...photos] });
  }

  private apply(d: ListingDetail): void {
    this.rows.clear();
    for (const f of d.ozellikler) this.rows.push(featureRow(f));
    this.form.markAsPristine();
    this.serverRows.set(null);
    this.detail.set(d);
  }

  private fetch() {
    return this.api
      .get<ListingDetail>(`${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(this.id)}`)
      .pipe(takeUntilDestroyed(this.destroyRef));
  }
}
