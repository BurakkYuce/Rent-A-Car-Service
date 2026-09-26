import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormGroup } from '@angular/forms';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';

import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

type ImportCounts = Sema<'ImportCountsDto'>;
export type ImportKind = 'arac' | 'cari';

/** Sunucu sınırıyla aynı (5 MB); daha büyüğü gönderilmeden reddedilir. */
export const MAX_IMPORT_BYTES = 5 * 1024 * 1024;
const ACCEPT = '.xlsx,.xls,.csv';

/**
 * F11.2d Veri İçe Aktar (Blazor `IceAktar`, ManageUsers): araç ve müşteri listesi Excel/CSV'den. Müşteri TC/ehliyet/
 * pasaport sunucuda şifreli yazılır; tekrar eden plaka/TC atlanır. Sonuç yalnız sayaç + hata mesajı özeti (satır
 * etiketi/müşteri adı sunucudan gelmez). Seçilen dosya ve sonuç yalnız bellekte.
 */
@Component({
  selector: 'rc-import-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, FormHatalari, SayfaBandi],
  styleUrl: '../definitions.scss',
  templateUrl: './import-page.html',
})
export class ImportPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly accept = ACCEPT;
  protected readonly kinds: readonly ImportKind[] = ['arac', 'cari'];
  private readonly vehicleInput = viewChild<ElementRef<HTMLInputElement>>('aracDosya');
  private readonly customerInput = viewChild<ElementRef<HTMLInputElement>>('cariDosya');

  protected readonly files = { arac: signal<File | null>(null), cari: signal<File | null>(null) };
  protected readonly fileErrors = {
    arac: signal<string | null>(null),
    cari: signal<string | null>(null),
  };
  protected readonly results = {
    arac: signal<ImportCounts | null>(null),
    cari: signal<ImportCounts | null>(null),
  };
  private readonly forms = { arac: new FormGroup({}), cari: new FormGroup({}) };
  protected readonly submits = { arac: formGonderimi(), cari: formGonderimi() };

  constructor() {
    sayfaTerkKorumasi(() => this.kaydedilmemisDegisiklikVar());
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.files.arac() !== null || this.files.cari() !== null;
  }

  protected picked(kind: ImportKind): void {
    const f = this.inputOf(kind)?.files?.[0] ?? null;
    const tooBig = f !== null && f.size > MAX_IMPORT_BYTES;
    this.fileErrors[kind].set(tooBig ? this.t('tanimlar.import.cokBuyuk') : null);
    this.files[kind].set(tooBig ? null : f);
  }

  protected upload(kind: ImportKind): void {
    const f = this.files[kind]();
    if (!f) {
      this.fileErrors[kind].set(this.t('tanimlar.import.dosyaSecilmedi'));
      return;
    }
    this.fileErrors[kind].set(null);
    this.submits[kind].gonder(
      this.forms[kind],
      (key) => {
        const body = new FormData();
        body.append('dosya', f, f.name);
        return this.api.post<ImportCounts>(`/api/ui/v1/ice-aktar/${kind}`, body, {
          islemAnahtari: key,
        });
      },
      {
        basarili: (r) => {
          this.results[kind].set(r);
          this.files[kind].set(null);
          const el = this.inputOf(kind);
          if (el) el.value = '';
          this.toast.basari(this.t(`tanimlar.import.${kind}.tamam`));
        },
        // Sunucunun `errors[dosya]`'sı formda kontrol olmadığı için `genelHatalar`'a düşer (bölümün hata listesi).
      },
    );
  }

  private inputOf(kind: ImportKind): HTMLInputElement | undefined {
    return (kind === 'arac' ? this.vehicleInput() : this.customerInput())?.nativeElement;
  }
}
