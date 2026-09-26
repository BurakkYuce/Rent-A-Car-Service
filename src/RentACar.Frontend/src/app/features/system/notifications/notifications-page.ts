import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import type { Observable } from 'rxjs';

import { apiHatasinaCevir } from '@core/api/api-hatasi';
import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { ToastServisi } from '@core/geri-bildirim/toast-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { TarihPipe, TarihSaatPipe } from '@shared/bicim/bicim-pipe';
import { SayfaBandi } from '../../../kabuk/sayfa-bandi/sayfa-bandi';

type NotificationCenter = Sema<'NotificationCenterDto'>;
type NotificationItem = Sema<'NotificationDto'>;
type ReadFilter = 'hepsi' | 'okunmamis' | 'okunmus';

const ROOT = '/api/ui/v1/bildirimler' as const;

/** Süzgeç → `okundu` sorgusu (`hepsi` = gönderilmez). */
export function readParam(f: ReadFilter): boolean | null {
  return f === 'hepsi' ? null : f === 'okunmus';
}

/**
 * F11.2b bildirim merkezi (Blazor `BildirimMerkezi`; oturum yeter): vade özetleri, açık şikâyetler ve kalıcı
 * bildirimler (okundu işaretleme). Menü rozeti sunucudaki okunmamış sayısından; kabuk 5 dk'da bir tazeler.
 */
@Component({
  selector: 'rc-notifications-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SayfaBandi, RouterLink, TranslocoPipe, TarihPipe, TarihSaatPipe],
  styleUrl: '../system.scss',
  templateUrl: './notifications-page.html',
})
export class NotificationsPage {
  private readonly api = inject(ApiIstemcisi);
  private readonly toast = inject(ToastServisi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly t = ceviriFonksiyonu();

  protected readonly filter = signal<ReadFilter>('okunmamis');
  protected readonly filters: readonly ReadFilter[] = ['okunmamis', 'okunmus', 'hepsi'];
  protected readonly center = new TemelStore<NotificationCenter, ReadFilter>(
    (f) => this.api.get<NotificationCenter>(ROOT, { parametreler: { okundu: readParam(f) } }),
    { oncekiVeriyiKoru: true },
  );
  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  constructor() {
    this.center.yukle(this.filter());
  }

  protected setFilter(f: ReadFilter): void {
    this.filter.set(f);
    this.center.yukle(f);
  }

  protected markRead(n: NotificationItem): void {
    this.act(this.api.post(`${ROOT}/${encodeURIComponent(n.id)}/oku`, {}));
  }

  protected markAll(): void {
    this.act(this.api.post(`${ROOT}/hepsini-oku`, {}));
  }

  private act(request: Observable<unknown>): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.basari(this.t('sistem.bildirim.okundu'));
        this.center.yenile();
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.actionError.set(apiHatasinaCevir(e).detay);
      },
    });
  }
}
