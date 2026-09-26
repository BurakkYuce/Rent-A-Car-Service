import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TranslocoPipe } from '@jsverse/transloco';

import {
  isUrgentState,
  type Toast,
  type ToastState,
  ToastService,
} from '@core/geri-bildirim/toast-service';
import { Icon } from '@shared/ikon/icon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

const ICON: Readonly<Record<ToastState, IkonAdi>> = {
  basari: 'circle-check',
  bilgi: 'info-circle',
  uyari: 'alert-triangle',
  hata: 'alert-circle',
  notr: 'info-circle',
  bekleme: 'loader-2',
};

/**
 * Toast yığını (sağ alt; dar ekranda tam genişlik). İki kalıcı canlı bölge: `hata`/`uyari`
 * `role="alert"` (hemen okunur), diğerleri `role="status"` (kibar). Bölgeler DOM'da hep durur ki
 * sonradan eklenen metin ekran okuyucuya ulaşsın. Üzerine gelince ya da odaklanınca süre durur.
 */
@Component({
  selector: 'rc-toast-alani',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, TranslocoPipe, NgTemplateOutlet],
  templateUrl: './toast-alani.html',
  styleUrl: './toast-alani.scss',
})
export class ToastAlani {
  protected readonly servis = inject(ToastService);
  protected readonly polite = computed(() =>
    this.servis.toasts().filter((t) => !isUrgentState(t.durum)),
  );
  protected readonly urgent = computed(() =>
    this.servis.toasts().filter((t) => isUrgentState(t.durum)),
  );

  protected ikon(toast: Toast): IkonAdi {
    return ICON[toast.durum];
  }
}
