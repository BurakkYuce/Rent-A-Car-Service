import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { TAM_SAYFA_GEZINMESI } from '@core/form/kaydedilmemis-degisiklik';

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
  imports: [TranslocoPipe],
  template: `
    <h1>{{ 'finansBelge.fatura.yazdirBaslik' | transloco }}</h1>
    @if (path; as p) {
      <p>
        {{ 'finansBelge.fatura.yazdirAciliyor' | transloco }}
        <a [href]="p">{{ 'finansBelge.fatura.yazdirBaglanti' | transloco }}</a>
      </p>
    } @else {
      <p role="alert">{{ 'finansBelge.fatura.bulunamadi' | transloco }}</p>
    }
  `,
})
export class InvoicePrint {
  protected readonly path = invoicePdfPath(inject(ActivatedRoute).snapshot.paramMap.get('id'));

  constructor() {
    if (this.path) inject(TAM_SAYFA_GEZINMESI)(this.path);
  }
}
