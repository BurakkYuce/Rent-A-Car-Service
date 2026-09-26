import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { FULL_PAGE_NAVIGATION } from '@core/form/kaydedilmemis-degisiklik';
import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Faturanın PDF adresi (sunucu ucu; SPA'ya yönlenmez). Geçersiz kimlikte `null`. */
export function invoicePdfPath(id: string | null | undefined): string | null {
  return id && UUID.test(id) ? `/faturalar/${id}/pdf` : null;
}

/**
 * `/app/faturalar/:id/yazdir` — Blazor `InvoicePrint` ile aynı: fatura çıktısının TEK kaynağı sunucunun QuestPDF ucu
 * (`GET /faturalar/{id}/pdf`). Tam sayfa gezinmeyle oraya gidilir; eski yer imleri çalışır. Sekme açmaz.
 */
@Component({
  selector: 'rc-invoice-print',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, TranslocoPipe],
  template: `
    <rc-sayfa-bandi [baslik]="'finansBelge.fatura.yazdirBaslik' | transloco" ikon="printer" />
    <div class="rc-sayfa">
      @if (path; as p) {
        <p>
          {{ 'finansBelge.fatura.yazdirAciliyor' | transloco }}
          <a [href]="p">{{ 'finansBelge.fatura.yazdirBaglanti' | transloco }}</a>
        </p>
      } @else {
        <p class="rc-form-mesaji rc-form-mesaji--hata" role="alert">
          {{ 'finansBelge.fatura.bulunamadi' | transloco }}
        </p>
      }
    </div>
  `,
})
export class InvoicePrint {
  protected readonly path = invoicePdfPath(inject(ActivatedRoute).snapshot.paramMap.get('id'));

  constructor() {
    if (this.path) inject(FULL_PAGE_NAVIGATION)(this.path);
  }
}
