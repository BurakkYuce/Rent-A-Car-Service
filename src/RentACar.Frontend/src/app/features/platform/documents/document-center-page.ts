import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom, type Observable } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';
import { TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { Alan } from '@shared/form/alan/alan';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';
import { MetinGirdisi } from '@shared/form/kontroller/metin-girdisi';
import { OnayKutusu } from '@shared/form/kontroller/onay-kutusu';
import { Ikon } from '@shared/ikon/ikon';

import {
  documentStatus,
  documentUploadForm,
  kilobytes,
  PLATFORM_API,
  type PlatformDocument,
  type PlatformTenantOption,
  toNumber,
} from '../platform-model';
import { PlatformSessionService } from '../platform-session';

/** Max PDF size (server `PdfValidation.MaxBayt`, 3 MB) — shown as a hint; the server decides. */
const MAX_MB = 3;

/**
 * `/app/platform/belgeler` — Document Center (Blazor `Belgeler` parity): ready PDFs distributed to
 * tenants. Upload starts as DRAFT (no tenant sees it until published); targets none = ALL tenants;
 * "only managers" flag; publish / archive; new version replaces the file in place (tenant links keep
 * working); delete asks first (archive is the reversible option). PDF-ness is decided on the server.
 */
@Component({
  selector: 'rc-document-center-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    TranslocoPipe,
    Alan,
    FormHatalari,
    Ikon,
    MetinGirdisi,
    OnayKutusu,
    TarihSaatPipe,
  ],
  templateUrl: './document-center-page.html',
  styleUrls: ['../platform-page.scss', './document-center-page.scss'],
})
export class DocumentCenterPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly session = inject(PlatformSessionService);
  private readonly t = ceviriFonksiyonu();

  protected readonly maxMb = MAX_MB;
  protected readonly docs = new TemelStore(
    () => this.api.get<readonly PlatformDocument[]>(`${PLATFORM_API}/belgeler`),
    { oncekiVeriyiKoru: true },
  );
  protected readonly tenants = new TemelStore(() =>
    this.api.get<readonly PlatformTenantOption[]>(`${PLATFORM_API}/kiracilar/secim`, {
      context: istekBaglami({ sessiz: true }),
    }),
  );
  protected readonly rows = computed(() => this.docs.veri() ?? []);

  protected readonly uploadOpen = signal(false);
  protected readonly form = new FormGroup({
    baslik: new FormControl<string | null>(null, [Validators.required, Validators.maxLength(200)]),
    aciklama: new FormControl<string | null>(null, Validators.maxLength(1000)),
    yalnizYoneticiler: new FormControl<boolean>(false, { nonNullable: true }),
  });
  protected readonly upload = formGonderimi();
  protected readonly file = signal<File | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly targets = signal<ReadonlySet<string>>(new Set());

  /** Row under a mutation (one at a time). */
  protected readonly busyId = signal<string | null>(null);
  protected readonly versionFiles = signal<Readonly<Record<string, File>>>({});

  protected readonly status = documentStatus;
  protected readonly kb = kilobytes;
  protected readonly num = toNumber;

  constructor() {
    this.docs.yukle();
    this.tenants.yukle();
    effect(() => {
      const d = this.docs.durum();
      if (d.tur === 'hazir' && d.veri.length === 0) this.uploadOpen.set(true);
    });
    effect(() => {
      const error = this.docs.hata();
      if (error) this.session.handleSessionLoss(error);
    });
  }

  protected contentUrl(doc: PlatformDocument): string {
    return `${PLATFORM_API}/belgeler/${encodeURIComponent(doc.id)}/icerik`;
  }

  protected toggleTarget(id: string, event: Event): void {
    const next = new Set(this.targets());
    if ((event.target as HTMLInputElement).checked) next.add(id);
    else next.delete(id);
    this.targets.set(next);
  }

  protected pickFile(event: Event): void {
    this.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
    this.fileError.set(null);
  }

  protected submit(input: HTMLInputElement): void {
    const file = this.file();
    if (!file) {
      // No request without a file; the other fields show their own errors too.
      this.form.markAllAsTouched();
      this.fileError.set(this.t('platform.belgeler.dosyaSecilmedi'));
      return;
    }
    const v = this.form.getRawValue();
    this.upload.gonder(
      this.form,
      () =>
        this.api.post<PlatformDocument>(
          `${PLATFORM_API}/belgeler`,
          documentUploadForm({
            baslik: v.baslik ?? '',
            aciklama: v.aciklama,
            file,
            onlyManagers: v.yalnizYoneticiler,
            targets: [...this.targets()],
          }),
        ),
      {
        basarili: () => {
          this.toast.basari(this.t('platform.belgeler.yuklendi'));
          this.form.reset({ baslik: null, aciklama: null, yalnizYoneticiler: false });
          this.targets.set(new Set());
          this.file.set(null);
          input.value = '';
          this.docs.yenile();
        },
        hata: (e) => {
          if (this.session.handleSessionLoss(e)) return;
          const fileMessage = e.alanlar?.['dosya']?.[0];
          if (fileMessage) this.fileError.set(fileMessage);
        },
      },
    );
  }

  protected setStatus(doc: PlatformDocument, durum: 'Yayinda' | 'Arsiv'): void {
    void this.mutate(doc, () =>
      this.api.post(`${PLATFORM_API}/belgeler/${encodeURIComponent(doc.id)}/durum`, { durum }),
    );
  }

  protected pickVersion(doc: PlatformDocument, event: Event): void {
    const picked = (event.target as HTMLInputElement).files?.[0];
    const next = { ...this.versionFiles() };
    if (picked) next[doc.id] = picked;
    else delete next[doc.id];
    this.versionFiles.set(next);
  }

  protected async uploadVersion(doc: PlatformDocument, input: HTMLInputElement): Promise<void> {
    const picked = this.versionFiles()[doc.id];
    if (!picked) {
      this.toast.uyari(this.t('platform.belgeler.dosyaSecilmedi'));
      return;
    }
    const form = new FormData();
    form.append('dosya', picked, picked.name);
    const done = await this.mutate(doc, () =>
      this.api.post(`${PLATFORM_API}/belgeler/${encodeURIComponent(doc.id)}/surum`, form),
    );
    if (done) {
      input.value = '';
      const next = { ...this.versionFiles() };
      delete next[doc.id];
      this.versionFiles.set(next);
    }
  }

  protected async remove(doc: PlatformDocument): Promise<void> {
    const ok = await this.confirm.sor({
      baslik: this.t('platform.belgeler.silBaslik'),
      mesaj: this.t('platform.belgeler.silMesaj', { baslik: doc.baslik }),
      tehlikeli: true,
    });
    if (!ok) return;
    await this.mutate(doc, () =>
      this.api.delete(`${PLATFORM_API}/belgeler/${encodeURIComponent(doc.id)}`),
    );
  }

  private async mutate(doc: PlatformDocument, call: () => Observable<unknown>): Promise<boolean> {
    if (this.busyId() !== null) return false;
    this.busyId.set(doc.id);
    try {
      await firstValueFrom(call());
      this.toast.basari(this.t('platform.islemTamam'));
      this.docs.yenile();
      return true;
    } catch (e: unknown) {
      if (!this.session.handleSessionLoss(e)) {
        const error = apiHatasinaCevir(e);
        if (error.kod === 'dogrulama' || error.kod === 'bilinmeyen') this.toast.hata(error.detay);
      }
      return false;
    } finally {
      this.busyId.set(null);
    }
  }
}
