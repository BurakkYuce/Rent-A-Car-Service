import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { EmptyState } from '@shared/bos-durum/empty-state';

import { PageBand } from '../sayfa-bandi/page-band';

/**
 * Kabuk içindeki bilinmeyen adres (`**`). Adres korunur (kullanıcı ne yazdığını görür), tek eylem Panel'e dönüş.
 * Sunucu `/app` dışındaki bilinmeyen sayfa adresini `/app/panel?hata=bulunamadi`'ya gönderir (`Cutover.StatusTarget`);
 * `/app` altını SPA kendisi karşılar — bu sayfa o karşılık.
 */
@Component({
  selector: 'rc-bulunamadi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, EmptyState, RouterLink, TranslocoPipe],
  template: `
    <rc-sayfa-bandi [baslik]="'kabuk.bulunamadi.baslik' | transloco" ikon="alert-circle" />
    <div class="rc-sayfa">
      <rc-bos-durum
        ikon="search"
        [baslik]="'kabuk.bulunamadi.ozet' | transloco"
        [aciklama]="'kabuk.bulunamadi.aciklama' | transloco"
      >
        <a class="rc-dugme rc-dugme--birincil" routerLink="/panel">{{
          'kabuk.bulunamadi.panelaDon' | transloco
        }}</a>
      </rc-bos-durum>
    </div>
  `,
})
export class NotFoundPage {}
