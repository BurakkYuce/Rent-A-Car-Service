import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormControl, FormGroup } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { sayfaTerkKorumasi } from '@core/form/kaydedilmemis-degisiklik';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemelStore } from '@core/veri/temel-store';
import { formGonderimi } from '@shared/form/form-gonderimi';
import { FormHatalari } from '@shared/form/form-hatalari';

import { PlateChipComponent } from '@shared/plaka/plaka';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';
import { LISTINGS_ROOT } from './website-hub';

type Cluster = Sema<'VehicleClusterDto'>;
type Created = Sema<'ListingCreatedDto'>;

export type ListingMode = 'beraber' | 'ayri';

/** Adım-1 gövdesi: beraber modda küme imzaları (bir küme = bir ilan), ayrı modda araç kimlikleri. */
export function listingCreateBody(mode: ListingMode, selected: ReadonlySet<string>) {
  const ids = [...selected];
  return mode === 'ayri' ? { mod: 'ayri', aracIdler: ids } : { mod: 'beraber', imzalar: ids };
}

/**
 * F11.2b ilan sihirbazı adım 1/3 (Blazor `AracEkle`): ilansız araç havuzu, "beraber" (aynı araçlar tek kart) ya da
 * "ayrı" (her araç ayrı kart) seçimi. Oluşturunca fiyat adımına geçer.
 */
@Component({
  selector: 'rc-listing-create',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SayfaBandi, PlateChipComponent, RouterLink, TranslocoPipe, FormHatalari],
  styleUrl: '../system.scss',
  templateUrl: './listing-create.html',
})
export class ListingCreate {
  private readonly api = inject(ApiIstemcisi);
  private readonly router = inject(Router);
  private readonly session = inject(OturumServisi);
  private readonly t = ceviriFonksiyonu();

  protected readonly module = computed(() => this.session.ben()?.moduller.webSitesi === true);
  protected readonly pool = new TemelStore<readonly Cluster[]>(() =>
    this.api.get<readonly Cluster[]>(`${LISTINGS_ROOT}/havuz`),
  );
  protected readonly mode = signal<ListingMode>('beraber');
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly submit = formGonderimi();
  /** Gönderim kilidi ve alan hatası yuvası için boş form (seçim signal'de). */
  private readonly form = new FormGroup({ secim: new FormControl<string | null>(null) });
  protected readonly selectionError = signal<string | null>(null);

  constructor() {
    sayfaTerkKorumasi(() => this.selected().size > 0);
    if (this.module()) this.pool.yukle();
  }

  kaydedilmemisDegisiklikVar(): boolean {
    return this.selected().size > 0;
  }

  protected setMode(mode: ListingMode): void {
    if (mode === this.mode()) return;
    this.mode.set(mode);
    this.selected.set(new Set());
  }

  protected toggle(key: string, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) next.add(key);
    else next.delete(key);
    this.selected.set(next);
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected next(): void {
    if (this.selected().size === 0) {
      this.selectionError.set(this.t('sistem.web.secimYok'));
      return;
    }
    this.selectionError.set(null);
    const body = listingCreateBody(this.mode(), this.selected());
    this.submit.gonder(
      this.form,
      (key) => this.api.post<Created>(`${LISTINGS_ROOT}/ilanlar`, body, { islemAnahtari: key }),
      {
        esleme: { imzalar: 'secim', aracIdler: 'secim', mod: 'secim' },
        basarili: (r) => {
          this.selected.set(new Set());
          void this.router.navigate(['/web-sitesi/ilan', r.id, 'fiyat']);
        },
        hata: (h) => {
          const message = h.alanlar?.['imzalar']?.[0] ?? h.alanlar?.['aracIdler']?.[0];
          if (message) this.selectionError.set(message);
        },
      },
    );
  }
}
