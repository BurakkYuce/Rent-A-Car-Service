import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { Icon } from '@shared/ikon/icon';

/**
 * Sayfa üstü uyarı bandı (`UyariBandiServisi`). Form hatası değildir: izin yok, pilot değil, alansız
 * çakışma, `?hata=` mesajı. Canlı bölge hep DOM'da (`role="alert"`), içerik gelince okunur.
 */
@Component({
  selector: 'rc-uyari-bandi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, TranslocoPipe],
  template: `
    <div role="alert">
      @if (servis.bant(); as bant) {
        <div class="bant" [class]="'bant--' + bant.tur" [attr.data-kod]="bant.kod ?? null">
          <rc-ikon [ad]="bant.tur === 'bilgi' ? 'info-circle' : 'alert-triangle'" [boyut]="16" />
          <p class="mesaj">{{ bant.mesaj }}</p>
          <button
            type="button"
            class="rc-dugme rc-dugme--hayalet rc-dugme--ikon rc-dugme--kucuk"
            [attr.aria-label]="'geriBildirim.kapat' | transloco"
            (click)="servis.kapat()"
          >
            <rc-ikon ad="x" [boyut]="14" />
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .bant {
      --_metin: var(--rc-uyari-metin);
      --_zemin: var(--rc-uyari-zemin);
      --_kenar: var(--rc-uyari-kenar);
      display: flex;
      align-items: center;
      gap: var(--rc-bosluk-2);
      padding: var(--rc-bosluk-1) var(--rc-bosluk-2) var(--rc-bosluk-1) var(--rc-bosluk-4);
      border-block-end: 1px solid var(--_kenar);
      background-color: var(--_zemin);
      color: var(--_metin);
    }
    .bant--hata {
      --_metin: var(--rc-hata-metin);
      --_zemin: var(--rc-hata-zemin);
      --_kenar: var(--rc-hata-kenar);
    }
    .bant--bilgi {
      --_metin: var(--rc-bilgi-metin);
      --_zemin: var(--rc-bilgi-zemin);
      --_kenar: var(--rc-bilgi-kenar);
    }
    .mesaj {
      flex: 1;
      min-width: 0;
      padding-block: var(--rc-bosluk-1);
      overflow-wrap: anywhere;
    }
  `,
})
export class WarningBanner {
  protected readonly servis = inject(WarningBannerService);
}
