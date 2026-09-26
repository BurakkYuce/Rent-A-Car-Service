import { ChangeDetectionStrategy, Component, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable, delay, of, throwError } from 'rxjs';
import { ApiHatasi } from '@core/api/api-hatasi';
import { pageLeaveGuard } from '@core/form/kaydedilmemis-degisiklik';
import { DefinitionCrud } from '@shared/form/tanim-crud/definition-crud';
import type {
  TanimAlani,
  DefinitionValue,
  DefinitionSource,
  DefinitionRow,
} from '@shared/form/tanim-crud/definition-source';

/**
 * Tanım CRUD vitrini, bellek içi kaynakla (F11'de `restTanimKaynagi('/api/ui/v1/...')`). Aynı kod
 * ikinci kez girilirse kaynak 400 `dogrulama` + `alanlar.Kod` döner: sunucu alan hatası yolu görünür.
 */
@Component({
  selector: 'rc-tanim-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DefinitionCrud],
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
      max-width: 60rem;
      min-width: 0;
      padding: var(--rc-bosluk-4);
    }
  `,
  template: `
    <div class="rc-sayfa-basligi">
      <h1>Tanım vitrini</h1>
      <a routerLink="/" class="rc-yazdirma-gizle">Ana sayfa</a>
    </div>
    <rc-tanim-crud baslik="Araç renkleri" [alanlar]="fields" [kaynak]="kaynak" />
  `,
})
export class DefinitionShowcase {
  private readonly crud = viewChild.required(DefinitionCrud);

  protected readonly fields: readonly TanimAlani[] = [
    { ad: 'kod', etiket: 'Kod', tur: 'metin', zorunlu: true, azamiUzunluk: 10 },
    { ad: 'ad', etiket: 'Ad', tur: 'metin', zorunlu: true, azamiUzunluk: 60 },
    { ad: 'sira', etiket: 'Sıra', tur: 'sayi' },
    { ad: 'aktif', etiket: 'Aktif', tur: 'onay' },
  ];
  protected readonly kaynak = memorySource([
    { id: '1', kod: 'BYZ', ad: 'Beyaz', sira: 1, aktif: true },
    { id: '2', kod: 'SYH', ad: 'Siyah', sira: 2, aktif: true },
    { id: '3', kod: 'GRI', ad: 'Gri', sira: 3, aktif: false },
  ]);

  constructor() {
    pageLeaveGuard(() => this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    return this.crud().hasUnsavedChanges();
  }
}

function memorySource(start: DefinitionRow[]): DefinitionSource {
  let rows = [...start];
  let counter = rows.length;
  const delayed = <T>(value: T): Observable<T> => of(value).pipe(delay(150));
  const codeConflicts = (value: DefinitionValue, excludedId?: string): boolean =>
    rows.some((s) => s['kod'] === value['kod'] && s.id !== excludedId);
  const conflict = (): Observable<never> =>
    throwError(
      () =>
        new ApiHatasi({
          status: 400,
          kod: 'dogrulama',
          detay: 'Bu kod zaten kullanılıyor.',
          alanlar: { Kod: ['Bu kod zaten kullanılıyor.'] },
        }),
    ).pipe(delay(150));
  return {
    listele: () => delayed([...rows]),
    olustur: (value) => {
      if (codeConflicts(value)) return conflict();
      counter += 1;
      rows = [...rows, { ...value, id: String(counter) }];
      return delayed(null);
    },
    guncelle: (id, value) => {
      if (codeConflicts(value, id)) return conflict();
      rows = rows.map((s) => (s.id === id ? { ...value, id } : s));
      return delayed(null);
    },
    sil: (id) => {
      rows = rows.filter((s) => s.id !== id);
      return delayed(null);
    },
  };
}
