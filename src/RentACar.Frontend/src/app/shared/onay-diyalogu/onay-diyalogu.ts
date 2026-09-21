import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import type { OnaySecenekleri } from '@core/geri-bildirim/onay-servisi';
import { Ikon } from '@shared/ikon/ikon';

/** `OnayServisi.sor` içeriği. Esc/perde → `undefined` (vazgeç), "Onayla" → `true`. */
@Component({
  selector: 'rc-onay-diyalogu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Ikon],
  template: `
    <div class="rc-diyalog">
      <header class="rc-diyalog__ust">
        <rc-ikon
          [ad]="veri.tehlikeli ? 'alert-triangle' : 'info-circle'"
          [boyut]="20"
          [class.tehlike]="veri.tehlikeli"
        />
        <h2 id="rc-onay-baslik">{{ veri.baslik }}</h2>
      </header>
      <p id="rc-onay-mesaj" class="rc-diyalog__govde">{{ veri.mesaj }}</p>
      <footer class="rc-diyalog__alt">
        <button type="button" class="rc-dugme rc-onay-iptal" (click)="ref.close(false)">
          {{ veri.iptalEtiketi ?? ('geriBildirim.onay.vazgec' | transloco) }}
        </button>
        <button
          type="button"
          class="rc-dugme rc-onay-onayla"
          [class.rc-dugme--birincil]="!veri.tehlikeli"
          [class.rc-dugme--tehlike]="veri.tehlikeli"
          (click)="ref.close(true)"
        >
          {{ veri.onayEtiketi ?? ('geriBildirim.onay.onayla' | transloco) }}
        </button>
      </footer>
    </div>
  `,
  styles: `
    .tehlike {
      color: var(--rc-hata-metin);
    }
  `,
})
export class OnayDiyalogu {
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);
  protected readonly veri = inject<OnaySecenekleri>(DIALOG_DATA);
}
