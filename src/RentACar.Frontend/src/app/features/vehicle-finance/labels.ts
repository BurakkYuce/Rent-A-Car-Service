import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import { translationFunction } from '@core/i18n/ceviri';
import { requestContext } from '@core/oturum/request-context';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

type Kind = 'musteri' | 'arac';

/**
 * URL süzgecindeki kimliğin (paylaşılan bağlantı / yenileme) seçim etiketi: önce bellekte, yoksa sunucudan
 * (`GET /secim/{tur}/{id}`; hata → geçici etiket, süzgeç yine kimlikle çalışır). Etiketler YALNIZ bellekte.
 */
class LabelCache {
  private readonly labels = signal<ReadonlyMap<string, string>>(new Map());
  private readonly requested = new Set<string>();

  constructor(
    private readonly api: ApiIstemcisi,
    private readonly destroyRef: DestroyRef,
    private readonly kind: Kind,
    private readonly fallback: string,
  ) {}

  /** Seçim öğesi (etiket çözülmediyse geçici etiketle); kimlik yoksa `null`. Signal okur (effect içinde çağrılır). */
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
    this.api
      .get<SecimSecenegi>(`/api/ui/v1/secim/${this.kind}/${encodeURIComponent(id)}`, {
        context: requestContext({ sessiz: true }),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (item) => queueMicrotask(() => this.remember(item)),
        error: () => undefined,
      });
  }
}

@Injectable()
export class CustomerLabels extends LabelCache {
  constructor() {
    super(
      inject(ApiIstemcisi),
      inject(DestroyRef),
      'musteri',
      translationFunction()('aracFinans.seciliMusteri'),
    );
  }
}

@Injectable()
export class VehicleLabels extends LabelCache {
  constructor() {
    super(
      inject(ApiIstemcisi),
      inject(DestroyRef),
      'arac',
      translationFunction()('aracFinans.seciliArac'),
    );
  }
}
