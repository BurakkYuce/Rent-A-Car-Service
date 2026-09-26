import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { OnayServisi } from '@core/geri-bildirim/onay-servisi';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemelStore } from '@core/veri/temel-store';
import { ParaPipe } from '@shared/bicim/bicim-pipe';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

type ListingRow = Sema<'ListingRowDto'>;
type ListingPage = Sema<'SayfaOfListingRowDto'>;
type Summary = Sema<'WebsiteSummaryDto'>;

export const LISTINGS_ROOT = '/api/ui/v1/web-sitesi' as const;
/** Fotoğrafsız ilan sitede görünmez: bu eksik rozeti tıklanınca özellik/foto adımına götürür (Blazor paritesi). */
export const MISSING_PHOTO = 'Foto yok';

/**
 * F11.2b web sitesi — ilanlar (Blazor `WebSiteHub`; OperationsWrite + web sitesi modülü). İlan listesi, yayına engel
 * eksikler, gizle/yayınla, onaylı sil. Modülü olmayan firmada ekran yalnız bilgi verir (uçlar zaten 404).
 */
@Component({
  selector: 'rc-website-hub',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SayfaBandi, RouterLink, TranslocoPipe, ParaPipe],
  styleUrl: '../system.scss',
  templateUrl: './website-hub.html',
})
export class WebsiteHub {
  private readonly api = inject(ApiIstemcisi);
  private readonly confirm = inject(OnayServisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  private readonly session = inject(OturumServisi);
  protected readonly module = computed(() => this.session.ben()?.moduller.webSitesi === true);
  protected readonly missingPhoto = MISSING_PHOTO;
  protected readonly summary = new TemelStore<Summary>(() =>
    this.api.get<Summary>(`${LISTINGS_ROOT}/ozet`),
  );
  protected readonly list = new TemelStore<ListingPage>(() =>
    this.api.get<ListingPage>(`${LISTINGS_ROOT}/ilanlar`, {
      parametreler: { boyut: 200, sirala: 'baslik' },
    }),
  );
  protected readonly rows = computed(() => this.list.veri()?.kayitlar ?? []);
  protected readonly busy = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  constructor() {
    if (this.module()) {
      this.summary.yukle();
      this.list.yukle();
    }
  }

  protected otherMissing(r: ListingRow): readonly string[] {
    return r.eksikler.filter((e) => e !== MISSING_PHOTO);
  }

  protected setStatus(r: ListingRow, durum: 'Yayinda' | 'Pasif'): void {
    this.act(
      r.id,
      this.api.post(`${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(r.id)}/durum`, { durum }),
    );
  }

  protected async remove(r: ListingRow): Promise<void> {
    const yes = await this.confirm.sor({
      baslik: this.t('sistem.web.silBaslik', { baslik: r.baslik }),
      mesaj: this.t('sistem.web.silMesaj'),
      onayEtiketi: this.t('sistem.ortak.sil'),
      tehlikeli: true,
    });
    if (yes) {
      this.act(r.id, this.api.delete(`${LISTINGS_ROOT}/ilanlar/${encodeURIComponent(r.id)}`));
    }
  }

  private act(id: string, request: Observable<unknown>): void {
    if (this.busy() !== null) return;
    this.busy.set(id);
    this.actionError.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(null);
        this.toast.basari(this.t('sistem.ortak.kaydedildi'));
        this.list.yenile();
        this.summary.yenile();
      },
      error: (e: unknown) => {
        this.busy.set(null);
        const h = apiHatasinaCevir(e);
        this.actionError.set(h.alanlar?.['durum']?.[0] ?? h.detay);
      },
    });
  }
}
