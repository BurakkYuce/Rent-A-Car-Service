import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { FULL_PAGE_NAVIGATION } from '@core/form/kaydedilmemis-degisiklik';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Kiranın sözleşme PDF adresi (sunucu ucu; SPA'ya yönlenmez). Geçersiz kimlikte `null`. */
export function contractPdfUrl(id: string | null | undefined): string | null {
  return id && UUID.test(id) ? `/kiralar/${id}/pdf` : null;
}

/**
 * `/app/kiralar/:id/yazdir` — Blazor `RentalPrint` ile aynı: kira sözleşmesi çıktısının TEK kaynağı
 * sunucunun QuestPDF ucu (`GET /kiralar/{id}/pdf`). Tam sayfa gezinmeyle oraya gidilir (SPA dışı);
 * eski yer imleri ve bağlantılar çalışmaya devam eder. Sekme açmaz.
 */
@Component({
  selector: 'rc-kira-yazdir',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    <h1>{{ 'kiraFormu.yazdir.baslik' | transloco }}</h1>
    @if (adres; as a) {
      <p>
        {{ 'kiraFormu.yazdir.aciliyor' | transloco }}
        <a [href]="a">{{ 'kiraFormu.yazdir.baglanti' | transloco }}</a>
      </p>
    } @else {
      <p role="alert">{{ 'kiraFormu.bulunamadi' | transloco }}</p>
    }
  `,
})
export class RentalPrint {
  protected readonly adres = contractPdfUrl(inject(ActivatedRoute).snapshot.paramMap.get('id'));

  constructor() {
    if (this.adres) inject(FULL_PAGE_NAVIGATION)(this.adres);
  }
}
