import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

import { customerLabelFetch } from './crm.store';

/**
 * URL süzgecindeki müşteri kimliğinin etiketi (paylaşılan bağlantı / yenileme): önce bellekte, yoksa sunucudan
 * (`GET /secim/musteri/{id}`; hata → geçici etiket, süzgeç yine kimlikle çalışır). Etiketler YALNIZ bellekte (KVKK).
 */
@Injectable()
export class CustomerFilterLabel {
  private readonly api = inject(ApiIstemcisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fallback = ceviriFonksiyonu()('crm.seciliMusteri');
  private readonly labels = signal<ReadonlyMap<string, string>>(new Map());
  private readonly requested = new Set<string>();

  label(id: string | undefined | null): SecimSecenegi | null {
    if (!id) return null;
    const known = this.labels().get(id);
    if (known === undefined) this.resolve(id);
    return { id, etiket: known ?? this.fallback };
  }

  remember(item: SecimSecenegi | null): void {
    if (!item || this.labels().get(item.id) === item.etiket) return;
    const next = new Map(this.labels());
    next.set(item.id, item.etiket);
    this.labels.set(next);
  }

  private resolve(id: string): void {
    if (this.requested.has(id)) return;
    this.requested.add(id);
    customerLabelFetch(this.api, id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (item) => queueMicrotask(() => this.remember(item)),
        error: () => undefined,
      });
  }
}
